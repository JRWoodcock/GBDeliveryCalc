using System.Globalization;
using System.Text.Json;
using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Core.Model;

namespace GBDeliveryCalc.Services.Google;

/// <summary>Driving miles and time with the Routes API (computeRoutes). Round trips make a second call for the return leg.</summary>
public sealed class GoogleRouting(GoogleApiClient client) : IRoutingProvider
{
    public const string Url = "https://routes.googleapis.com/directions/v2:computeRoutes";
    public const string FieldMask = "routes.distanceMeters,routes.duration";
    public const string ProviderName = "Google Routes API";
    private const decimal MetersPerMile = 1609.344m;

    public ProviderInfo Info { get; } = new(ProviderName, ProviderStatus.Available,
        "Driving miles and time with your Google Maps Platform key.", PermitsRetention: false);

    public async Task<RouteLookupResult> GetRouteAsync(string origin, string destination, TripType tripType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination))
            throw new ProviderUnavailableException("Route lookup: enter both a starting address and a destination.");

        var outbound = await LegAsync(origin, destination, ct).ConfigureAwait(false)
            ?? throw new ProviderUnavailableException("Route lookup: Google found no driving route between these addresses. Check both addresses.");

        RouteLeg? back = null;
        if (tripType == TripType.RoundTrip)
        {
            // A failed return leg is not fatal: the caller doubles the outbound leg and labels it.
            try { back = await LegAsync(destination, origin, ct).ConfigureAwait(false); }
            catch (ProviderUnavailableException) { back = null; }
        }

        return new RouteLookupResult { Outbound = outbound, Return = back, ProviderName = ProviderName, PermitsRetention = false };
    }

    private async Task<RouteLeg?> LegAsync(string from, string to, CancellationToken ct)
    {
        var body = new
        {
            origin = new { address = from.Trim() },
            destination = new { address = to.Trim() },
            travelMode = "DRIVE",
            routingPreference = "TRAFFIC_UNAWARE",
        };
        using var doc = await client.PostAsync(Url, FieldMask, body, "Route lookup", ct).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ProviderUnavailableException("Route lookup: Google sent a response the app could not read.");
        // Google returns {} when it cannot route between the two places.
        if (!root.TryGetProperty("routes", out var routes) || routes.ValueKind != JsonValueKind.Array || routes.GetArrayLength() == 0)
            return null;

        var route = routes[0];
        var meters = route.ValueKind == JsonValueKind.Object && route.TryGetProperty("distanceMeters", out var d) && d.ValueKind == JsonValueKind.Number
            ? d.GetDecimal() : 0m;
        var seconds = route.ValueKind == JsonValueKind.Object && route.TryGetProperty("duration", out var t) && t.ValueKind == JsonValueKind.String
            ? ParseSeconds(t.GetString()) : null;
        if (meters <= 0 || seconds is null)
            throw new ProviderUnavailableException("Route lookup: Google's route had no distance or duration.");
        return new RouteLeg(MetersToMiles(meters), SecondsToMinutes(seconds.Value));
    }

    /// <summary>Meters to miles, rounded to 0.1 mile.</summary>
    public static decimal MetersToMiles(decimal meters) => Math.Round(meters / MetersPerMile, 1, MidpointRounding.AwayFromZero);

    /// <summary>Seconds to the nearest whole minute.</summary>
    public static int SecondsToMinutes(decimal seconds) => (int)Math.Round(seconds / 60m, 0, MidpointRounding.AwayFromZero);

    /// <summary>Parses Google's duration format, e.g. "8040s" or "8040.5s".</summary>
    public static decimal? ParseSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.EndsWith('s')) return null;
        return decimal.TryParse(value[..^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var s) && s >= 0 ? s : null;
    }
}
