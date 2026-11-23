using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Services.Google;
using GBDeliveryCalc.Services.OilPriceApi;

namespace GBDeliveryCalc.Services;

/// <summary>The address, routing and fuel providers in use.</summary>
public sealed record IntegrationSet(IPlaceSearchProvider Places, IRoutingProvider Routing, IFuelPriceSource Fuel);

/// <summary>API keys saved on this PC. Null or blank means that integration is off.</summary>
public sealed record IntegrationKeys(string? Google = null, string? OilPriceApi = null);

/// <summary>
/// Chooses adapters. With a Google Maps Platform key, address and route lookups call Google directly from this PC;
/// with an OilPriceAPI key, the state average fuel price is fetched from OilPriceAPI. Without a key, that
/// integration is "not configured" and staff enter the values manually. No adapter ever returns sample data.
/// </summary>
public static class ProviderFactory
{
    public const string NoKeyReason =
        "Not configured: add a Google Maps Platform API key in Settings → API keys → Google lookups. Enter miles and drive time manually.";

    public const string NoFuelKeyReason =
        "Not configured: add an OilPriceAPI key in Settings → API keys → Fuel prices, or enter the price manually.";

    // One HttpClient for the app's lifetime. Each adapter applies its own 15-second timeout per request.
    private static readonly Lazy<HttpClient> SharedHttp = new(() => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });

    public static IntegrationSet Create(string? googleApiKey, HttpClient? http = null) => Create(new IntegrationKeys(googleApiKey), http);

    public static IntegrationSet Create(IntegrationKeys keys, HttpClient? http = null)
    {
        var client = http ?? SharedHttp.Value;

        IFuelPriceSource fuel = string.IsNullOrWhiteSpace(keys.OilPriceApi)
            ? new NotConfiguredFuelPriceSource(NoFuelKeyReason)
            : new OilPriceApiFuelSource(client, keys.OilPriceApi);

        if (string.IsNullOrWhiteSpace(keys.Google))
            return new IntegrationSet(new NotConfiguredPlaceSearch(NoKeyReason), new NotConfiguredRouting(NoKeyReason), fuel);

        var google = new GoogleApiClient(client, keys.Google);
        return new IntegrationSet(new GooglePlaceSearch(google), new GoogleRouting(google), fuel);
    }
}
