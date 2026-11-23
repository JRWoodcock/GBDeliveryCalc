using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Core.Model;
using GBDeliveryCalc.Presentation;
using GBDeliveryCalc.Services;
using GBDeliveryCalc.Services.OilPriceApi;

namespace GBDeliveryCalc.Tests;

/// <summary>
/// End to end: changing the shop's starting address in Settings (Google-verified or typed) switches the fuel prices
/// on New estimate to that state's averages, for regular and diesel, with one fetch per fuel type per state per day.
/// </summary>
public class ShopStateFuelTests
{
    private const string Key = "opa_SHOP_STATE_test_key";

    /// <summary>Google-like address search: every query returns one full address in a known state.</summary>
    private sealed class StatePlaces : IPlaceSearchProvider
    {
        public static readonly Dictionary<string, string> Addresses = new()
        {
            ["Grover Brothers Hattiesburg"] = "1500 Main St, Hattiesburg, MS 39401, USA",
            ["Mobile city hall"] = "205 Government St, Mobile, AL 36602, USA",
            ["Baton Rouge capitol"] = "900 N 3rd St, Baton Rouge, LA 70802, USA",
            ["Memphis"] = "125 N Main St, Memphis, TN 38103, USA",
            ["Houston"] = "901 Bagby St, Houston, TX 77002, USA",
            ["Pensacola"] = "222 W Main St, Pensacola, FL 32502, USA",
        };

        public ProviderInfo Info { get; } = new("Google Places API", ProviderStatus.Available, "", false);

        public Task<PlaceSearchResult> SearchAsync(string query, CancellationToken ct = default) => Task.FromResult(new PlaceSearchResult
        {
            ProviderName = "Google Places API",
            Candidates = [new PlaceCandidate { DisplayName = query, FormattedAddress = Addresses[query] }],
        });
    }

    /// <summary>OilPriceAPI stub with a distinct price for every state and fuel, recording each series requested.</summary>
    private sealed class PriceApi
    {
        public List<string> Codes { get; } = [];
        public GoogleStub Stub { get; }

        public static decimal Price(string state, FuelType fuel) =>
            (fuel == FuelType.Diesel ? 5m : 3m) + (UsStates.Names.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList().IndexOf(state) + 1) / 100m;

        public PriceApi() => Stub = new GoogleStub((r, _) =>
        {
            var code = r.Uri.Query[(r.Uri.Query.IndexOf('=') + 1)..];
            Codes.Add(code);
            var fuel = code.StartsWith("DIESEL", StringComparison.Ordinal) ? FuelType.Diesel : FuelType.RegularUnleaded;
            var state = code.Split('_')[^2];
            return GoogleStub.Json("{\"data\":{\"code\":\"" + code + "\",\"price\":" +
                Price(state, fuel).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"currency\":\"USD\",\"unit\":\"gallon\",\"source\":\"aaa\",\"data_status\":\"current\",\"as_of\":\"2026-11-20T14:00:00Z\"}}");
        });
    }

    private static async Task<(MainViewModel vm, PriceApi api, TempDatabase db)> CreateAsync()
    {
        var api = new PriceApi();
        var secrets = new MemorySecretStore();
        secrets.Set(SecretNames.OilPriceApiKey, Key);
        var http = new HttpClient(api.Stub);
        var (vm, _, db, _) = await WorkflowTests.CreateAsync(
            _ => new IntegrationSet(new StatePlaces(), new FakeRouting(), new OilPriceApiFuelSource(http, Key)), secrets);
        await WorkflowTests.SetUpTeamsAsync(vm.Settings); // Sierra (regular) and Box truck (diesel)
        return (vm, api, db);
    }

    /// <summary>Look up and confirm the shop address in Settings, then wait for New estimate to reload.</summary>
    private static async Task ConfirmShopAddressAsync(MainViewModel vm, string query)
    {
        var s = vm.Settings;
        s.OriginAddress = query;
        await s.SearchOriginAddressCommand.ExecuteAsync(null);
        s.SelectedOriginCandidate = Assert.Single(s.OriginCandidates);
        await s.ConfirmOriginAddressCommand.ExecuteAsync(null);
        Assert.True(s.OriginVerified, s.OriginLookupMessage);
        await Task.Delay(100); // SettingsChanged reloads New estimate, which fetches the new state's prices
    }

    private static void AssertPricesShown(NewEstimateViewModel e, string state)
    {
        var name = UsStates.Name(state);
        e.ManualFuelPriceText = "";
        e.SelectedVehicle = e.Vehicles.Single(v => v.Name == "2028 GMC Sierra");
        Assert.Equal(state, e.FuelState);
        Assert.Contains($"Regular unleaded {Fmt(PriceApi.Price(state, FuelType.RegularUnleaded))}/gal (AAA {name} average via OilPriceAPI", e.FuelStatusText);
        e.SelectedVehicle = e.Vehicles.Single(v => v.Name == "Box truck");
        Assert.Contains($"Diesel {Fmt(PriceApi.Price(state, FuelType.Diesel))}/gal (AAA {name} average via OilPriceAPI", e.FuelStatusText);
    }

    private static string Fmt(decimal d) => Core.Calculation.Fmt.Rate(d);

    [Theory]
    [InlineData("Grover Brothers Hattiesburg", "MS")]
    [InlineData("Mobile city hall", "AL")]
    [InlineData("Baton Rouge capitol", "LA")]
    [InlineData("Memphis", "TN")]
    [InlineData("Houston", "TX")]
    [InlineData("Pensacola", "FL")]
    public async Task Confirming_a_shop_address_fetches_that_states_prices(string query, string state)
    {
        var (vm, api, db) = await CreateAsync();
        using var _db = db;
        await ConfirmShopAddressAsync(vm, query);
        Assert.Contains($"GASOLINE_RETAIL_STATE_{state}_USD", api.Codes);
        Assert.Contains($"DIESEL_RETAIL_STATE_{state}_USD", api.Codes);
        AssertPricesShown(vm.NewEstimate, state);
    }

    [Fact]
    public async Task Each_change_of_shop_state_switches_prices_and_fetches_each_state_once_a_day()
    {
        var (vm, api, db) = await CreateAsync();
        using var _db = db;
        var e = vm.NewEstimate;
        WorkflowTests.EnterNeshoba(e);

        var sequence = new[]
        {
            ("Grover Brothers Hattiesburg", "MS"), ("Mobile city hall", "AL"), ("Baton Rouge capitol", "LA"),
            ("Memphis", "TN"), ("Houston", "TX"), ("Pensacola", "FL"),
        };
        foreach (var (query, state) in sequence)
        {
            var before = api.Codes.Count;
            await ConfirmShopAddressAsync(vm, query);
            if (state != "MS") // MS was already fetched at startup
            {
                Assert.Equal(new[] { $"GASOLINE_RETAIL_STATE_{state}_USD", $"DIESEL_RETAIL_STATE_{state}_USD" }, api.Codes.Skip(before));
            }
            AssertPricesShown(e, state);

            // The fuel price feeds the estimate: total uses this state's price.
            e.SelectedVehicle = e.Vehicles.Single(v => v.Name == "2028 GMC Sierra");
            var regular = PriceApi.Price(state, FuelType.RegularUnleaded);
            Assert.Equal(Core.Calculation.Money.ToCents(250m / 19m * regular), vm.NewEstimate.Result.FuelCost);
        }

        // Going back to states already fetched today makes no new calls.
        var calls = api.Codes.Count;
        await ConfirmShopAddressAsync(vm, "Mobile city hall");
        AssertPricesShown(e, "AL");
        await ConfirmShopAddressAsync(vm, "Grover Brothers Hattiesburg");
        AssertPricesShown(e, "MS");
        Assert.Equal(calls, api.Codes.Count);

        // A new estimate keeps the shop's state; each series was fetched exactly once.
        await e.StartNewAsync();
        Assert.Equal("MS", e.FuelState);
        Assert.All(api.Codes.GroupBy(c => c), g => Assert.Single(g));
        Assert.Equal(12, api.Codes.Count); // 6 states × regular and diesel
    }

    [Fact]
    public async Task Typed_and_saved_addresses_also_switch_the_state_and_no_state_means_mississippi()
    {
        var (vm, api, db) = await CreateAsync();
        using var _db = db;
        var s = vm.Settings;
        var e = vm.NewEstimate;

        s.OriginAddress = "400 W Markham St, Little Rock, AR 72201";
        await s.SaveCompanyCommand.ExecuteAsync(null);
        await Task.Delay(100);
        AssertPricesShown(e, "AR");
        Assert.Contains("GASOLINE_RETAIL_STATE_AR_USD", api.Codes);

        s.OriginAddress = "Jackson, Mississippi";
        await s.SaveCompanyCommand.ExecuteAsync(null);
        await Task.Delay(100);
        AssertPricesShown(e, "MS");

        s.OriginAddress = "Our shop"; // no state named
        await s.SaveCompanyCommand.ExecuteAsync(null);
        await Task.Delay(100);
        AssertPricesShown(e, "MS");
    }

    [Fact]
    public async Task A_state_without_a_price_series_reports_it_and_allows_a_typed_price()
    {
        var calls = 0;
        var stub = new GoogleStub((r, _) =>
        {
            calls++;
            return GoogleStub.Json("{\"message\":\"not found\"}", System.Net.HttpStatusCode.NotFound);
        });
        var secrets = new MemorySecretStore();
        secrets.Set(SecretNames.OilPriceApiKey, Key);
        var (vm, _, db, _) = await WorkflowTests.CreateAsync(
            _ => new IntegrationSet(new StatePlaces(), new FakeRouting(), new OilPriceApiFuelSource(new HttpClient(stub), Key)), secrets);
        using var _db = db;
        await WorkflowTests.SetUpTeamsAsync(vm.Settings);
        await ConfirmShopAddressAsync(vm, "Houston");
        var e = vm.NewEstimate;
        e.SelectedVehicle = e.Vehicles.Single(v => v.Name == "2028 GMC Sierra");
        e.ManualFuelPriceText = "";
        Assert.Contains("no price series for this state", e.FuelStatusText);
        Assert.Contains("Texas", e.FuelStatusText);
        e.ManualFuelPriceText = "3.10";
        Assert.Contains("manual entry", e.FuelStatusText);
        Assert.True(calls > 0);
    }
}
