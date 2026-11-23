using System.Net;
using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Core.Model;
using GBDeliveryCalc.Presentation;
using GBDeliveryCalc.Services;
using GBDeliveryCalc.Services.OilPriceApi;

namespace GBDeliveryCalc.Tests;

public class OilPriceApiTests
{
    private const string Key = "opa_TEST_not_a_real_key_0123456789";

    private static string Latest(string code, decimal price, string status = "current", string asOf = "2026-11-18T14:57:28.663Z",
        string source = "aaa", bool stale = false, string currency = "USD", string unit = "gallon") =>
        "{\"status\":\"success\",\"data\":{\"price\":" + price.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        ",\"formatted\":\"$x\",\"currency\":\"" + currency + "\",\"code\":\"" + code + "\",\"created_at\":\"" + asOf +
        "\",\"type\":\"spot_price\",\"unit\":\"" + unit + "\",\"source\":\"" + source + "\",\"data_status\":\"" + status +
        "\",\"freshness\":{\"status\":\"" + status + "\"},\"as_of\":\"" + asOf + "\",\"synthetic\":false,\"stale\":" + (stale ? "true" : "false") +
        ",\"metadata\":{\"source\":\"" + source + "\",\"source_description\":\"Aaa\"}}}";

    private static HttpResponseMessage Answer(SeenRequest r) => r.Uri.Query.Contains(OilPriceApiFuelSource.DieselCode)
        ? GoogleStub.Json(Latest(OilPriceApiFuelSource.DieselCode, 5.592m))
        : GoogleStub.Json(Latest(OilPriceApiFuelSource.RegularCode, 3.49m, asOf: "2026-11-18T14:15:09.129Z"));

    private static OilPriceApiFuelSource Source(GoogleStub stub) =>
        new(new HttpClient(stub), Key, TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Central Standard Time" : "America/Chicago"));

    // ---------------- requests and parsing ----------------
    [Theory]
    [InlineData(FuelType.RegularUnleaded, "GASOLINE_RETAIL_STATE_MS_USD", 3.49)]
    [InlineData(FuelType.Diesel, "DIESEL_RETAIL_STATE_MS_USD", 5.592)]
    public async Task Requests_the_mississippi_series_for_each_fuel_type(FuelType fuel, string code, double expected)
    {
        var stub = new GoogleStub((r, _) => Answer(r));
        var price = await Source(stub).GetStateAverageAsync(fuel);

        var req = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Equal($"https://api.oilpriceapi.com/v1/prices/latest?by_code={code}", req.Uri.AbsoluteUri);
        Assert.Equal($"Token {Key}", req.Headers["Authorization"]);
        Assert.DoesNotContain(Key, req.Uri.AbsoluteUri);

        Assert.Equal(fuel, price.FuelType);
        Assert.Equal((decimal)expected, price.PricePerGallon);
        Assert.Equal("AAA Mississippi average via OilPriceAPI", price.Source);
        Assert.Equal(new DateOnly(2026, 11, 18), price.PublishedDate);
        Assert.True(price.IsLive);
        Assert.False(price.IsManualOverride);
    }

    [Fact]
    public async Task Regular_and_diesel_are_labelled_distinctly()
    {
        var src = Source(new GoogleStub((r, _) => Answer(r)));
        var reg = await src.GetStateAverageAsync(FuelType.RegularUnleaded);
        var dsl = await src.GetStateAverageAsync(FuelType.Diesel);
        Assert.StartsWith(FuelType.RegularUnleaded.Label(), reg.Describe());
        Assert.StartsWith(FuelType.Diesel.Label(), dsl.Describe());
        Assert.Contains("published 2026-11-18", dsl.Describe());
        Assert.NotEqual(reg.Describe(), dsl.Describe());
    }

    [Fact]
    public async Task Published_date_is_the_local_date()
    {
        // 03:30 UTC on the 19th is still the 18th in Mississippi.
        var src = Source(new GoogleStub((_, _) => GoogleStub.Json(Latest(OilPriceApiFuelSource.RegularCode, 3.49m, asOf: "2026-11-19T03:30:00Z"))));
        Assert.Equal(new DateOnly(2026, 11, 18), (await src.GetStateAverageAsync(FuelType.RegularUnleaded)).PublishedDate);
    }

    [Theory]
    [InlineData("stale", false)]
    [InlineData("delayed", false)]
    [InlineData("fresh", true)]
    public async Task Only_current_values_are_live(string status, bool live)
    {
        var src = Source(new GoogleStub((_, _) => GoogleStub.Json(Latest(OilPriceApiFuelSource.RegularCode, 3.49m, status: status))));
        Assert.Equal(live, (await src.GetStateAverageAsync(FuelType.RegularUnleaded)).IsLive);
    }

    [Fact]
    public async Task Stale_flag_makes_value_not_live()
    {
        var src = Source(new GoogleStub((_, _) => GoogleStub.Json(Latest(OilPriceApiFuelSource.RegularCode, 3.49m, stale: true))));
        Assert.False((await src.GetStateAverageAsync(FuelType.RegularUnleaded)).IsLive);
    }

    [Fact]
    public void Other_sources_are_named_in_the_label() =>
        Assert.Equal("opis Mississippi via OilPriceAPI", OilPriceApiFuelSource.SourceLabel("opis"));

    // ---------------- rejection ----------------
    public static TheoryData<string, string> BadData => new()
    {
        { Latest(OilPriceApiFuelSource.DieselCode, 5.59m), "different series" },          // wrong code for regular
        { Latest(OilPriceApiFuelSource.RegularCode, 0m), "no usable price" },
        { Latest(OilPriceApiFuelSource.RegularCode, -1m), "no usable price" },
        { """{"status":"success","data":{"code":"GASOLINE_RETAIL_STATE_MS_USD"}}""", "no usable price" },
        { Latest(OilPriceApiFuelSource.RegularCode, 3.49m, currency: "CAD"), "not USD" },
        { Latest(OilPriceApiFuelSource.RegularCode, 3.49m, unit: "litre"), "not per gallon" },
        { "not json", "could not read" },
        { """{"status":"success"}""", "could not read" },
        { "[1,2]", "could not read" },
    };

    [Theory]
    [MemberData(nameof(BadData))]
    public async Task Unusable_responses_are_rejected(string body, string expected)
    {
        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(() =>
            Source(new GoogleStub((_, _) => GoogleStub.Json(body))).GetStateAverageAsync(FuelType.RegularUnleaded));
        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData(401, "refused the key")]
    [InlineData(403, "plan")]
    [InlineData(402, "plan")]
    [InlineData(404, "404")]
    [InlineData(429, "429")]
    [InlineData(500, "500")]
    public async Task Http_errors_become_friendly_messages_without_the_key(int code, string expected)
    {
        var body = $$$"""{"status":"error","message":"Invalid token {{{Key}}}"}""";
        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(() =>
            Source(new GoogleStub((_, _) => GoogleStub.Json(body, (HttpStatusCode)code))).GetStateAverageAsync(FuelType.Diesel));
        Assert.Contains(expected, ex.Message);
        Assert.Contains("OilPriceAPI said", ex.Message);
        Assert.DoesNotContain(Key, ex.ToString());
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task Network_error_becomes_ProviderUnavailable_without_the_key()
    {
        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(() =>
            Source(new GoogleStub((_, _) => throw new HttpRequestException($"failed {Key}"))).GetStateAverageAsync(FuelType.RegularUnleaded));
        Assert.Contains("could not reach OilPriceAPI", ex.Message);
        Assert.DoesNotContain(Key, ex.ToString());
    }

    [Fact]
    public void Requests_time_out_after_15_seconds() => Assert.Equal(TimeSpan.FromSeconds(15), OilPriceApiFuelSource.Timeout);

    // ---------------- factory and FuelPriceService ----------------
    [Fact]
    public void Factory_uses_OilPriceAPI_only_when_its_key_is_set()
    {
        Assert.IsType<NotConfiguredFuelPriceSource>(ProviderFactory.Create(new IntegrationKeys()).Fuel);
        Assert.IsType<NotConfiguredFuelPriceSource>(ProviderFactory.Create(new IntegrationKeys(Google: "g")).Fuel);
        var set = ProviderFactory.Create(new IntegrationKeys(OilPriceApi: Key));
        Assert.IsType<OilPriceApiFuelSource>(set.Fuel);
        Assert.Equal(ProviderStatus.Available, set.Fuel.Info.Status);
        Assert.Equal("OilPriceAPI (state AAA average)", set.Fuel.Info.Name);
        Assert.Equal(ProviderStatus.NotConfigured, set.Routing.Info.Status); // keys are independent
    }

    [Fact]
    public async Task Service_reuses_todays_value_per_fuel_type_and_falls_back_when_down()
    {
        var calls = 0;
        var down = false;
        var stub = new GoogleStub((r, _) => { calls++; if (down) throw new HttpRequestException("x"); return Answer(r); });
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero));
        var svc = new FuelPriceService(Source(stub), new MemoryFuelCache(), clock);

        Assert.Equal(3.49m, (await svc.GetAsync(FuelType.RegularUnleaded)).Price!.PricePerGallon);
        Assert.True((await svc.GetAsync(FuelType.RegularUnleaded)).FromCacheToday);
        Assert.Equal(5.592m, (await svc.GetAsync(FuelType.Diesel)).Price!.PricePerGallon);
        Assert.Equal(2, calls);

        clock.Now = clock.Now.AddDays(1);
        down = true;
        var r = await svc.GetAsync(FuelType.Diesel);
        Assert.True(r.IsLastKnownFallback);
        Assert.Equal(5.592m, r.Price!.PricePerGallon);
        Assert.Equal(TimeSpan.FromDays(1), r.Age);
        Assert.Contains("could not reach OilPriceAPI", r.Error);
    }

    // ---------------- view model ----------------
    private static Task<(MainViewModel vm, FakeClipboard clip, TempDatabase db, EstimatorServices services)> CreateAsync(
        GoogleStub stub, MemorySecretStore secrets) =>
        WorkflowTests.CreateAsync(k => ProviderFactory.Create(k, new HttpClient(stub)), secrets);

    [Fact]
    public async Task Save_test_and_remove_fuel_key()
    {
        var stub = new GoogleStub((r, _) => Answer(r));
        var secrets = new MemorySecretStore();
        var (vm, _, db, _) = await CreateAsync(stub, secrets);
        using var _db = db;
        var s = vm.Settings;
        Assert.Equal("No key", s.FuelKeyStatus);
        Assert.Contains("Settings → API keys → Fuel prices", vm.NewEstimate.IntegrationBanner);

        await s.TestFuelKeyAsync(null);
        Assert.Contains("No key to test", s.FuelKeyMessage);

        await s.TestFuelKeyAsync(Key);
        Assert.StartsWith("Test passed", s.FuelKeyMessage);
        Assert.Contains("$3.49", s.FuelKeyMessage);
        Assert.Contains("2026-11-18", s.FuelKeyMessage);
        Assert.Empty(secrets.Values); // testing doesn't save

        Assert.False(await s.SaveFuelKeyAsync("has space"));
        Assert.True(await s.SaveFuelKeyAsync(Key));
        Assert.Equal(Key, secrets.Values[SecretNames.OilPriceApiKey]);
        Assert.Equal("Key saved", s.FuelKeyStatus);
        Assert.DoesNotContain(Key, s.FuelKeyMessage);
        await Task.Yield();
        Assert.Equal(ProviderStatus.Available, vm.NewEstimate.FuelInfo.Status);
        Assert.DoesNotContain("Fuel prices", vm.NewEstimate.IntegrationBanner);

        await s.RemoveFuelKeyAsync();
        await Task.Yield();
        Assert.False(secrets.Values.ContainsKey(SecretNames.OilPriceApiKey));
        Assert.Equal(ProviderStatus.NotConfigured, vm.NewEstimate.FuelInfo.Status);
    }

    [Fact]
    public async Task Failed_fuel_test_shows_reason_without_the_key()
    {
        var stub = new GoogleStub((_, _) => GoogleStub.Json($$$"""{"message":"bad token {{{Key}}}"}""", HttpStatusCode.Unauthorized));
        var (vm, _, db, _) = await CreateAsync(stub, new MemorySecretStore());
        using var _db = db;
        await vm.Settings.TestFuelKeyAsync(Key);
        Assert.StartsWith("Test failed", vm.Settings.FuelKeyMessage);
        Assert.DoesNotContain(Key, vm.Settings.FuelKeyMessage);
    }

    [Fact]
    public async Task New_estimate_fetches_for_the_vehicle_fuel_type_and_manual_price_wins()
    {
        var stub = new GoogleStub((r, _) => Answer(r));
        var secrets = new MemorySecretStore();
        secrets.Set(SecretNames.OilPriceApiKey, Key);
        var (vm, _, db, _) = await CreateAsync(stub, secrets);
        using var _db = db;
        await WorkflowTests.SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        WorkflowTests.EnterNeshoba(e);
        e.ManualFuelPriceText = "";

        // Both fuel types were fetched at startup.
        Assert.Contains(stub.Requests, r => r.Uri.Query.Contains(OilPriceApiFuelSource.RegularCode));
        Assert.Contains(stub.Requests, r => r.Uri.Query.Contains(OilPriceApiFuelSource.DieselCode));
        var calls = stub.Requests.Count;
        Assert.Contains("Regular", e.FuelStatusText);
        Assert.Contains("$3.49", e.FuelStatusText);
        Assert.Contains("AAA Mississippi average via OilPriceAPI", e.FuelStatusText);
        Assert.Equal("$394.59", e.TotalCostText);

        // Switching to the diesel truck uses today's diesel price, never the regular one, and makes no new call.
        e.SelectedVehicle = e.Vehicles.Single(v => v.Name == "Box truck");
        Assert.Contains("Diesel", e.FuelStatusText);
        Assert.Contains("5.59", e.FuelStatusText);
        Assert.Equal(calls, stub.Requests.Count);

        // A typed price always wins.
        e.ManualFuelPriceText = "4.00";
        Assert.Contains("manual entry", e.FuelStatusText);
        Assert.All(stub.Requests, r => Assert.DoesNotContain(Key, r.Uri.AbsoluteUri));
    }
}
