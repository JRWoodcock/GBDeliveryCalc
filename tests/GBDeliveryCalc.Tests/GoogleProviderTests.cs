using System.Net;
using System.Text;
using System.Text.Json;
using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Core.Model;
using GBDeliveryCalc.Services;
using GBDeliveryCalc.Services.Google;

namespace GBDeliveryCalc.Tests;

/// <summary>A request as the handler saw it (captured before the provider disposes the request).</summary>
internal sealed record SeenRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body)
{
    public JsonElement Json => JsonDocument.Parse(Body).RootElement;
}

internal class GoogleStub(Func<SeenRequest, int, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<SeenRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var seen = new SeenRequest(request.Method, request.RequestUri!, headers, body);
        Requests.Add(seen);
        return respond(seen, Requests.Count);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Hangs until cancelled, to exercise the timeout path.</summary>
    public static GoogleStub Hanging() => new HangingStub();

    private sealed class HangingStub() : GoogleStub((_, _) => throw new InvalidOperationException())
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        }
    }
}

public class GoogleProviderTests
{
    private const string Key = "AIzaTEST-not-a-real-key-1234567890abcd";

    private const string PlacesJson = """
        {"places":[{"id":"ChIJabc","formattedAddress":"1125 Golf Course Rd, Philadelphia, MS 39350, USA","displayName":{"text":"Neshoba Central High School","languageCode":"en"}}]}
        """;

    private static string RouteJson(int meters, string duration) =>
        $$"""{"routes":[{"distanceMeters":{{meters}},"duration":"{{duration}}"}]}""";

    private static IntegrationSet Google(GoogleStub stub) => ProviderFactory.Create(Key, new HttpClient(stub));

    // ---------------- factory ----------------
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task No_key_means_not_configured_and_never_sample_data(string? key)
    {
        var set = ProviderFactory.Create(key);
        Assert.Equal(ProviderStatus.NotConfigured, set.Places.Info.Status);
        Assert.Equal(ProviderStatus.NotConfigured, set.Routing.Info.Status);
        Assert.Equal(ProviderStatus.NotConfigured, set.Fuel.Info.Status);
        Assert.Contains("Settings → API keys → Google lookups", set.Routing.Info.StatusMessage);
        Assert.Contains("manually", set.Places.Info.StatusMessage);
        await Assert.ThrowsAsync<ProviderUnavailableException>(() => set.Places.SearchAsync("Neshoba Central High School"));
        await Assert.ThrowsAsync<ProviderUnavailableException>(() => set.Routing.GetRouteAsync("a", "b", TripType.RoundTrip));
        await Assert.ThrowsAsync<ProviderUnavailableException>(() => set.Fuel.GetStateAverageAsync(FuelType.Diesel));
    }

    [Fact]
    public void Key_means_google_providers_that_forbid_retention()
    {
        var set = ProviderFactory.Create(Key);
        Assert.IsType<GooglePlaceSearch>(set.Places);
        Assert.IsType<GoogleRouting>(set.Routing);
        Assert.Equal(ProviderStatus.Available, set.Places.Info.Status);
        Assert.False(set.Places.Info.PermitsRetention);
        Assert.False(set.Routing.Info.PermitsRetention);
        Assert.Equal(ProviderStatus.NotConfigured, set.Fuel.Info.Status);
    }

    // ---------------- places ----------------
    [Fact]
    public async Task Place_search_sends_text_search_request_with_key_only_in_header()
    {
        var stub = new GoogleStub((_, _) => GoogleStub.Json(PlacesJson));
        var r = await Google(stub).Places.SearchAsync("  Neshoba Central High School ");

        var req = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://places.googleapis.com/v1/places:searchText", req.Uri.AbsoluteUri);
        Assert.DoesNotContain(Key, req.Uri.AbsoluteUri);
        Assert.Equal(Key, req.Headers["X-Goog-Api-Key"]);
        Assert.Equal("places.id,places.displayName,places.formattedAddress", req.Headers["X-Goog-FieldMask"]);
        Assert.Equal("Neshoba Central High School", req.Json.GetProperty("textQuery").GetString());
        Assert.Equal("US", req.Json.GetProperty("regionCode").GetString());
        Assert.DoesNotContain(Key, req.Body);

        var c = Assert.Single(r.Candidates);
        Assert.Equal("Neshoba Central High School", c.DisplayName);
        Assert.Equal("1125 Golf Course Rd, Philadelphia, MS 39350, USA", c.FormattedAddress);
        Assert.Equal("ChIJabc", c.ProviderPlaceId);
        Assert.Equal("Google Places API", r.ProviderName);
        Assert.False(r.PermitsRetention);
    }

    [Fact]
    public async Task Place_search_with_no_matches_returns_empty_list()
    {
        var r = await Google(new GoogleStub((_, _) => GoogleStub.Json("{}"))).Places.SearchAsync("zzzz nowhere");
        Assert.Empty(r.Candidates);
    }

    [Fact]
    public async Task Place_without_display_name_falls_back_to_address()
    {
        var r = await Google(new GoogleStub((_, _) => GoogleStub.Json("""{"places":[{"id":"x","formattedAddress":"Hattiesburg, MS, USA"}]}""")))
            .Places.SearchAsync("Hattiesburg");
        Assert.Equal("Hattiesburg, MS, USA", Assert.Single(r.Candidates).DisplayName);
    }

    // ---------------- routes ----------------
    [Fact]
    public async Task Round_trip_makes_two_route_calls_in_opposite_directions()
    {
        var stub = new GoogleStub((_, n) => GoogleStub.Json(n == 1 ? RouteJson(201_810, "8040s") : RouteJson(202_900, "8190s")));
        var r = await Google(stub).Routing.GetRouteAsync("Hattiesburg, MS", "Philadelphia, MS", TripType.RoundTrip);

        Assert.Equal(2, stub.Requests.Count);
        foreach (var req in stub.Requests)
        {
            Assert.Equal("https://routes.googleapis.com/directions/v2:computeRoutes", req.Uri.AbsoluteUri);
            Assert.Equal(Key, req.Headers["X-Goog-Api-Key"]);
            Assert.Equal("routes.distanceMeters,routes.duration", req.Headers["X-Goog-FieldMask"]);
            Assert.Equal("DRIVE", req.Json.GetProperty("travelMode").GetString());
            Assert.Equal("TRAFFIC_UNAWARE", req.Json.GetProperty("routingPreference").GetString());
            Assert.DoesNotContain(Key, req.Uri.AbsoluteUri);
        }
        Assert.Equal("Hattiesburg, MS", stub.Requests[0].Json.GetProperty("origin").GetProperty("address").GetString());
        Assert.Equal("Philadelphia, MS", stub.Requests[0].Json.GetProperty("destination").GetProperty("address").GetString());
        Assert.Equal("Philadelphia, MS", stub.Requests[1].Json.GetProperty("origin").GetProperty("address").GetString());
        Assert.Equal("Hattiesburg, MS", stub.Requests[1].Json.GetProperty("destination").GetProperty("address").GetString());

        Assert.Equal(125.4m, r.Outbound.Miles);   // 201,810 m / 1609.344 = 125.40
        Assert.Equal(134, r.Outbound.DriveMinutes); // 8040 s = 134 min
        Assert.Equal(126.1m, r.Return!.Miles);    // 202,900 m = 126.08
        Assert.Equal(137, r.Return.DriveMinutes); // 8190 s = 136.5 → 137
        Assert.Equal("Google Routes API", r.ProviderName);
        Assert.False(r.PermitsRetention);
    }

    [Fact]
    public async Task One_way_makes_a_single_call()
    {
        var stub = new GoogleStub((_, _) => GoogleStub.Json(RouteJson(16_093, "1200s")));
        var r = await Google(stub).Routing.GetRouteAsync("a", "b", TripType.OneWay);
        Assert.Single(stub.Requests);
        Assert.Null(r.Return);
        Assert.Equal(10.0m, r.Outbound.Miles);
        Assert.Equal(20, r.Outbound.DriveMinutes);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(403)]
    [InlineData(-1)] // network error
    [InlineData(0)]  // {} = no route
    public async Task Failed_return_leg_is_null_so_caller_doubles_and_labels(int secondCall)
    {
        var stub = new GoogleStub((_, n) =>
        {
            if (n == 1) return GoogleStub.Json(RouteJson(201_168, "8040s"));
            if (secondCall == -1) throw new HttpRequestException("reset");
            if (secondCall == 0) return GoogleStub.Json("{}");
            return GoogleStub.Json("{}", (HttpStatusCode)secondCall);
        });
        var r = await Google(stub).Routing.GetRouteAsync("a", "b", TripType.RoundTrip);
        Assert.Null(r.Return);
        var fig = RouteFigures.FromLookup(TripType.RoundTrip, r.Outbound, r.Return, new RouteKey("a", "b", TripType.RoundTrip), r.ProviderName, r.PermitsRetention);
        Assert.True(fig.ReturnEstimatedByDoubling);
        Assert.Equal(250m, fig.TotalMiles);
        Assert.Equal(268, fig.TotalDriveMinutes);
    }

    [Fact]
    public async Task No_outbound_route_is_an_error()
    {
        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(() =>
            Google(new GoogleStub((_, _) => GoogleStub.Json("{}"))).Routing.GetRouteAsync("a", "b", TripType.OneWay));
        Assert.Contains("no driving route", ex.Message);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1609.344, 1.0)]
    [InlineData(80467.2, 50.0)]
    [InlineData(160, 0.1)]   // 0.0994 → 0.1
    [InlineData(1690, 1.1)]  // 1.0501 → 1.1
    public void Meters_to_miles_rounds_to_a_tenth(double meters, double miles) =>
        Assert.Equal((decimal)miles, GoogleRouting.MetersToMiles((decimal)meters));

    [Theory]
    [InlineData("0s", 0)]
    [InlineData("29s", 0)]
    [InlineData("30s", 1)]
    [InlineData("8040s", 134)]
    [InlineData("8069.9s", 134)]
    [InlineData("8070s", 135)]
    public void Duration_parses_and_rounds_to_the_minute(string duration, int minutes) =>
        Assert.Equal(minutes, GoogleRouting.SecondsToMinutes(GoogleRouting.ParseSeconds(duration)!.Value));

    [Theory]
    [InlineData("")]
    [InlineData("8040")]
    [InlineData("abc s")]
    [InlineData("-5s")]
    public void Bad_duration_is_rejected(string duration) => Assert.Null(GoogleRouting.ParseSeconds(duration));

    [Fact]
    public async Task Route_without_duration_is_an_error()
    {
        await Assert.ThrowsAsync<ProviderUnavailableException>(() =>
            Google(new GoogleStub((_, _) => GoogleStub.Json("""{"routes":[{"distanceMeters":1000}]}""")))
                .Routing.GetRouteAsync("a", "b", TripType.OneWay));
    }

    // ---------------- failures ----------------
    public static TheoryData<int, string> Failures => new()
    {
        { 400, $$$"""{"error":{"code":400,"message":"API key not valid. Please pass a valid API key. ({{{Key}}})","status":"INVALID_ARGUMENT"}}""" },
        { 403, $$$"""{"error":{"code":403,"message":"Requests from this key {{{Key}}} are blocked.","status":"PERMISSION_DENIED"}}""" },
        { 429, """{"error":{"code":429,"message":"Quota exceeded","status":"RESOURCE_EXHAUSTED"}}""" },
        { 500, "<html>oops</html>" },
        { 200, "not json" },
        { 200, "[1,2,3]" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Google_failures_become_ProviderUnavailable_without_the_key(int code, string body)
    {
        var set = Google(new GoogleStub((_, _) => GoogleStub.Json(body, (HttpStatusCode)code)));
        var routeEx = await Assert.ThrowsAsync<ProviderUnavailableException>(() => set.Routing.GetRouteAsync("a", "b", TripType.OneWay));
        var placeEx = await Assert.ThrowsAsync<ProviderUnavailableException>(() => set.Places.SearchAsync("x"));
        foreach (var ex in new[] { routeEx, placeEx })
        {
            Assert.DoesNotContain(Key, ex.ToString());
            Assert.Null(ex.InnerException);
            if (code != 200) Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), ex.Message);
        }
    }

    [Fact]
    public async Task Refused_key_message_points_at_the_fix()
    {
        var set = Google(new GoogleStub((_, _) => GoogleStub.Json("""{"error":{"message":"denied"}}""", HttpStatusCode.Forbidden)));
        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(() => set.Places.SearchAsync("x"));
        Assert.Contains("refused the API key", ex.Message);
        Assert.Contains("Places API (New)", ex.Message);
        Assert.Contains("Google said: \"denied\"", ex.Message);
    }

    [Fact]
    public async Task Network_error_becomes_ProviderUnavailable_without_the_key()
    {
        var set = Google(new GoogleStub((_, _) => throw new HttpRequestException($"connect failed key={Key}")));
        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(() => set.Places.SearchAsync("x"));
        Assert.Contains("could not reach Google", ex.Message);
        Assert.DoesNotContain(Key, ex.ToString());
    }

    [Fact]
    public void Requests_time_out_after_15_seconds() => Assert.Equal(TimeSpan.FromSeconds(15), GoogleApiClient.Timeout);

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_timeout()
    {
        using var cts = new CancellationTokenSource();
        var set = Google(GoogleStub.Hanging());
        var task = set.Places.SearchAsync("x", cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
