using System.Text;
using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Core.Model;
using GBDeliveryCalc.Data;
using GBDeliveryCalc.Presentation;
using GBDeliveryCalc.Services;
using GBDeliveryCalc.Services.OilPriceApi;

namespace GBDeliveryCalc.Tests;

public class StateFuelAndKeyBackupTests
{
    private const string Key = "opa_STATE_test_key_000000";

    // ---------------- finding the state ----------------
    [Theory]
    [InlineData("1125 Golf Course Rd, Philadelphia, MS 39350, USA", "MS")]
    [InlineData("100 Main St, Mobile, AL 36602", "AL")]
    [InlineData("Baton Rouge, LA", "LA")]
    [InlineData("Shop, 12 Oak St, Hattiesburg, MS", "MS")]
    [InlineData("Jackson, Mississippi", "MS")]
    [InlineData("Charleston, West Virginia 25301", "WV")]
    [InlineData("Richmond, Virginia", "VA")]
    [InlineData("Grover Brothers Restaurant Supply", null)]
    [InlineData("Neshoba Central High School", null)]
    [InlineData("", null)]
    public void State_is_found_in_an_address(string address, string? expected) => Assert.Equal(expected, UsStates.FindInAddress(address));

    [Fact]
    public void OilPriceApi_codes_and_labels_follow_the_state()
    {
        Assert.Equal("GASOLINE_RETAIL_STATE_AL_USD", OilPriceApiFuelSource.CodeFor(FuelType.RegularUnleaded, "al"));
        Assert.Equal("DIESEL_RETAIL_STATE_LA_USD", OilPriceApiFuelSource.CodeFor(FuelType.Diesel, "LA"));
        Assert.Equal(OilPriceApiFuelSource.RegularCode, OilPriceApiFuelSource.CodeFor(FuelType.RegularUnleaded));
        Assert.Equal("AAA Alabama average via OilPriceAPI", OilPriceApiFuelSource.SourceLabel("aaa", "AL"));
    }

    private sealed class Api
    {
        public List<string> Codes { get; } = [];
        public GoogleStub Stub { get; }
        public Api() => Stub = new GoogleStub((r, _) =>
        {
            var code = r.Uri.Query[(r.Uri.Query.IndexOf('=') + 1)..];
            Codes.Add(code);
            var price = code.StartsWith("DIESEL", StringComparison.Ordinal) ? "4.10" : code.Contains("_AL_", StringComparison.Ordinal) ? "3.21" : "3.49";
            return GoogleStub.Json("{\"data\":{\"code\":\"" + code + "\",\"price\":" + price +
                ",\"currency\":\"USD\",\"unit\":\"gallon\",\"source\":\"aaa\",\"data_status\":\"current\",\"as_of\":\"2026-11-20T14:00:00Z\"}}");
        });
    }

    [Fact]
    public async Task Each_state_has_its_own_daily_fetch()
    {
        using var db = await TempDatabase.CreateAsync();
        var api = new Api();
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero));
        var svc = new FuelPriceService(new OilPriceApiFuelSource(new HttpClient(api.Stub), Key), new SqliteFuelPriceCache(db.Database), clock);

        var ms = await svc.GetAsync(FuelType.RegularUnleaded, "MS");
        var al = await svc.GetAsync(FuelType.RegularUnleaded, "AL");
        Assert.Equal(3.49m, ms.Price!.PricePerGallon);
        Assert.Equal(3.21m, al.Price!.PricePerGallon);
        Assert.Equal("AL", al.Price.State);
        Assert.Equal("AAA Alabama average via OilPriceAPI", al.Price.Source);

        Assert.True((await svc.GetAsync(FuelType.RegularUnleaded, "AL")).FromCacheToday);
        Assert.True((await svc.GetAsync(FuelType.RegularUnleaded, "MS")).FromCacheToday);
        Assert.Equal(new[] { "GASOLINE_RETAIL_STATE_MS_USD", "GASOLINE_RETAIL_STATE_AL_USD" }, api.Codes);
    }

    [Fact]
    public async Task New_estimate_uses_the_shops_state()
    {
        var api = new Api();
        var secrets = new MemorySecretStore();
        secrets.Set(SecretNames.OilPriceApiKey, Key);
        var (vm, _, db, _) = await WorkflowTests.CreateAsync(k => ProviderFactory.Create(k, new HttpClient(api.Stub)), secrets);
        using var _db = db;
        Assert.Equal("MS", vm.NewEstimate.FuelState); // starting address has no state yet
        Assert.Contains("GASOLINE_RETAIL_STATE_MS_USD", api.Codes);

        await WorkflowTests.SetUpTeamsAsync(vm.Settings); // starting address "Grover Brothers, Hattiesburg, MS"
        vm.Settings.OriginAddress = "100 Main St, Mobile, AL 36602";
        await vm.Settings.SaveCompanyCommand.ExecuteAsync(null);
        await Task.Delay(50);
        var e = vm.NewEstimate;
        Assert.Equal("AL", e.FuelState);
        Assert.Contains("GASOLINE_RETAIL_STATE_AL_USD", api.Codes);
        Assert.Contains("DIESEL_RETAIL_STATE_AL_USD", api.Codes);

        // The job's address doesn't change the state used.
        WorkflowTests.EnterNeshoba(e);
        e.ManualFuelPriceText = "";
        e.DestinationAddress = "1125 Golf Course Rd, Philadelphia, MS 39350, USA";
        Assert.Equal("AL", e.FuelState);
        Assert.Contains("AAA Alabama average", e.FuelStatusText);
        Assert.Contains("$3.21", e.FuelStatusText);
    }

    // ---------------- API key backup ----------------
    private static readonly Dictionary<string, string> Keys = new()
    {
        [SecretNames.GoogleMapsApiKey] = "AIzaTEST-google-key-1234567890",
        [SecretNames.OilPriceApiKey] = "opa_TEST_fuel_key",
    };

    [Fact]
    public void Key_backup_round_trips_and_does_not_contain_the_keys_in_plain_text()
    {
        var file = KeyBackup.Export(Keys, "correct horse");
        var text = Encoding.UTF8.GetString(file);
        Assert.DoesNotContain("AIzaTEST", text);
        Assert.DoesNotContain("opa_TEST", text);
        Assert.Contains("grover-estimator-keys", text);
        Assert.Equal(Keys, KeyBackup.Import(file, "correct horse"));
    }

    [Fact]
    public void Key_backup_rejects_wrong_password_tampering_foreign_files_and_short_passwords()
    {
        var file = KeyBackup.Export(Keys, "correct horse");
        Assert.Throws<InvalidDataException>(() => KeyBackup.Import(file, "wrong horse"));

        var tampered = Encoding.UTF8.GetString(file);
        var i = tampered.IndexOf("\"Data\": \"", StringComparison.Ordinal) + 9;
        tampered = tampered[..i] + (tampered[i] == 'A' ? 'B' : 'A') + tampered[(i + 1)..];
        Assert.Throws<InvalidDataException>(() => KeyBackup.Import(Encoding.UTF8.GetBytes(tampered), "correct horse"));

        Assert.Throws<InvalidDataException>(() => KeyBackup.Import(Encoding.UTF8.GetBytes("{\"hello\":1}"), "x"));
        Assert.Throws<InvalidDataException>(() => KeyBackup.Import(Encoding.UTF8.GetBytes("not json"), "x"));
        Assert.Throws<ArgumentException>(() => KeyBackup.Export(Keys, "short"));
    }

    [Fact]
    public async Task Settings_backs_up_and_restores_both_keys()
    {
        var dialogs = new FakeDialogs();
        var secrets = new MemorySecretStore();
        foreach (var (k, v) in Keys) secrets.Set(k, v);
        var api = new Api();
        var (vm, _, db, _) = await WorkflowTests.CreateAsync(k => ProviderFactory.Create(k, new HttpClient(api.Stub)), secrets, dialogs);
        using var _db = db;
        var s = vm.Settings;
        var path = Path.Combine(db.Directory, "keys" + KeyBackup.Extension);

        dialogs.KeysExportPath = path;
        dialogs.Passwords.Enqueue("short");
        await s.ExportKeysCommand.ExecuteAsync(null);
        Assert.Contains("at least 8", s.KeyBackupMessage);
        Assert.False(File.Exists(path));

        dialogs.Passwords.Enqueue("a long password");
        await s.ExportKeysCommand.ExecuteAsync(null);
        Assert.StartsWith("Backed up 2 API key(s)", s.KeyBackupMessage);
        Assert.True(File.Exists(path));

        // New PC: no keys saved.
        secrets.Values.Clear();
        await s.LoadAsync();
        Assert.False(s.HasGoogleKey);

        dialogs.KeysImportPath = path;
        dialogs.Passwords.Enqueue("wrong password");
        await s.ImportKeysCommand.ExecuteAsync(null);
        Assert.StartsWith("Not restored: Wrong password", s.KeyBackupMessage);
        Assert.Empty(secrets.Values);

        dialogs.Passwords.Enqueue("a long password");
        await s.ImportKeysCommand.ExecuteAsync(null);
        Assert.StartsWith("Restored 2 API key(s)", s.KeyBackupMessage);
        Assert.Equal(Keys[SecretNames.GoogleMapsApiKey], secrets.Values[SecretNames.GoogleMapsApiKey]);
        Assert.Equal(Keys[SecretNames.OilPriceApiKey], secrets.Values[SecretNames.OilPriceApiKey]);
        Assert.True(s.HasGoogleKey);
        Assert.True(s.HasFuelKey);
        Assert.DoesNotContain("AIzaTEST", s.KeyBackupMessage);
        await Task.Delay(100); // let the settings reload (and its fuel fetch) finish before the database is deleted
    }

    [Fact]
    public async Task Exporting_with_no_keys_says_so()
    {
        var dialogs = new FakeDialogs();
        var (vm, _, db, _) = await WorkflowTests.CreateAsync(k => ProviderFactory.Create(k, new HttpClient(new Api().Stub)), new MemorySecretStore(), dialogs);
        using var _db = db;
        await vm.Settings.ExportKeysCommand.ExecuteAsync(null);
        Assert.Contains("No API keys are saved", vm.Settings.KeyBackupMessage);
    }
}
