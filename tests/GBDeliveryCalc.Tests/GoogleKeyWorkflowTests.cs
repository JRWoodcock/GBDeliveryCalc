using System.Net;
using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Presentation;
using GBDeliveryCalc.Services;

namespace GBDeliveryCalc.Tests;

/// <summary>Settings → API keys → Google lookups and the New estimate lookups, driven through the real Google adapters over a stub.</summary>
public class GoogleKeyWorkflowTests
{
    private const string Key = "AIzaWORKFLOW-test-key-000000000000000";

    private static HttpResponseMessage GoogleAnswers(SeenRequest req) => req.Uri.Host.StartsWith("places", StringComparison.Ordinal)
        ? GoogleStub.Json("""{"places":[{"id":"p1","formattedAddress":"1125 Golf Course Rd, Philadelphia, MS 39350, USA","displayName":{"text":"Neshoba Central High School"}}]}""")
        : GoogleStub.Json("""{"routes":[{"distanceMeters":201168,"duration":"8040s"}]}""");

    private static Task<(MainViewModel vm, FakeClipboard clip, TempDatabase db, EstimatorServices services)> CreateAsync(
        GoogleStub stub, MemorySecretStore secrets) =>
        WorkflowTests.CreateAsync(k => ProviderFactory.Create(k, new HttpClient(stub)), secrets);

    [Fact]
    public async Task Without_a_key_lookups_are_off_and_point_to_settings()
    {
        var (vm, _, db, _) = await CreateAsync(new GoogleStub((r, _) => GoogleAnswers(r)), new MemorySecretStore());
        using var _db = db;
        Assert.False(vm.Settings.HasGoogleKey);
        Assert.Equal("No key", vm.Settings.GoogleKeyStatus);
        Assert.False(vm.NewEstimate.CanLookUpAddress);
        Assert.False(vm.NewEstimate.CanLookUpRoute);
        Assert.Contains("Settings → API keys → Google lookups", vm.NewEstimate.IntegrationBanner);
    }

    [Fact]
    public async Task Saving_a_key_turns_lookups_on_immediately_and_never_echoes_it()
    {
        var secrets = new MemorySecretStore();
        var (vm, _, db, _) = await CreateAsync(new GoogleStub((r, _) => GoogleAnswers(r)), secrets);
        using var _db = db;
        var s = vm.Settings;

        Assert.False(s.SaveGoogleKey("   "));
        Assert.False(s.SaveGoogleKey("AIza with spaces"));
        Assert.Empty(secrets.Values);

        Assert.True(s.SaveGoogleKey("  " + Key + "  "));
        Assert.Equal(Key, secrets.Values[SecretNames.GoogleMapsApiKey]);
        Assert.Equal("Key saved", s.GoogleKeyStatus);
        Assert.DoesNotContain(Key, s.GoogleKeyMessage);
        Assert.DoesNotContain(Key, s.StatusMessage);

        await Task.Yield(); // SettingsChanged reloads New estimate
        Assert.True(vm.NewEstimate.CanLookUpAddress);
        Assert.True(vm.NewEstimate.CanLookUpRoute);
        Assert.DoesNotContain("Google lookups", vm.NewEstimate.IntegrationBanner);

        // Reloading Settings only reports that a key exists.
        await s.LoadAsync();
        Assert.True(s.HasGoogleKey);
    }

    [Fact]
    public async Task Test_key_uses_typed_key_or_saved_key_and_reports_result()
    {
        var stub = new GoogleStub((r, _) => GoogleAnswers(r));
        var secrets = new MemorySecretStore();
        var (vm, _, db, _) = await CreateAsync(stub, secrets);
        using var _db = db;
        var s = vm.Settings;

        await s.TestGoogleKeyAsync("");
        Assert.Contains("No key to test", s.GoogleKeyMessage);
        Assert.Empty(stub.Requests);

        await s.TestGoogleKeyAsync(Key);
        Assert.StartsWith("Test passed", s.GoogleKeyMessage);
        var req = Assert.Single(stub.Requests);
        Assert.Equal(Key, req.Headers["X-Goog-Api-Key"]);
        Assert.Equal(SettingsViewModel.TestQuery, req.Json.GetProperty("textQuery").GetString());
        Assert.Empty(secrets.Values); // testing does not save

        s.SaveGoogleKey("AIzaSAVED-key");
        await s.TestGoogleKeyAsync(null);
        Assert.Equal("AIzaSAVED-key", stub.Requests[^1].Headers["X-Goog-Api-Key"]);
    }

    [Fact]
    public async Task Failed_test_shows_googles_reason_without_the_key()
    {
        var stub = new GoogleStub((_, _) => GoogleStub.Json(
            $$$"""{"error":{"code":400,"message":"API key not valid ({{{Key}}}). Please pass a valid API key."}}""", HttpStatusCode.BadRequest));
        var (vm, _, db, _) = await CreateAsync(stub, new MemorySecretStore());
        using var _db = db;
        await vm.Settings.TestGoogleKeyAsync(Key);
        Assert.StartsWith("Test failed", vm.Settings.GoogleKeyMessage);
        Assert.Contains("API key not valid", vm.Settings.GoogleKeyMessage);
        Assert.DoesNotContain(Key, vm.Settings.GoogleKeyMessage);
    }

    [Fact]
    public async Task Removing_the_key_turns_lookups_off()
    {
        var secrets = new MemorySecretStore();
        secrets.Set(SecretNames.GoogleMapsApiKey, Key);
        var (vm, _, db, _) = await CreateAsync(new GoogleStub((r, _) => GoogleAnswers(r)), secrets);
        using var _db = db;
        Assert.True(vm.Settings.HasGoogleKey);
        Assert.True(vm.NewEstimate.CanLookUpRoute);

        vm.Settings.RemoveGoogleKey();
        await Task.Yield();
        Assert.Empty(secrets.Values);
        Assert.Equal("No key", vm.Settings.GoogleKeyStatus);
        Assert.False(vm.NewEstimate.CanLookUpRoute);
        Assert.Contains("Settings → API keys → Google lookups", vm.NewEstimate.IntegrationBanner);
    }

    [Fact]
    public async Task Google_lookup_flow_fills_manual_boxes_and_saves()
    {
        var secrets = new MemorySecretStore();
        secrets.Set(SecretNames.GoogleMapsApiKey, Key);
        var stub = new GoogleStub((r, _) => GoogleAnswers(r));
        var (vm, _, db, _) = await CreateAsync(stub, secrets);
        using var _db = db;
        await WorkflowTests.SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        WorkflowTests.EnterNeshoba(e);

        // Typing the address ran the route lookup already (round trip = 2 calls).
        Assert.Equal(2, stub.Requests.Count(r => r.Uri.Host.StartsWith("routes", StringComparison.Ordinal)));

        // Address lookup shows full addresses with Google attribution; confirming re-runs the route lookup.
        await e.SearchAddressCommand.ExecuteAsync(null);
        var c = Assert.Single(e.Candidates);
        Assert.True(e.ShowPoweredByGoogle);
        e.SelectedCandidate = c;
        await e.ConfirmCandidateCommand.ExecuteAsync(null);
        Assert.False(e.ShowPoweredByGoogle);
        Assert.Equal("1125 Golf Course Rd, Philadelphia, MS 39350, USA", e.DestinationAddress);

        // 201,168 m = 125.0 mi, 8040 s = 2 h 14 min each way, copied into the per-leg boxes.
        Assert.Equal(4, stub.Requests.Count(r => r.Uri.Host.StartsWith("routes", StringComparison.Ordinal)));
        Assert.Equal("125", e.ManualMilesText);
        Assert.Equal(("2", "14"), (e.ManualDriveHoursText, e.ManualDriveMinutesText));
        Assert.Contains("From Google route lookup, copied into manual entry", e.RouteSourceText);
        Assert.Equal("$394.59", e.TotalCostText);

        // Google confirmed (2026-11-19) that copied figures and addresses may be saved.
        await e.SaveCommand.ExecuteAsync(null);
        Assert.StartsWith("Saved estimate", e.StatusMessage);
        Assert.Single(vm.History.Estimates);
        Assert.All(stub.Requests, r => Assert.DoesNotContain(Key, r.Uri.AbsoluteUri));
    }
}
