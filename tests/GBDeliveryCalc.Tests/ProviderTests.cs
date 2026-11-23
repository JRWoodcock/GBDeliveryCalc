using System.Net;
using System.Text;
using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Core.Model;
using GBDeliveryCalc.Core.Persistence;
using GBDeliveryCalc.Services;

namespace GBDeliveryCalc.Tests;

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal sealed class MemoryFuelCache : IFuelPriceCache
{
    public List<FuelPrice> Items { get; } = [];
    public Task<FuelPrice?> GetLatestAsync(FuelType fuelType, string state = UsStates.Default, CancellationToken ct = default) =>
        Task.FromResult(Items.Where(i => i.FuelType == fuelType).OrderByDescending(i => i.RetrievedAt).FirstOrDefault());
    public Task SaveAsync(FuelPrice price, CancellationToken ct = default) { Items.Add(price); return Task.CompletedTask; }

    public Dictionary<FuelType, FuelFetchState> States { get; } = [];
    public Task<FuelFetchState?> GetFetchStateAsync(FuelType fuelType, string state = UsStates.Default, CancellationToken ct = default) =>
        Task.FromResult(States.TryGetValue(fuelType, out var s) ? s : null);
    public Task SaveFetchStateAsync(FuelFetchState state, CancellationToken ct = default) { States[state.FuelType] = state; return Task.CompletedTask; }
    public Task ClearFetchStateAsync(CancellationToken ct = default) { States.Clear(); return Task.CompletedTask; }
}

internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal sealed class CountingFuelSource(Func<FuelType, FuelPrice> produce) : IFuelPriceSource
{
    public int Calls { get; private set; }
    public bool Fail { get; set; }
    public ProviderInfo Info { get; } = new("Test fuel", ProviderStatus.Available, "", true);
    public Task<FuelPrice> GetStateAverageAsync(FuelType fuelType, string state = UsStates.Default, CancellationToken ct = default)
    {
        Calls++;
        if (Fail) throw new ProviderUnavailableException("source down");
        return Task.FromResult(produce(fuelType));
    }
}

public class ProviderTests
{
    [Fact]
    public void Fuel_is_not_configured_without_an_OilPriceAPI_key()
    {
        foreach (var key in new[] { null, "", "google-key-only" })
        {
            var set = ProviderFactory.Create(key);
            Assert.Equal(ProviderStatus.NotConfigured, set.Fuel.Info.Status);
            Assert.Contains("Settings → API keys → Fuel prices", set.Fuel.Info.StatusMessage);
        }
    }

    [Fact]
    public async Task Fuel_service_reuses_same_day_result_and_refetches_next_day()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero));
        var source = new CountingFuelSource(f => new FuelPrice { FuelType = f, PricePerGallon = 3.49m, Source = "Licensed", IsLive = true });
        var svc = new FuelPriceService(source, new MemoryFuelCache(), clock);

        var first = await svc.GetAsync(FuelType.RegularUnleaded);
        Assert.False(first.FromCacheToday);
        clock.Now = clock.Now.AddHours(6);
        var second = await svc.GetAsync(FuelType.RegularUnleaded);
        Assert.True(second.FromCacheToday);
        Assert.Equal(1, source.Calls);

        await svc.GetAsync(FuelType.Diesel); // other fuel type is fetched separately
        Assert.Equal(2, source.Calls);

        clock.Now = clock.Now.AddDays(1);
        var third = await svc.GetAsync(FuelType.RegularUnleaded);
        Assert.False(third.FromCacheToday);
        Assert.Equal(3, source.Calls);
    }

    [Fact]
    public async Task Fuel_service_falls_back_to_last_known_with_age()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero));
        var source = new CountingFuelSource(f => new FuelPrice { FuelType = f, PricePerGallon = 3.45m, Source = "Licensed", IsLive = true });
        var svc = new FuelPriceService(source, new MemoryFuelCache(), clock);
        await svc.GetAsync(FuelType.RegularUnleaded);

        clock.Now = clock.Now.AddDays(2);
        source.Fail = true;
        var r = await svc.GetAsync(FuelType.RegularUnleaded);
        Assert.True(r.IsLastKnownFallback);
        Assert.Equal(3.45m, r.Price!.PricePerGallon);
        Assert.Equal(TimeSpan.FromDays(2), r.Age);
        Assert.Equal("source down", r.Error);
    }

    [Fact]
    public async Task Fuel_service_with_nothing_cached_reports_error()
    {
        var svc = new FuelPriceService(new NotConfiguredFuelPriceSource("nope"), new MemoryFuelCache(), TimeProvider.System);
        var r = await svc.GetAsync(FuelType.Diesel);
        Assert.Null(r.Price);
        Assert.False(r.IsLastKnownFallback);
        Assert.Equal("nope", r.Error);
    }

    [Fact]
    public async Task Fuel_service_rejects_wrong_fuel_type_and_does_not_cache_non_live_values()
    {
        var cache = new MemoryFuelCache();
        var wrong = new CountingFuelSource(_ => new FuelPrice { FuelType = FuelType.Diesel, PricePerGallon = 3.9m, IsLive = true });
        var r = await new FuelPriceService(wrong, cache, TimeProvider.System).GetAsync(FuelType.RegularUnleaded);
        Assert.NotNull(r.Error);
        Assert.Null(r.Price);

        var notLive = new CountingFuelSource(f => new FuelPrice { FuelType = f, PricePerGallon = 3.1m, IsLive = false, Source = "Unverified" });
        var cache2 = new MemoryFuelCache();
        var r2 = await new FuelPriceService(notLive, cache2, TimeProvider.System).GetAsync(FuelType.RegularUnleaded);
        Assert.False(r2.Price!.IsLive);
        Assert.Empty(cache2.Items);
    }
}
