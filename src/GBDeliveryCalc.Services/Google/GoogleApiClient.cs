using System.Net;
using System.Text;
using System.Text.Json;
using GBDeliveryCalc.Core.Integrations;

namespace GBDeliveryCalc.Services.Google;

/// <summary>
/// Shared plumbing for Google Maps Platform calls. The API key travels only in the X-Goog-Api-Key header: it is never
/// put in a URL, and every error message is built here without it (Google's own error text is scrubbed of it too).
/// </summary>
public sealed class GoogleApiClient
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public GoogleApiClient(HttpClient http, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("An API key is required.", nameof(apiKey));
        _http = http;
        _apiKey = apiKey.Trim();
    }

    /// <summary>POSTs <paramref name="body"/> as JSON and returns the parsed response document.</summary>
    public async Task<JsonDocument> PostAsync(string url, string fieldMask, object body, string what, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", _apiKey);
        request.Headers.TryAddWithoutValidation("X-Goog-FieldMask", fieldMask);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false); }
        catch (HttpRequestException) { throw new ProviderUnavailableException($"{what}: could not reach Google. Check the internet connection."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ProviderUnavailableException($"{what}: Google did not answer within {Timeout.TotalSeconds:0} seconds."); }

        using (response)
        {
            string text;
            try { text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            { throw new ProviderUnavailableException($"{what}: the response from Google was cut off."); }

            if (!response.IsSuccessStatusCode)
                throw new ProviderUnavailableException($"{what}: {Describe(response.StatusCode)}{GoogleDetail(text)}");
            try { return JsonDocument.Parse(text); }
            catch (JsonException) { throw new ProviderUnavailableException($"{what}: Google sent a response the app could not read."); }
        }
    }

    private static string Describe(HttpStatusCode code) => (int)code switch
    {
        400 => "Google rejected the request (400). If the key was just entered, check that it was copied correctly.",
        401 or 403 => $"Google refused the API key ({(int)code}). Check that the key is correct, that Places API (New) and Routes API are enabled for it, and that billing is on.",
        429 => "Google's quota for this key is used up (429). Enter figures manually, or raise the quota in Google Cloud Console.",
        >= 500 => $"Google's service had a problem ({(int)code}). Try again in a few minutes.",
        _ => $"Google returned an unexpected status ({(int)code}).",
    };

    /// <summary>Google's own error message, if any, scrubbed of the key and shortened.</summary>
    private string GoogleDetail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object &&
                err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
            {
                var msg = Scrub(m.GetString() ?? "").Trim();
                if (msg.Length > 200) msg = msg[..200] + "…";
                return msg.Length == 0 ? "" : $" Google said: \"{msg}\"";
            }
        }
        catch (JsonException) { }
        return "";
    }

    /// <summary>Removes the key from any text before it can reach the UI.</summary>
    private string Scrub(string text) => text.Replace(_apiKey, "[key]", StringComparison.Ordinal);
}
