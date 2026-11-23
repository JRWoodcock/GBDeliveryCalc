using System.Text.Json;
using GBDeliveryCalc.Core.Integrations;

namespace GBDeliveryCalc.Services.Google;

/// <summary>Address lookup with Places API (New) Text Search.</summary>
public sealed class GooglePlaceSearch(GoogleApiClient client) : IPlaceSearchProvider
{
    public const string Url = "https://places.googleapis.com/v1/places:searchText";
    public const string FieldMask = "places.id,places.displayName,places.formattedAddress";
    public const string ProviderName = "Google Places API";

    public ProviderInfo Info { get; } = new(ProviderName, ProviderStatus.Available,
        "Address lookup with your Google Maps Platform key.", PermitsRetention: false);

    public async Task<PlaceSearchResult> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new PlaceSearchResult { ProviderName = ProviderName };
        using var doc = await client.PostAsync(Url, FieldMask, new { textQuery = query.Trim(), regionCode = "US" }, "Address lookup", ct)
            .ConfigureAwait(false);

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ProviderUnavailableException("Address lookup: Google sent a response the app could not read.");

        var candidates = new List<PlaceCandidate>();
        // Google omits "places" entirely when nothing matches; the screen then says "No matches".
        if (root.TryGetProperty("places", out var places) && places.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in places.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object) continue;
                var address = Str(p, "formattedAddress");
                if (string.IsNullOrWhiteSpace(address)) continue;
                var name = p.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.Object ? Str(dn, "text") : null;
                candidates.Add(new PlaceCandidate
                {
                    DisplayName = string.IsNullOrWhiteSpace(name) ? address : name,
                    FormattedAddress = address,
                    ProviderPlaceId = Str(p, "id"),
                });
            }
        }
        return new PlaceSearchResult { Candidates = candidates, ProviderName = ProviderName, PermitsRetention = false };
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
