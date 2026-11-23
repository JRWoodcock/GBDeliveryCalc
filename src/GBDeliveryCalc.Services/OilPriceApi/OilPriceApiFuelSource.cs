using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Core.Model;

namespace GBDeliveryCalc.Services.OilPriceApi;

/// <summary>
/// Statewide average retail fuel price (AAA data) for any US state, from OilPriceAPI's latest-price endpoint.
/// The key is sent only in the Authorization header and is scrubbed from every message this class produces.
/// </summary>
public sealed class OilPriceApiFuelSource : IFuelPriceSource
{
    public const string BaseUrl = "https://api.oilpriceapi.com/v1/prices/latest";
    /// <summary>Mississippi series codes (the default state).</summary>
    public const string RegularCode = "GASOLINE_RETAIL_STATE_MS_USD";
    public const string DieselCode = "DIESEL_RETAIL_STATE_MS_USD";
    public const string ProviderName = "OilPriceAPI (state AAA average)";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly TimeZoneInfo _localZone;

    public OilPriceApiFuelSource(HttpClient http, string apiKey, TimeZoneInfo? localZone = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("An API key is required.", nameof(apiKey));
        _http = http;
        _apiKey = apiKey.Trim();
        _localZone = localZone ?? TimeZoneInfo.Local;
    }

    // Retention: the price is stored with saved estimates, which the plan's commercial-use grant covers (docs/FUEL_PRICES.md).
    public ProviderInfo Info { get; } = new(ProviderName, ProviderStatus.Available,
        "Statewide average (AAA) for the shop's state via your OilPriceAPI key.", PermitsRetention: true);

    /// <summary>OilPriceAPI series code, e.g. GASOLINE_RETAIL_STATE_AL_USD.</summary>
    public static string CodeFor(FuelType fuelType, string state = UsStates.Default) =>
        $"{(fuelType == FuelType.Diesel ? "DIESEL" : "GASOLINE")}_RETAIL_STATE_{state.Trim().ToUpperInvariant()}_USD";

    public async Task<FuelPrice> GetStateAverageAsync(FuelType fuelType, string state = UsStates.Default, CancellationToken ct = default)
    {
        state = string.IsNullOrWhiteSpace(state) ? UsStates.Default : state.Trim().ToUpperInvariant();
        if (!UsStates.Names.ContainsKey(state)) throw new ProviderUnavailableException($"Fuel price: '{state}' isn't a US state code.");
        var code = CodeFor(fuelType, state);
        var what = $"Fuel price ({fuelType.Label()}, {UsStates.Name(state)})";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}?by_code={code}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        string text;
        int status;
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            status = (int)response.StatusCode;
            text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException) { throw new ProviderUnavailableException($"{what}: could not reach OilPriceAPI. Check the internet connection."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ProviderUnavailableException($"{what}: OilPriceAPI did not answer within {Timeout.TotalSeconds:0} seconds."); }

        if (status is < 200 or > 299)
            throw new ProviderUnavailableException($"{what}: {Describe(status)}{Detail(text)}");

        JsonElement data;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(text); }
        catch (JsonException) { throw new ProviderUnavailableException($"{what}: OilPriceAPI sent a response the app could not read."); }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("data", out data) || data.ValueKind != JsonValueKind.Object)
                throw new ProviderUnavailableException($"{what}: OilPriceAPI sent a response the app could not read.");

            var returnedCode = Str(data, "code");
            if (!string.Equals(returnedCode, code, StringComparison.OrdinalIgnoreCase))
                throw new ProviderUnavailableException($"{what}: OilPriceAPI returned a different series ({Scrub(returnedCode ?? "none")}) than requested ({code}).");

            if (!data.TryGetProperty("price", out var p) || p.ValueKind != JsonValueKind.Number || !p.TryGetDecimal(out var price) || price <= 0m)
                throw new ProviderUnavailableException($"{what}: OilPriceAPI returned no usable price.");

            var currency = Str(data, "currency");
            if (currency is not null && !currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
                throw new ProviderUnavailableException($"{what}: OilPriceAPI returned a price in {Scrub(currency)}, not USD.");
            var unit = Str(data, "unit");
            if (unit is not null && !unit.Equals("gallon", StringComparison.OrdinalIgnoreCase))
                throw new ProviderUnavailableException($"{what}: OilPriceAPI returned a price per {Scrub(unit)}, not per gallon.");

            var source = Str(data, "source");
            return new FuelPrice
            {
                FuelType = fuelType,
                State = state,
                PricePerGallon = price,
                Source = SourceLabel(source, state),
                PublishedDate = PublishedDate(data),
                IsLive = IsCurrent(data),
            };
        }
    }

    /// <summary>"aaa" and "AL" → "AAA Alabama average via OilPriceAPI".</summary>
    public static string SourceLabel(string? source, string state = UsStates.Default) =>
        string.IsNullOrWhiteSpace(source) || source.Equals("aaa", StringComparison.OrdinalIgnoreCase)
            ? $"AAA {UsStates.Name(state)} average via OilPriceAPI"
            : $"{source.Trim()} {UsStates.Name(state)} via OilPriceAPI";

    /// <summary>Live only when OilPriceAPI says the value is current and not stale.</summary>
    private static bool IsCurrent(JsonElement data)
    {
        if (data.TryGetProperty("stale", out var stale) && stale.ValueKind == JsonValueKind.True) return false;
        if (data.TryGetProperty("synthetic", out var syn) && syn.ValueKind == JsonValueKind.True) return false;
        var status = Str(data, "data_status");
        if (status is null && data.TryGetProperty("freshness", out var f) && f.ValueKind == JsonValueKind.Object) status = Str(f, "status");
        return status is not null && (status.Equals("current", StringComparison.OrdinalIgnoreCase) || status.Equals("fresh", StringComparison.OrdinalIgnoreCase));
    }

    private DateOnly? PublishedDate(JsonElement data)
    {
        foreach (var name in new[] { "as_of", "observed_at", "created_at", "updated_at" })
        {
            if (Str(data, name) is { } s &&
                DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t))
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t, _localZone).DateTime);
        }
        return null;
    }

    private static string Describe(int code) => code switch
    {
        401 => "OilPriceAPI refused the key (401). Check that it was copied correctly and is still active.",
        402 or 403 => $"OilPriceAPI says this key's plan doesn't allow this data ({code}). Check the plan in the OilPriceAPI dashboard.",
        404 => "OilPriceAPI has no price series for this state and fuel (404). Enter the price manually.",
        429 => "OilPriceAPI's request limit for this key is used up (429). Enter the price manually or try later.",
        >= 500 => $"OilPriceAPI had a problem ({code}). Try again in a few minutes.",
        _ => $"OilPriceAPI returned an unexpected status ({code}).",
    };

    /// <summary>OilPriceAPI's own error text, if any, scrubbed of the key and shortened.</summary>
    private string Detail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return "";
            var msg = Str(root, "message") ?? Str(root, "error");
            if (msg is null && root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object) msg = Str(e, "message");
            if (string.IsNullOrWhiteSpace(msg)) return "";
            msg = Scrub(msg).Trim();
            if (msg.Length > 200) msg = msg[..200] + "…";
            return $" OilPriceAPI said: \"{msg}\"";
        }
        catch (JsonException) { return ""; }
    }

    private string Scrub(string text) => text.Replace(_apiKey, "[key]", StringComparison.Ordinal);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
