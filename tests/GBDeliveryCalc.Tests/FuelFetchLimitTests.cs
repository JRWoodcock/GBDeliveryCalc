using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Core.Model;
using GBDeliveryCalc.Data;
using GBDeliveryCalc.Presentation;
using GBDeliveryCalc.Services;
using GBDeliveryCalc.Services.OilPriceApi;

namespace GBDeliveryCalc.Tests;

/// <summary>
/// OilPriceAPI is called at most once per fuel type per local day (persisted in SQLite), and after a failure at most
/// once per hour. Uses the real adapter over a counting stub and the real SQLite cache.
/// </summary>
public class FuelFetchLimitTests
{
    private const string Key = "opa_LIMIT_test_key_000000";

    private sealed class Api
    {
        public int Calls;
        public bool Down;
        public GoogleStub Stub { get; }
        public Api() => Stub = new GoogleStub((r, _) =>
        {
            Calls++;
            if (Down) throw new HttpRequestException("down");
            var diesel = r.Uri.Query.Contains(OilPriceApiFuelSource.DieselCode);
            var code = diesel ? OilPriceApiFuelSource.DieselCode : OilPriceApiFuelSource.RegularCode;
            var price = diesel ? "5.592" : "3.49";
            return GoogleStub.Json("{\"data\":{\"code\":\"" + code + "\",\"price\":" + price +
                ",\"currency\":\"USD\",\"unit\":\"gallon\",\"source\":\"aaa\",\"data_status\":\"current\",\"as_of\":\"2026-11-18T14:00:00Z\"}}");
        });
    }

    private static FuelPriceService Service(Api api, string dbPath, TimeProvider clock) =>
        new(new OilPriceApiFuelSource(new HttpClient(api.Stub), Key), new SqliteFuelPriceCache(new SqliteDatabase(dbPath)), clock);

    [Fact]
    public async Task Second_request_same_day_makes_no_call_even_after_restart()
    {
        using var db = await TempDatabase.CreateAsync();
        var api = new Api();
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero));

        var first = await Service(api, db.Path, clock).GetAsync(FuelType.RegularUnleaded);
        Assert.Equal(3.49m, first.Price!.PricePerGallon);
        Assert.Equal(1, api.Calls);

        clock.Now = clock.Now.AddHours(9);
        // "Restart": brand-new service, cache and database objects over the same file.
        var again = await Service(api, db.Path, clock).GetAsync(FuelType.RegularUnleaded);
        Assert.True(again.FromCacheToday);
        Assert.Equal(3.49m, again.Price!.PricePerGallon);
        Assert.Equal(1, api.Calls);

        // Diesel is a separate daily fetch.
        await Service(api, db.Path, clock).GetAsync(FuelType.Diesel);
        await Service(api, db.Path, clock).GetAsync(FuelType.Diesel);
        Assert.Equal(2, api.Calls);
    }

    [Fact]
    public async Task Next_day_makes_exactly_one_new_call()
    {
        using var db = await TempDatabase.CreateAsync();
        var api = new Api();
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 18, 20, 0, 0, TimeSpan.Zero));
        await Service(api, db.Path, clock).GetAsync(FuelType.RegularUnleaded);

        clock.Now = clock.Now.AddHours(5); // next local calendar day (clock is UTC)
        var r = await Service(api, db.Path, clock).GetAsync(FuelType.RegularUnleaded);
        Assert.False(r.FromCacheToday);
        await Service(api, db.Path, clock).GetAsync(FuelType.RegularUnleaded);
        Assert.Equal(2, api.Calls);
    }

    [Fact]
    public async Task Failure_is_not_retried_within_the_hour_even_after_restart()
    {
        using var db = await TempDatabase.CreateAsync();
        var api = new Api();
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
        await Service(api, db.Path, clock).GetAsync(FuelType.Diesel); // yesterday's value
        Assert.Equal(1, api.Calls);

        clock.Now = new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
        api.Down = true;
        var failed = await Service(api, db.Path, clock).GetAsync(FuelType.Diesel);
        Assert.Equal(2, api.Calls);
        Assert.True(failed.IsLastKnownFallback);
        Assert.Equal(5.592m, failed.Price!.PricePerGallon);
        Assert.Equal(TimeSpan.FromHours(21), failed.Age);

        clock.Now = clock.Now.AddMinutes(59);
        var throttled = await Service(api, db.Path, clock).GetAsync(FuelType.Diesel);
        Assert.Equal(2, api.Calls); // no call
        Assert.True(throttled.IsLastKnownFallback);
        Assert.Contains("try again after 10:00 AM", throttled.Error);

        clock.Now = clock.Now.AddMinutes(1);
        api.Down = false;
        var recovered = await Service(api, db.Path, clock).GetAsync(FuelType.Diesel);
        Assert.Equal(3, api.Calls);
        Assert.False(recovered.IsLastKnownFallback);
        await Service(api, db.Path, clock).GetAsync(FuelType.Diesel);
        Assert.Equal(3, api.Calls); // and now cached for the day
    }

    [Fact]
    public async Task Startup_fetches_both_fuels_once_and_everything_else_reuses_them()
    {
        var api = new Api();
        var secrets = new MemorySecretStore();
        secrets.Set(SecretNames.OilPriceApiKey, Key);
        var (vm, _, db, services) = await WorkflowTests.CreateAsync(k => ProviderFactory.Create(k, new HttpClient(api.Stub)), secrets);
        using var _db = db;
        Assert.Equal(2, api.Calls); // regular + diesel at startup

        await WorkflowTests.SetUpTeamsAsync(vm.Settings); // reloads New estimate
        var e = vm.NewEstimate;
        WorkflowTests.EnterNeshoba(e);
        e.ManualFuelPriceText = "";
        e.SelectedVehicle = e.Vehicles.Single(v => v.Name == "Box truck");
        e.SelectedVehicle = e.Vehicles.Single(v => v.Name == "2028 GMC Sierra");
        await e.StartNewAsync();
        await e.RefreshFuelPricesAsync();
        await e.CheckFuelPricesAsync();
        Assert.Equal(2, api.Calls);

        // Local date rollover: the 15-minute check fetches each fuel type once more.
        var clock = (FakeClock)services.Clock;
        clock.Now = clock.Now.AddDays(1);
        await e.CheckFuelPricesAsync();
        await e.CheckFuelPricesAsync();
        Assert.Equal(4, api.Calls);
    }

    [Fact]
    public async Task Periodic_check_retries_a_failure_at_most_hourly()
    {
        var api = new Api { Down = true };
        var secrets = new MemorySecretStore();
        secrets.Set(SecretNames.OilPriceApiKey, Key);
        var (vm, _, db, services) = await WorkflowTests.CreateAsync(k => ProviderFactory.Create(k, new HttpClient(api.Stub)), secrets);
        using var _db = db;
        Assert.Equal(2, api.Calls);
        var e = vm.NewEstimate;
        var clock = (FakeClock)services.Clock;

        clock.Now = new DateTimeOffset(2026, 11, 18, 15, 59, 0, TimeSpan.Zero);
        await e.CheckFuelPricesAsync();
        Assert.Equal(2, api.Calls); // within the hour: no call
        Assert.Contains("try again after", e.FuelStatusText);

        api.Down = false;
        clock.Now = new DateTimeOffset(2026, 11, 18, 16, 0, 0, TimeSpan.Zero);
        await e.CheckFuelPricesAsync();
        Assert.Equal(4, api.Calls);
        await e.CheckFuelPricesAsync();
        Assert.Equal(4, api.Calls); // now cached for the day
    }

    [Fact]
    public async Task Saving_a_new_key_allows_an_immediate_retry_after_a_failure()
    {
        var api = new Api { Down = true };
        var secrets = new MemorySecretStore();
        secrets.Set(SecretNames.OilPriceApiKey, "old-bad-key");
        var (vm, _, db, _) = await WorkflowTests.CreateAsync(k => ProviderFactory.Create(k, new HttpClient(api.Stub)), secrets);
        using var _db = db;
        Assert.Equal(2, api.Calls);
        await vm.NewEstimate.RefreshFuelPricesAsync();
        Assert.Equal(2, api.Calls); // throttled

        api.Down = false;
        Assert.Equal("Save / replace key", vm.Settings.FuelSaveButtonText);
        Assert.True(await vm.Settings.SaveFuelKeyAsync(Key));
        Assert.StartsWith("Key replaced", vm.Settings.FuelKeyMessage);
        Assert.Equal(Key, secrets.Values[SecretNames.OilPriceApiKey]);
        await Task.Delay(50); // Settings change reloads New estimate, which fetches again
        Assert.Equal(4, api.Calls);
        Assert.Equal(Key, api.Stub.Requests[^1].Headers["Authorization"]["Token ".Length..]);
    }

    [Fact]
    public async Task Test_key_checks_the_new_key_before_saving_and_save_replaces()
    {
        var api = new Api();
        var secrets = new MemorySecretStore();
        secrets.Set(SecretNames.OilPriceApiKey, "old-key");
        secrets.Set(SecretNames.GoogleMapsApiKey, "old-google");
        var (vm, _, db, _) = await WorkflowTests.CreateAsync(k => ProviderFactory.Create(k, new HttpClient(api.Stub)), secrets);
        using var _db = db;
        var s = vm.Settings;

        await s.TestFuelKeyAsync("new-key");
        Assert.Equal("Token new-key", api.Stub.Requests[^1].Headers["Authorization"]);
        Assert.Equal("old-key", secrets.Values[SecretNames.OilPriceApiKey]); // not saved by testing
        Assert.True(await s.SaveFuelKeyAsync("new-key"));
        Assert.Equal("new-key", secrets.Values[SecretNames.OilPriceApiKey]);

        Assert.Equal("Save / replace key", s.GoogleSaveButtonText);
        Assert.True(s.SaveGoogleKey("new-google"));
        Assert.StartsWith("Key replaced", s.GoogleKeyMessage);
        Assert.Equal("new-google", secrets.Values[SecretNames.GoogleMapsApiKey]);
        s.RemoveGoogleKey();
        Assert.Equal("Save key", s.GoogleSaveButtonText);
        await s.RemoveFuelKeyAsync();
        Assert.Equal("Save key", s.FuelSaveButtonText);
        Assert.Empty(secrets.Values);
    }
}
