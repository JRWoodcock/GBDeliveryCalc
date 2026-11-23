using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Core.Model;
using GBDeliveryCalc.Data;
using GBDeliveryCalc.Presentation;
using GBDeliveryCalc.Services;

namespace GBDeliveryCalc.Tests;

internal sealed class FakeClipboard : IClipboardService
{
    public string? Text { get; private set; }
    public void SetText(string text) => Text = text;
}

internal sealed class FakeDialogs : IDialogService
{
    public bool ConfirmAnswer { get; set; } = true;
    public List<string> Confirmations { get; } = [];
    public string? SavePath { get; set; }
    public string? RestorePath { get; set; }
    public string? LastError { get; private set; }
    public string? PickBackupSavePath(string suggestedFileName) => SavePath;
    public string? PickBackupToRestore() => RestorePath;
    public bool Confirm(string title, string message) { Confirmations.Add(message); return ConfirmAnswer; }
    public void ShowError(string title, string message) => LastError = message;
    public string? KeysExportPath { get; set; }
    public string? KeysImportPath { get; set; }
    public Queue<string?> Passwords { get; } = new();
    public string? PickKeysExportPath(string suggestedFileName) => KeysExportPath;
    public string? PickKeysImportPath() => KeysImportPath;
    public string? AskPassword(string title, string prompt, bool confirm) => Passwords.Count > 0 ? Passwords.Dequeue() : null;
}

internal sealed class TestMaintenance(SqliteDatabase db, string dir) : IDatabaseMaintenance
{
    public string DatabasePath => db.DatabasePath;
    public string BackupDirectory => dir;
    public Task BackupAsync(string destinationPath) { DatabaseBackup.BackupTo(db.DatabasePath, destinationPath); return Task.CompletedTask; }
    public async Task<string> RestoreAsync(string backupPath)
    {
        var s = DatabaseBackup.RestoreFrom(backupPath, db.DatabasePath, dir);
        await db.MigrateAsync();
        return s;
    }
}

internal sealed class FakeRouting(Func<RouteLookupResult>? result = null) : IRoutingProvider
{
    /// <summary>Google-like answer: 125.0 mi / 2 h 14 min out, 126.0 mi / 2 h 16 min back.</summary>
    public static FakeRouting BothLegs() => new(() => new RouteLookupResult
    { Outbound = new RouteLeg(125m, 134), Return = new RouteLeg(126m, 136), ProviderName = "Google Routes API", PermitsRetention = false });

    public int Calls { get; private set; }
    public List<(string Origin, string Destination, TripType Trip)> Requests { get; } = [];
    public ProviderInfo Info { get; } = new("Fake router", ProviderStatus.Available, "", false);
    public Task<RouteLookupResult> GetRouteAsync(string origin, string destination, TripType tripType, CancellationToken ct = default)
    {
        Calls++;
        Requests.Add((origin, destination, tripType));
        if (result is null) throw new ProviderUnavailableException("Route lookup: could not reach Google. Check the internet connection.");
        var r = result();
        return Task.FromResult(tripType == TripType.OneWay ? r with { Return = null } : r); // like Google: one way has no return leg
    }
}

internal sealed class FakePlaces : IPlaceSearchProvider
{
    public ProviderInfo Info { get; } = new("Fake places", ProviderStatus.Available, "", false);
    public Task<PlaceSearchResult> SearchAsync(string query, CancellationToken ct = default) => Task.FromResult(new PlaceSearchResult
    {
        ProviderName = "Fake places",
        PermitsRetention = false,
        Candidates = [new PlaceCandidate { DisplayName = "Neshoba Central High School", FormattedAddress = "1125 Golf Course Rd, Philadelphia, MS 39350, USA" }],
    });
}

internal sealed class MemorySecretStore : ISecretStore
{
    public Dictionary<string, string> Values { get; } = [];
    public string? Get(string name) => Values.GetValueOrDefault(name);
    public void Set(string name, string value) => Values[name] = value;
    public void Delete(string name) => Values.Remove(name);
}

public class WorkflowTests
{
    internal static async Task<(MainViewModel vm, FakeClipboard clip, TempDatabase db, EstimatorServices services)> CreateAsync(
        Func<IntegrationKeys, IntegrationSet>? integrations = null, MemorySecretStore? secrets = null, FakeDialogs? dialogs = null)
    {
        var db = await TempDatabase.CreateAsync();
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero));
        var services = new EstimatorServices(
            new SqliteSettingsStore(db.Database), new SqliteEstimateStore(db.Database), new SqliteFuelPriceCache(db.Database),
            new TestMaintenance(db.Database, db.Directory), secrets ?? new MemorySecretStore(),
            integrations ?? (k => ProviderFactory.Create(k)), clock);
        var clip = new FakeClipboard();
        var vm = new MainViewModel(services, clip, dialogs ?? new FakeDialogs());
        await vm.InitializeAsync();
        return (vm, clip, db, services);
    }

    internal static async Task SetUpTeamsAsync(SettingsViewModel s)
    {
        s.OriginAddress = TestData.Origin;
        await s.SaveCompanyCommand.ExecuteAsync(null);

        s.NewVehicleCommand.Execute(null);
        s.VehicleName = "2028 GMC Sierra";
        s.VehicleFuelType = FuelType.RegularUnleaded;
        s.VehicleMpgText = "19";
        s.VehicleOperatingCostText = "0.20";
        await s.SaveVehicleCommand.ExecuteAsync(null);

        s.NewVehicleCommand.Execute(null);
        s.VehicleName = "Box truck";
        s.VehicleFuelType = FuelType.Diesel;
        s.VehicleMpgText = "11";
        await s.SaveVehicleCommand.ExecuteAsync(null);

        s.NewTeamCommand.Execute(null);
        s.TeamName = "Team B";
        s.TeamDescription = "Alex and Sam, Company Truck";
        s.TeamLoadedCostText = "40";
        s.TeamDefaultVehicle = s.VehicleOptions.Single(o => o.Label == "2028 GMC Sierra");
        await s.SaveTeamCommand.ExecuteAsync(null);
        Assert.StartsWith("Team 'Team B' saved", s.StatusMessage);
    }

    /// <summary>Team, customer and typed address only, so a route lookup's figures stay as copied.</summary>
    internal static async Task EnterNeshobaAddressAsync(NewEstimateViewModel e)
    {
        e.SelectedTeam = e.Teams.Single(t => t.Name == "Team B");
        e.CustomerName = "Neshoba County Schools";
        e.ManualFuelPriceText = "3.49";
        e.DestinationQuery = "Neshoba Central High School";
        await e.UseTypedAddressCommand.ExecuteAsync(null);
    }

    internal static void EnterNeshoba(NewEstimateViewModel e)
    {
        e.SelectedTeam = e.Teams.Single(t => t.Name == "Team B");
        e.CustomerName = "Neshoba County Schools";
        e.DestinationQuery = "Neshoba Central High School";
        e.UseTypedAddressCommand.Execute(null);
        e.ManualMilesText = "125";
        e.ManualDriveHoursText = "2";
        e.ManualDriveMinutesText = "14";
        e.ManualFuelPriceText = "3.49";
    }

    private static IntegrationSet Google(FakeRouting routing) => new(new FakePlaces(), routing, new NotConfiguredFuelPriceSource("n/a"));

    [Fact]
    public async Task First_run_opens_settings_with_company_defaults()
    {
        var (vm, _, db, _) = await CreateAsync();
        using var _db = db;
        Assert.Equal(MainViewModel.SettingsTab, vm.SelectedTab);
        Assert.False(vm.Settings.RoundLaborHoursUp);
        Assert.Equal(("3", "0"), (vm.Settings.DefaultInstallHoursText, vm.Settings.DefaultInstallMinutesText));
        Assert.Equal("", vm.Settings.OriginAddress);
        Assert.Contains("Not configured", vm.NewEstimate.IntegrationBanner);
    }

    [Fact]
    public async Task Main_workflow_cost_copy_save_history_actuals()
    {
        var (vm, clip, db, services) = await CreateAsync();
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;

        Assert.Equal(TestData.Origin, e.OriginAddress);
        Assert.Equal("3", e.InstallHoursText);
        EnterNeshoba(e);

        Assert.Equal("2028 GMC Sierra", e.SelectedVehicle!.Name);
        Assert.Contains("Alex and Sam", e.TeamRatesText);
        Assert.Contains("Loaded labor cost $40.00/hr", e.TeamRatesText);
        Assert.Equal("Miles per leg (each way)", e.ManualMilesLabel);

        Assert.Equal("$394.59", e.TotalCostText);
        Assert.Equal(new[] { "$178.67", "$120.00", "$45.92", "$50.00" }, e.CostLines.Select(l => l.Amount));
        Assert.Equal(new[] { "Travel labor cost", "Installation labor cost", "Fuel", "Vehicle operating cost" }, e.CostLines.Select(l => l.Label));
        Assert.Contains("Round trip: 250.0 mi total, 4 h 28 min total driving", e.TripSummary);
        Assert.Contains("Manually entered (per leg)", e.RouteSourceText);
        Assert.Contains("7.47", e.LaborHoursText);

        e.CopyCostEstimateCommand.Execute(null);
        Assert.NotNull(clip.Text);
        Assert.Contains("$394.59", clip.Text);
        Assert.Contains("Team: Team B", clip.Text);
        Assert.Contains("Vehicle: 2028 GMC Sierra", clip.Text);
        Assert.DoesNotContain("Alex", clip.Text);
        Assert.Equal("Cost estimate copied to the clipboard.", e.StatusMessage);

        await e.SaveCommand.ExecuteAsync(null);
        Assert.StartsWith("Saved estimate GB-20261118-001", e.StatusMessage);
        Assert.Contains("GB-20261118-001", e.EstimatePreview);
        Assert.True(e.IsEditing);
        Assert.Equal("Save changes", e.SaveButtonText);
        await e.SaveCommand.ExecuteAsync(null); // double-click must not create a duplicate
        Assert.StartsWith("No changes since GB-20261118-001", e.StatusMessage);

        var h = vm.History;
        var summary = Assert.Single(h.Estimates);
        Assert.Equal(394.59m, summary.TotalCost);
        await h.SelectAsync(summary.Id);
        Assert.Contains("GB-20261118-001", h.EstimateHeader);
        Assert.Equal("$394.59", h.CostLines.Last().Amount);
        Assert.True(h.CanEdit);

        // Change settings after saving: history must not move.
        vm.Settings.SelectedTeam = vm.Settings.Teams.Single();
        vm.Settings.TeamLoadedCostText = "80";
        vm.Settings.TeamDescription = "New crew";
        await vm.Settings.SaveTeamCommand.ExecuteAsync(null);
        vm.Settings.SelectedVehicle = vm.Settings.Vehicles.Single(v => v.Name == "2028 GMC Sierra");
        vm.Settings.VehicleMpgText = "10";
        await vm.Settings.SaveVehicleCommand.ExecuteAsync(null);
        await h.SelectAsync(summary.Id);
        Assert.Equal("$394.59", h.CostLines.Last().Amount);
        Assert.Contains("Alex and Sam", h.TeamAndVehicleText);
        Assert.Contains("19 MPG", h.TeamAndVehicleText);

        // Actuals: estimated vs actual cost.
        h.ActualMilesText = "260";
        h.ActualPaidHoursText = "9.5";
        h.ActualFuelCostText = "52.10";
        await h.SaveActualsCommand.ExecuteAsync(null);
        Assert.Equal("Actuals saved. The estimate itself is unchanged.", h.StatusMessage);
        await h.SelectAsync(summary.Id);
        Assert.Equal("260", h.ActualMilesText);
        var total = h.Comparison.Single(c => c.Label == "Total cost");
        Assert.Equal("$394.59", total.Estimated);
        Assert.Equal("$484.10", total.Actual); // 9.5 × 40 + 52.10 + 260 × 0.20
        Assert.Equal("+$89.51", total.Variance);
        Assert.Contains(h.Assumptions, a => a.Contains("loaded rate saved with the estimate ($40.00/hr)"));
        Assert.Equal(394.59m, (await services.Estimates.GetAsync(summary.Id))!.Result.TotalDirectCost);
    }

    /// <summary>
    /// Regression for the 2026-11-19 "Save is broken" report. Root cause: with a Google key saved, a confirmed Google
    /// address (and any looked-up route) was tagged as provider data that may not be stored, so RetentionPolicy refused
    /// every save ("cannot be saved under its terms"). Confirmed addresses and route figures are now copied into the
    /// editable fields, which Google confirmed is allowed, and they save.
    /// </summary>
    [Fact]
    public async Task Save_works_with_google_address_and_route_lookup()
    {
        var routing = FakeRouting.BothLegs();
        var (vm, _, db, services) = await CreateAsync(_ => Google(routing));
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        e.SelectedTeam = e.Teams.Single();
        e.ManualFuelPriceText = "3.49";
        e.DestinationQuery = "Neshoba Central High School";
        await e.SearchAddressCommand.ExecuteAsync(null);
        e.SelectedCandidate = e.Candidates.Single();
        await e.ConfirmCandidateCommand.ExecuteAsync(null);

        Assert.Equal("1125 Golf Course Rd, Philadelphia, MS 39350, USA", e.DestinationAddress);
        var call = Assert.Single(routing.Requests);
        Assert.Equal(TripType.RoundTrip, call.Trip);
        Assert.Equal("125.5", e.ManualMilesText); // per leg: average of 125.0 and 126.0
        Assert.Equal(("2", "15"), (e.ManualDriveHoursText, e.ManualDriveMinutesText));
        Assert.Contains("From Google route lookup, copied into manual entry (per leg)", e.RouteSourceText);
        Assert.Contains("copied into the boxes", e.LookupMessage);

        await e.SaveCommand.ExecuteAsync(null);
        Assert.StartsWith("Saved estimate GB-20261118-001", e.StatusMessage);
        var saved = await services.Estimates.GetAsync(Assert.Single(vm.History.Estimates).Id);
        Assert.Equal("Google Routes API", saved!.Input.Route!.CopiedFromLookupProvider);
        Assert.Null(saved.Input.DestinationProviderName);
        Assert.Equal("1125 Golf Course Rd, Philadelphia, MS 39350, USA", saved.Input.DestinationAddress);
    }

    [Fact]
    public async Task Route_lookup_respects_trip_totals_and_refills_when_basis_changes()
    {
        var routing = FakeRouting.BothLegs();
        var (vm, _, db, _) = await CreateAsync(_ => Google(routing));
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        e.ManualIsTripTotal = true;
        e.DestinationQuery = "x";
        await e.SearchAddressCommand.ExecuteAsync(null);
        e.SelectedCandidate = e.Candidates.Single();
        await e.ConfirmCandidateCommand.ExecuteAsync(null);
        Assert.Equal("251", e.ManualMilesText);
        Assert.Equal(("4", "30"), (e.ManualDriveHoursText, e.ManualDriveMinutesText));
        Assert.Contains("(trip totals)", e.RouteSourceText);

        e.ManualIsTripTotal = false; // re-entered per leg, not reinterpreted
        Assert.Equal("125.5", e.ManualMilesText);
        Assert.Contains("From Google route lookup", e.RouteSourceText);
        Assert.Single(routing.Requests);
    }

    [Fact]
    public async Task Missing_return_leg_is_doubled_and_noted()
    {
        var routing = new FakeRouting(() => new RouteLookupResult { Outbound = new RouteLeg(125m, 134), Return = null, ProviderName = "Google Routes API" });
        var (vm, _, db, _) = await CreateAsync(_ => Google(routing));
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        EnterNeshoba(e); // "Use as typed" runs the lookup
        Assert.Contains("doubled", e.LookupMessage);
        Assert.Contains("doubling", e.RouteSourceText + string.Join(" ", e.CostNotes));
        Assert.Equal("$394.59", e.TotalCostText);
    }

    [Fact]
    public async Task Changed_destination_makes_copied_figures_stale_until_rerun_or_kept()
    {
        var routing = FakeRouting.BothLegs();
        var (vm, _, db, _) = await CreateAsync(_ => Google(routing));
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        await EnterNeshobaAddressAsync(e);
        Assert.Single(routing.Requests);
        Assert.False(e.RouteIsStale);
        Assert.False(e.ShowRerunRouteLookup);

        e.DestinationAddress = "Philadelphia High School, Philadelphia, MS";
        Assert.True(e.RouteIsStale);
        Assert.True(e.ShowRerunRouteLookup);
        Assert.Equal("Incomplete", e.TotalCostText);
        await e.RunRouteLookupCommand.ExecuteAsync(null);
        Assert.Equal(2, routing.Requests.Count);
        Assert.Equal("Philadelphia High School, Philadelphia, MS", routing.Requests[1].Destination);
        Assert.False(e.RouteIsStale);

        // A trip-type change recalculates instead of going stale.
        e.IsRoundTrip = false;
        Assert.False(e.RouteIsStale);
        Assert.Contains("One way: 125.0 mi", e.TripSummary);
    }

    [Fact]
    public async Task Typing_over_copied_figures_makes_them_manual()
    {
        var (vm, _, db, _) = await CreateAsync(_ => Google(FakeRouting.BothLegs()));
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        await EnterNeshobaAddressAsync(e);
        Assert.Contains("From Google route lookup", e.RouteSourceText);
        e.ManualMilesText = "130";
        Assert.Contains("Manually entered (per leg)", e.RouteSourceText);
        e.DestinationAddress = "Elsewhere";
        Assert.False(e.RouteIsStale); // plain manual figures don't go stale
    }

    [Fact]
    public async Task Vehicle_override_does_not_change_team_default()
    {
        var (vm, _, db, services) = await CreateAsync();
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        EnterNeshoba(e);
        e.SelectedVehicle = e.Vehicles.Single(v => v.Name == "Box truck");
        Assert.True(e.VehicleOverridden);
        Assert.Contains("overriding team default", e.VehicleText);
        Assert.Contains("Diesel", e.FuelStatusText); // typed price follows the vehicle's fuel type
        Assert.Contains("Operating cost per mile for Box truck", e.CostIssues);
        vm.Settings.SelectedVehicle = vm.Settings.Vehicles.Single(v => v.Name == "Box truck");
        vm.Settings.VehicleOperatingCostText = "0.35";
        await vm.Settings.SaveVehicleCommand.ExecuteAsync(null);
        e.SelectedVehicle = e.Vehicles.Single(v => v.Name == "Box truck");
        await e.SaveCommand.ExecuteAsync(null);

        var team = (await services.Settings.GetTeamsAsync(false)).Single();
        var sierra = (await services.Settings.GetVehiclesAsync(false)).Single(v => v.Name == "2028 GMC Sierra");
        Assert.Equal(sierra.Id, team.DefaultVehicleId);
        var saved = await services.Estimates.GetAsync(vm.History.Estimates.Single().Id);
        Assert.True(saved!.Input.VehicleOverridden);
        Assert.Equal("Box truck", saved.Input.Vehicle!.Name);
    }

    [Fact]
    public async Task Archived_team_is_hidden_from_picker_but_kept_on_history()
    {
        var (vm, _, db, _) = await CreateAsync();
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        EnterNeshoba(vm.NewEstimate);
        await vm.NewEstimate.SaveCommand.ExecuteAsync(null);

        vm.Settings.SelectedTeam = vm.Settings.Teams.Single();
        await vm.Settings.ToggleTeamArchivedCommand.ExecuteAsync(null);
        await vm.NewEstimate.StartNewAsync();
        Assert.Empty(vm.NewEstimate.Teams);

        await vm.History.SelectAsync(vm.History.Estimates.Single().Id);
        Assert.Contains("Team: Team B", vm.History.TeamAndVehicleText);
    }

    [Fact]
    public async Task Trip_type_switch_recomputes_per_leg_and_keeps_installation()
    {
        var (vm, _, db, _) = await CreateAsync();
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        EnterNeshoba(e);
        e.IsRoundTrip = false;
        Assert.Equal("Miles (one way)", e.ManualMilesLabel);
        Assert.Contains("One way: 125.0 mi", e.TripSummary);
        Assert.Contains(e.CostLines, l => l.Label == "Installation labor cost" && l.Amount == "$120.00");
        Assert.Equal("$257.29", e.TotalCostText);
    }

    [Fact]
    public async Task Hand_typed_trip_totals_are_halved_or_doubled_when_trip_type_changes()
    {
        var (vm, _, db, _) = await CreateAsync();
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        EnterNeshoba(e);
        e.ManualIsTripTotal = true;
        Assert.Equal("Total trip miles", e.ManualMilesLabel);
        e.ManualMilesText = "250";
        e.ManualDriveHoursText = "4";
        e.ManualDriveMinutesText = "28";
        Assert.Equal("$394.59", e.TotalCostText);

        e.IsRoundTrip = false;
        Assert.False(e.RouteIsStale);
        Assert.Equal("125", e.ManualMilesText);
        Assert.Equal(("2", "14"), (e.ManualDriveHoursText, e.ManualDriveMinutesText));
        Assert.Contains("halved", e.LookupMessage);
        Assert.Equal("$257.29", e.TotalCostText);

        e.IsRoundTrip = true;
        Assert.Equal("250", e.ManualMilesText);
        Assert.Equal(("4", "28"), (e.ManualDriveHoursText, e.ManualDriveMinutesText));
        Assert.Contains("doubled", e.LookupMessage);
        Assert.Equal("$394.59", e.TotalCostText);
    }

    [Fact]
    public async Task Trip_type_toggle_recalculates_figures_copied_from_a_lookup()
    {
        var routing = FakeRouting.BothLegs();
        var (vm, _, db, _) = await CreateAsync(_ => Google(routing));
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        await EnterNeshobaAddressAsync(e);
        Assert.Equal("125.5", e.ManualMilesText);
        Assert.Contains("Round trip: 251.0 mi total", e.TripSummary);

        e.IsRoundTrip = false; // per leg: the outbound leg
        Assert.False(e.RouteIsStale);
        Assert.Equal("125", e.ManualMilesText);
        Assert.Equal(("2", "14"), (e.ManualDriveHoursText, e.ManualDriveMinutesText));
        Assert.Contains("One way: 125.0 mi total, 2 h 14 min total driving", e.TripSummary);
        Assert.Contains("From Google route lookup", e.RouteSourceText);
        Assert.NotEqual("Incomplete", e.TotalCostText);

        e.IsRoundTrip = true; // back to the average of both directions
        Assert.Equal("125.5", e.ManualMilesText);
        Assert.Contains("Round trip: 251.0 mi total", e.TripSummary);

        e.ManualIsTripTotal = true;
        Assert.Equal("251", e.ManualMilesText);
        e.IsRoundTrip = false; // trip totals: the outbound leg only
        Assert.False(e.RouteIsStale);
        Assert.Equal("125", e.ManualMilesText);
        Assert.Contains("One way: 125.0 mi total", e.TripSummary);
        e.IsRoundTrip = true;
        Assert.Equal("251", e.ManualMilesText);
        Assert.Equal(("4", "30"), (e.ManualDriveHoursText, e.ManualDriveMinutesText));
        Assert.Single(routing.Requests); // derived from the stored legs, no new lookup
    }

    [Fact]
    public async Task Switching_a_one_way_lookup_to_round_trip_looks_the_route_up_again()
    {
        var routing = FakeRouting.BothLegs();
        var (vm, _, db, _) = await CreateAsync(_ => Google(routing));
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        e.IsRoundTrip = false;
        await EnterNeshobaAddressAsync(e);
        Assert.Equal(TripType.OneWay, routing.Requests.Single().Trip);

        e.IsRoundTrip = true;
        await Task.Delay(50);
        Assert.Equal(2, routing.Requests.Count);
        Assert.Equal(TripType.RoundTrip, routing.Requests[1].Trip);
        Assert.False(e.RouteIsStale);
        Assert.Equal("125.5", e.ManualMilesText);
    }

    [Fact]
    public async Task Address_candidates_show_full_address_before_confirming()
    {
        var (vm, _, db, _) = await CreateAsync(_ => Google(new FakeRouting()));
        using var _db = db;
        var e = vm.NewEstimate;
        e.DestinationQuery = "Neshoba Central High School";
        await e.SearchAddressCommand.ExecuteAsync(null);
        var c = Assert.Single(e.Candidates);
        Assert.Equal("1125 Golf Course Rd, Philadelphia, MS 39350, USA", c.FormattedAddress);
        Assert.Equal("", e.DestinationAddress);
        e.SelectedCandidate = c;
        await e.ConfirmCandidateCommand.ExecuteAsync(null);
        Assert.Equal(c.FormattedAddress, e.DestinationAddress);
    }

    [Fact]
    public async Task Route_provider_failure_keeps_manual_entry_working()
    {
        var (vm, _, db, _) = await CreateAsync(_ => Google(new FakeRouting()));
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        EnterNeshoba(e);
        Assert.Contains("could not reach Google", e.LookupMessage);
        Assert.Contains("manually", e.LookupMessage);
        Assert.Equal("$394.59", e.TotalCostText);
        Assert.Contains("Manually entered", e.RouteSourceText);
    }

    [Fact]
    public async Task Missing_inputs_are_listed_and_block_copy_and_save()
    {
        var (vm, clip, db, _) = await CreateAsync();
        using var _db = db;
        var e = vm.NewEstimate;
        e.CopyCostEstimateCommand.Execute(null);
        Assert.Null(clip.Text);
        Assert.Contains("Team", e.CostIssues);
        Assert.Contains("Trip miles and drive time", e.CostIssues);
        await e.SaveCommand.ExecuteAsync(null);
        Assert.StartsWith("Cannot save:", e.StatusMessage);

        e.ManualMilesText = "abc";
        e.ManualDriveHoursText = "1";
        Assert.Contains("Miles is not a number", e.CostIssues);
        Assert.Equal("Incomplete", e.TotalCostText);
    }

    [Fact]
    public async Task Missing_loaded_cost_flags_the_total_incomplete()
    {
        var (vm, clip, db, _) = await CreateAsync();
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        vm.Settings.SelectedTeam = vm.Settings.Teams.Single();
        vm.Settings.TeamLoadedCostText = "";
        await vm.Settings.SaveTeamCommand.ExecuteAsync(null);
        var e = vm.NewEstimate;
        EnterNeshoba(e);
        Assert.Equal("Incomplete", e.TotalCostText);
        Assert.Contains("Loaded labor cost per hour for Team B", e.CostIssues);
        Assert.Equal("Incomplete", e.CostLines[0].Amount);
        Assert.Equal("$45.92", e.CostLines.Single(l => l.Label == "Fuel").Amount);
        e.CopyCostEstimateCommand.Execute(null);
        Assert.Null(clip.Text);
    }

    [Fact]
    public async Task Round_up_setting_applies_to_new_estimates_and_is_saved_with_them()
    {
        var (vm, _, db, services) = await CreateAsync();
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        vm.Settings.RoundLaborHoursUp = true;
        await vm.Settings.SaveCompanyCommand.ExecuteAsync(null);
        var e = vm.NewEstimate;
        EnterNeshoba(e);
        Assert.Equal("$200.00", e.CostLines[0].Amount);
        Assert.Contains("rounded up to 5 h", e.CostLines[0].Detail);
        Assert.Equal("$120.00", e.CostLines[1].Amount);
        Assert.Equal("$415.92", e.TotalCostText);
        Assert.Contains("rounded up to whole hours", e.LaborHoursText);
        await e.SaveCommand.ExecuteAsync(null);

        // Turning it off later doesn't change the saved estimate.
        vm.Settings.RoundLaborHoursUp = false;
        await vm.Settings.SaveCompanyCommand.ExecuteAsync(null);
        var saved = await services.Estimates.GetAsync(vm.History.Estimates.Single().Id);
        Assert.True(saved!.Input.RoundLaborHoursUp);
        Assert.Equal(415.92m, saved.Result.TotalDirectCost);
    }

    [Fact]
    public async Task Paid_hours_override_is_shown_and_costed()
    {
        var (vm, _, db, _) = await CreateAsync();
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        EnterNeshoba(e);
        e.PaidHoursOverrideText = "9";
        Assert.Equal("Labor cost (paid hours override)", e.CostLines[0].Label);
        Assert.Equal("$360.00", e.CostLines[0].Amount);
        Assert.Contains("Paid hours override: 9.00 h", e.LaborHoursText);
        Assert.Equal("$455.92", e.TotalCostText);
    }

    [Fact]
    public async Task Edit_from_history_updates_in_place_and_keeps_the_previous_version()
    {
        var (vm, _, db, services) = await CreateAsync();
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        EnterNeshoba(e);
        await e.SaveCommand.ExecuteAsync(null);
        await e.StartNewAsync();
        Assert.False(e.IsEditing);

        // Settings change after saving: the edited estimate keeps its own team rate.
        vm.Settings.SelectedTeam = vm.Settings.Teams.Single();
        vm.Settings.TeamLoadedCostText = "80";
        await vm.Settings.SaveTeamCommand.ExecuteAsync(null);

        var h = vm.History;
        var id = h.Estimates.Single().Id;
        await h.SelectAsync(id);
        vm.SelectedTab = MainViewModel.HistoryTab;
        h.EditCommand.Execute(null);
        await Task.Delay(50);
        Assert.Equal(MainViewModel.NewEstimateTab, vm.SelectedTab);
        Assert.True(e.IsEditing);
        Assert.Equal(id, e.EditingId);
        Assert.Contains("Editing saved estimate GB-20261118-001", e.EditingText);
        Assert.Equal("Neshoba County Schools", e.CustomerName);
        Assert.Equal("125", e.ManualMilesText);
        Assert.Contains("$40.00/hr", e.TeamRatesText);
        Assert.Equal("$394.59", e.TotalCostText);

        e.InstallHoursText = "4";
        await e.SaveCommand.ExecuteAsync(null);
        Assert.StartsWith("Saved changes to GB-20261118-001 (version 2", e.StatusMessage);
        var summary = Assert.Single(h.Estimates);
        Assert.Equal("GB-20261118-001", summary.QuoteNumber);
        Assert.Equal(434.59m, summary.TotalCost);
        var revisions = await services.Estimates.GetRevisionsAsync(id);
        Assert.Equal(394.59m, Assert.Single(revisions).Snapshot.Result.TotalDirectCost);
        await h.SelectAsync(id);
        Assert.Contains("edited", h.EstimateHeader);
    }

    [Fact]
    public async Task Delete_from_history_confirms_and_removes_the_estimate()
    {
        var dialogs = new FakeDialogs();
        var (vm, _, db, services) = await CreateAsync(dialogs: dialogs);
        using var _db = db;
        await SetUpTeamsAsync(vm.Settings);
        var e = vm.NewEstimate;
        EnterNeshoba(e);
        await e.SaveCommand.ExecuteAsync(null);
        var h = vm.History;
        var id = h.Estimates.Single().Id;
        h.ActualFuelCostText = "50";
        await h.SelectAsync(id);
        h.ActualFuelCostText = "50";
        await h.SaveActualsCommand.ExecuteAsync(null);
        await h.SelectAsync(id);

        dialogs.ConfirmAnswer = false;
        await h.DeleteCommand.ExecuteAsync(null);
        Assert.Single(h.Estimates);

        dialogs.ConfirmAnswer = true;
        await h.DeleteCommand.ExecuteAsync(null);
        Assert.Contains("GB-20261118-001", dialogs.Confirmations.Last());
        Assert.Empty(h.Estimates);
        Assert.False(h.HasSelection);
        Assert.Null(await services.Estimates.GetAsync(id));
        Assert.Null(await services.Estimates.GetActualsAsync(id));
        Assert.False(e.IsEditing); // the form was editing it; saving now creates a new estimate
        await e.SaveCommand.ExecuteAsync(null);
        Assert.StartsWith("Saved estimate GB-20261118-001", e.StatusMessage);
    }

    [Fact]
    public async Task Settings_backup_and_restore()
    {
        var db = await TempDatabase.CreateAsync();
        using var _db = db;
        var dialogs = new FakeDialogs();
        var services = new EstimatorServices(new SqliteSettingsStore(db.Database), new SqliteEstimateStore(db.Database),
            new SqliteFuelPriceCache(db.Database), new TestMaintenance(db.Database, db.Directory), new MemorySecretStore(), k => ProviderFactory.Create(k), TimeProvider.System);
        var vm = new MainViewModel(services, new FakeClipboard(), dialogs);
        await vm.InitializeAsync();
        await SetUpTeamsAsync(vm.Settings);

        dialogs.SavePath = Path.Combine(db.Directory, "x.estimator-backup");
        await vm.Settings.BackupCommand.ExecuteAsync(null);
        Assert.Null(dialogs.LastError);
        Assert.True(File.Exists(dialogs.SavePath));

        vm.Settings.SelectedTeam = vm.Settings.Teams.Single();
        vm.Settings.TeamName = "Changed";
        await vm.Settings.SaveTeamCommand.ExecuteAsync(null);

        dialogs.RestorePath = dialogs.SavePath;
        await vm.Settings.RestoreCommand.ExecuteAsync(null);
        Assert.Null(dialogs.LastError);
        Assert.Equal("Team B", vm.Settings.Teams.Single().Name);
        Assert.Equal("Team B", vm.NewEstimate.Teams.Single().Name);
    }
}
