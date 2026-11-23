using GBDeliveryCalc.Core.Calculation;
using GBDeliveryCalc.Core.Model;
using GBDeliveryCalc.Core.Persistence;
using GBDeliveryCalc.Core.Quotes;
using GBDeliveryCalc.Data;
using Microsoft.EntityFrameworkCore;

namespace GBDeliveryCalc.Tests;

public class PersistenceTests
{
    private static async Task<(TeamProfile team, VehicleProfile vehicle)> SeedAsync(SqliteSettingsStore s)
    {
        var vehicle = await s.SaveVehicleAsync(new VehicleProfile { Name = "2028 GMC Sierra", FuelType = FuelType.RegularUnleaded, EstimatedMpg = 19m, OperatingCostPerMileExcludingFuel = 0.20m });
        var team = await s.SaveTeamAsync(new TeamProfile { Name = "Team B", Description = "Alex and Sam, Company Truck", LoadedLaborCostPerHour = 40m, DefaultVehicleId = vehicle.Id });
        return (team, vehicle);
    }

    private static EstimateSnapshot Snapshot(TeamProfile team, VehicleProfile vehicle, string number)
    {
        var input = TestData.Neshoba(loaded: team.LoadedLaborCostPerHour, opCost: vehicle.OperatingCostPerMileExcludingFuel) with
        {
            Team = team.ToSnapshot(),
            Vehicle = vehicle.ToSnapshot(),
        };
        var result = EstimateCalculator.Calculate(input);
        return new EstimateSnapshot
        {
            QuoteNumber = number,
            CreatedAt = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(-6)),
            CustomerName = "Neshoba County Schools",
            Input = input,
            Result = result,
            EstimateText = CostEstimateFormatter.Format(input, result, number),
        };
    }

    [Fact]
    public async Task Migrations_apply_and_seed_company_defaults()
    {
        using var t = await TempDatabase.CreateAsync();
        await using (var db = new EstimatorDbContext(EstimatorDbContext.OptionsFor(t.Path)))
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var c = await new SqliteSettingsStore(t.Database).GetCompanySettingsAsync();
        Assert.Equal("", c.OriginAddress);
        Assert.Equal(180, c.DefaultInstallMinutes);
        Assert.False(c.RoundLaborHoursUp);
    }

    [Fact]
    public async Task Migrate_is_idempotent()
    {
        using var t = await TempDatabase.CreateAsync();
        await t.Database.MigrateAsync();
        await t.Database.MigrateAsync();
    }

    [Fact]
    public async Task Teams_and_vehicles_crud_and_archive()
    {
        using var t = await TempDatabase.CreateAsync();
        var s = new SqliteSettingsStore(t.Database);
        var (team, vehicle) = await SeedAsync(s);
        await s.SaveTeamAsync(new TeamProfile { Name = "Team A", Description = "Chris, alone" });

        Assert.Equal(2, (await s.GetTeamsAsync(false)).Count);
        var unset = (await s.GetTeamsAsync(false)).Single(x => x.Name == "Team A");
        Assert.Null(unset.LoadedLaborCostPerHour); // allowed to stay unset during setup

        await s.SaveTeamAsync(team with { IsArchived = true });
        await s.SaveVehicleAsync(vehicle with { IsArchived = true });
        Assert.Single(await s.GetTeamsAsync(false));
        Assert.Equal(2, (await s.GetTeamsAsync(true)).Count);
        Assert.Empty(await s.GetVehiclesAsync(false));
        Assert.Single(await s.GetVehiclesAsync(true));

        await Assert.ThrowsAsync<ArgumentException>(() => s.SaveTeamAsync(new TeamProfile { Name = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => s.SaveVehicleAsync(new VehicleProfile { Name = "X", EstimatedMpg = 0m }));
    }

    [Fact]
    public async Task Company_settings_round_trip()
    {
        using var t = await TempDatabase.CreateAsync();
        var s = new SqliteSettingsStore(t.Database);
        await s.SaveCompanySettingsAsync(new CompanySettings
        {
            OriginAddress = "  123 Example St, Hattiesburg, MS  ",
            DefaultInstallMinutes = 150,
            RoundLaborHoursUp = true,
        });
        var c = await s.GetCompanySettingsAsync();
        Assert.Equal("123 Example St, Hattiesburg, MS", c.OriginAddress);
        Assert.Equal(150, c.DefaultInstallMinutes);
        Assert.True(c.RoundLaborHoursUp);
    }

    [Fact]
    public async Task Editing_team_or_vehicle_after_saving_leaves_history_intact()
    {
        using var t = await TempDatabase.CreateAsync();
        var settings = new SqliteSettingsStore(t.Database);
        var store = new SqliteEstimateStore(t.Database);
        var (team, vehicle) = await SeedAsync(settings);
        var saved = await store.SaveNewAsync(Snapshot(team, vehicle, "GB-20261118-001"));

        // Change everything the estimate depended on, then archive.
        await settings.SaveTeamAsync(team with { Name = "Renamed", Description = "Someone else", LoadedLaborCostPerHour = 70m, IsArchived = true });
        await settings.SaveVehicleAsync(vehicle with { Name = "Other truck", EstimatedMpg = 8m, OperatingCostPerMileExcludingFuel = 1m, IsArchived = true });
        await settings.SaveCompanySettingsAsync(new CompanySettings { RoundLaborHoursUp = true });

        var loaded = await store.GetAsync(saved.Id);
        Assert.NotNull(loaded);
        Assert.Equal(394.59m, loaded.Result.TotalDirectCost);
        Assert.Equal(178.67m, loaded.Result.CostLines[0].Amount);
        Assert.Equal(45.92m, loaded.Result.FuelCost);
        Assert.Equal("Team B", loaded.Input.Team!.Name);
        Assert.Equal("Alex and Sam, Company Truck", loaded.Input.Team.Description);
        Assert.Equal(40m, loaded.Input.Team.LoadedLaborCostPerHour);
        Assert.Equal(19m, loaded.Input.Vehicle!.EstimatedMpg);
        Assert.False(loaded.Input.RoundLaborHoursUp);
        Assert.Equal(3.49m, loaded.Input.Fuel!.PricePerGallon);
        Assert.Equal(saved.EstimateText, loaded.EstimateText);
        Assert.Equal(saved.Result.CostLines, loaded.Result.CostLines);

        var summary = Assert.Single(await store.GetSummariesAsync());
        Assert.Equal("Team B", summary.TeamName);
        Assert.Equal(394.59m, summary.TotalCost);
    }

    [Fact]
    public async Task Recording_actuals_does_not_change_the_estimate()
    {
        using var t = await TempDatabase.CreateAsync();
        var settings = new SqliteSettingsStore(t.Database);
        var store = new SqliteEstimateStore(t.Database);
        var (team, vehicle) = await SeedAsync(settings);
        var saved = await store.SaveNewAsync(Snapshot(team, vehicle, "GB-20261118-001"));

        string Raw()
        {
            using var db = new EstimatorDbContext(EstimatorDbContext.OptionsFor(t.Path));
            return db.Estimates.AsNoTracking().Single().SnapshotJson;
        }
        var before = Raw();

        var actuals = new EstimateActuals { TripMiles = 262m, FuelCost = 50m, OtherDirectCosts = [], UpdatedAt = DateTimeOffset.Now };
        await store.SaveActualsAsync(saved.Id, actuals);
        await store.SaveActualsAsync(saved.Id, actuals with { FuelCost = 51m }); // edit actuals again

        Assert.Equal(before, Raw());
        var a = await store.GetActualsAsync(saved.Id);
        Assert.Equal(51m, a!.FuelCost);
        Assert.NotNull(a.OtherDirectCosts);
        Assert.True(Assert.Single(await store.GetSummariesAsync()).HasActuals);
        Assert.Equal(394.59m, (await store.GetAsync(saved.Id))!.Result.TotalDirectCost);
    }

    [Fact]
    public async Task Saving_an_existing_estimate_as_new_is_refused()
    {
        using var t = await TempDatabase.CreateAsync();
        var settings = new SqliteSettingsStore(t.Database);
        var store = new SqliteEstimateStore(t.Database);
        var (team, vehicle) = await SeedAsync(settings);
        var snap = await store.SaveNewAsync(Snapshot(team, vehicle, "GB-20261118-001"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveNewAsync(snap with { QuoteNumber = "GB-20261118-099" }));
    }

    [Fact]
    public async Task Quote_numbers_increment_per_day()
    {
        using var t = await TempDatabase.CreateAsync();
        var settings = new SqliteSettingsStore(t.Database);
        var store = new SqliteEstimateStore(t.Database);
        var (team, vehicle) = await SeedAsync(settings);
        var day = new DateOnly(2026, 11, 18);
        Assert.Equal("GB-20261118-001", await store.NextQuoteNumberAsync(day));
        await store.SaveNewAsync(Snapshot(team, vehicle, await store.NextQuoteNumberAsync(day)));
        Assert.Equal("GB-20261118-002", await store.NextQuoteNumberAsync(day));
        Assert.Equal("GB-20261119-001", await store.NextQuoteNumberAsync(day.AddDays(1)));
    }

    [Fact]
    public async Task Provider_data_without_retention_rights_is_not_saved()
    {
        using var t = await TempDatabase.CreateAsync();
        var settings = new SqliteSettingsStore(t.Database);
        var store = new SqliteEstimateStore(t.Database);
        var (team, vehicle) = await SeedAsync(settings);
        var snap = Snapshot(team, vehicle, "GB-20261118-001");
        var lookup = RouteFigures.FromLookup(TripType.RoundTrip, new RouteLeg(125m, 134), null, snap.Input.CurrentRouteKey, "Google Routes API", providerPermitsRetention: false);

        await Assert.ThrowsAsync<RetentionNotPermittedException>(() => store.SaveNewAsync(snap with { Input = snap.Input with { Route = lookup } }));
        await Assert.ThrowsAsync<RetentionNotPermittedException>(() => store.SaveNewAsync(snap with
        {
            Input = snap.Input with { DestinationProviderName = "Google Places API", DestinationProviderPermitsRetention = false },
        }));
        Assert.Empty(await store.GetSummariesAsync());

        // A provider whose terms do permit retention can be saved.
        var ok = RouteFigures.FromLookup(TripType.RoundTrip, new RouteLeg(125m, 134), null, snap.Input.CurrentRouteKey, "Approved router", true);
        await store.SaveNewAsync(snap with { Input = snap.Input with { Route = ok } });
        Assert.Single(await store.GetSummariesAsync());
    }

    [Fact]
    public async Task Copied_lookup_figures_and_addresses_save()
    {
        using var t = await TempDatabase.CreateAsync();
        var settings = new SqliteSettingsStore(t.Database);
        var store = new SqliteEstimateStore(t.Database);
        var (team, vehicle) = await SeedAsync(settings);
        var snap = Snapshot(team, vehicle, "GB-20261118-001");
        var copied = RouteFigures.ManualPerLeg(TripType.RoundTrip, 125m, 134, snap.Input.CurrentRouteKey) with { CopiedFromLookupProvider = "Google Routes API" };
        await store.SaveNewAsync(snap with { Input = snap.Input with { Route = copied, DestinationAddress = "1125 Golf Course Rd, Philadelphia, MS 39350, USA" } });
        var loaded = await store.GetAsync(snap.Id);
        Assert.Equal("Google Routes API", loaded!.Input.Route!.CopiedFromLookupProvider);
        Assert.Contains("From Google route lookup, copied into manual entry", loaded.Input.Route.ProvenanceLabel);
    }

    [Fact]
    public async Task Update_keeps_number_and_stores_previous_version()
    {
        using var t = await TempDatabase.CreateAsync();
        var settings = new SqliteSettingsStore(t.Database);
        var store = new SqliteEstimateStore(t.Database);
        var (team, vehicle) = await SeedAsync(settings);
        var first = await store.SaveNewAsync(Snapshot(team, vehicle, "GB-20261118-001"));

        var edited = first with { QuoteNumber = "IGNORED", CustomerName = "Edited customer", Input = first.Input with { InstallMinutes = 240 } };
        edited = edited with { Result = EstimateCalculator.Calculate(edited.Input) };
        var updated = await store.UpdateAsync(edited);
        Assert.Equal("GB-20261118-001", updated.QuoteNumber);
        Assert.Equal(first.CreatedAt, updated.CreatedAt);
        Assert.Equal(2, updated.Revision);
        Assert.NotNull(updated.UpdatedAt);

        var loaded = await store.GetAsync(first.Id);
        Assert.Equal("Edited customer", loaded!.CustomerName);
        Assert.Equal(160.00m, loaded.Result.CostLines.Single(l => l.Kind == CostLineKind.InstallationLabor).Amount);
        var summary = Assert.Single(await store.GetSummariesAsync());
        Assert.Equal(loaded.Result.TotalDirectCost, summary.TotalCost);
        Assert.Equal("Edited customer", summary.CustomerName);

        var rev = Assert.Single(await store.GetRevisionsAsync(first.Id));
        Assert.Equal(1, rev.Revision);
        Assert.Equal("Neshoba County Schools", rev.Snapshot.CustomerName);
        Assert.Equal(394.59m, rev.Snapshot.Result.TotalDirectCost);

        await store.UpdateAsync(loaded with { CustomerName = "Third" });
        Assert.Equal(2, (await store.GetRevisionsAsync(first.Id)).Count);
        Assert.Equal(3, (await store.GetAsync(first.Id))!.Revision);
    }

    [Fact]
    public async Task Delete_removes_estimate_actuals_and_revisions()
    {
        using var t = await TempDatabase.CreateAsync();
        var settings = new SqliteSettingsStore(t.Database);
        var store = new SqliteEstimateStore(t.Database);
        var (team, vehicle) = await SeedAsync(settings);
        var keep = await store.SaveNewAsync(Snapshot(team, vehicle, "GB-20261118-001"));
        var gone = await store.SaveNewAsync(Snapshot(team, vehicle, "GB-20261118-002"));
        await store.SaveActualsAsync(gone.Id, new EstimateActuals { FuelCost = 40m, UpdatedAt = DateTimeOffset.Now });
        await store.UpdateAsync(gone with { CustomerName = "v2" });

        Assert.True(await store.DeleteAsync(gone.Id));
        Assert.False(await store.DeleteAsync(gone.Id));
        Assert.Null(await store.GetAsync(gone.Id));
        Assert.Null(await store.GetActualsAsync(gone.Id));
        Assert.Empty(await store.GetRevisionsAsync(gone.Id));
        Assert.Equal(keep.Id, Assert.Single(await store.GetSummariesAsync()).Id);
        await using var db = new EstimatorDbContext(EstimatorDbContext.OptionsFor(t.Path));
        Assert.Equal(0, await db.EstimateActuals.CountAsync());
        Assert.Equal(0, await db.EstimateRevisions.CountAsync());
    }

    [Fact]
    public async Task Billing_era_snapshot_opens_read_only_with_its_stored_lines()
    {
        using var t = await TempDatabase.CreateAsync();
        var store = new SqliteEstimateStore(t.Database);
        var id = Guid.NewGuid();
        var json = "{\"SchemaVersion\":1,\"Id\":\"ID\",\"QuoteNumber\":\"GB-20261118-001\",\"CreatedAt\":\"2026-11-18T10:00:00-06:00\",\"CustomerName\":\"Old customer\",\"Input\":{\"Team\":{\"TeamId\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"Team B\",\"Description\":\"Crew\",\"BillingRatePerHour\":55,\"LoadedLaborCostPerHour\":40,\"WasArchived\":false},\"OriginAddress\":\"Shop\",\"DestinationAddress\":\"Site\",\"TripType\":\"RoundTrip\",\"InstallMinutes\":180,\"BillingMode\":\"AllInMileage\",\"AllInMileageRatePerMile\":0.725,\"OtherDirectCosts\":[],\"Notes\":\"\"},\"Result\":{\"TotalMiles\":250,\"TotalDriveMinutes\":268,\"InstallMinutes\":180,\"RouteValidity\":\"Current\",\"QuoteLines\":[{\"Kind\":\"TravelLabor\",\"Label\":\"Travel labor\",\"Detail\":\"\",\"Amount\":245.67}],\"QuoteTotal\":591.92,\"QuoteIssues\":[],\"PaidHours\":7.4667,\"CostLines\":[{\"Label\":\"Labor\",\"Detail\":\"7.47 paid hrs @ $40.00/hr\",\"Amount\":298.67},{\"Label\":\"Fuel\",\"Detail\":\"\",\"Amount\":45.92},{\"Label\":\"Other vehicle cost\",\"Detail\":\"\",\"Amount\":50}],\"LaborCost\":298.67,\"FuelCost\":45.92,\"OtherVehicleCost\":50,\"OtherDirectCostsTotal\":0,\"TotalDirectCost\":394.59,\"GrossProfit\":197.33,\"GrossMargin\":0.3334,\"CostIssues\":[]},\"CustomerQuoteText\":\"Grover Brothers quote. Total $591.92\"}".Replace("\"ID\"", "\"" + id + "\"");
        await using (var db = new EstimatorDbContext(EstimatorDbContext.OptionsFor(t.Path)))
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Estimates (Id, QuoteNumber, CreatedAt, CustomerName, Destination, TeamName, TotalCost, SnapshotSchemaVersion, SnapshotJson) VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8})",
                id.ToString().ToUpperInvariant(), "GB-20261118-001", "2026-11-18 10:00:00-06:00", "Old customer", "Site", "Team B", "394.59", 1, json);
        }
        var s = await store.GetAsync(id);
        Assert.NotNull(s);
        Assert.True(s.IsLegacy);
        Assert.Equal(3, s.Result.CostLines.Count);
        Assert.Equal(298.67m, s.Result.CostLines[0].Amount);
        Assert.Equal(394.59m, s.Result.TotalDirectCost);
        Assert.Contains("$591.92", s.DisplayText);
        var summary = Assert.Single(await store.GetSummariesAsync());
        Assert.True(summary.IsLegacy);
        Assert.Equal(394.59m, summary.TotalCost);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(s));
    }

    [Fact]
    public async Task Fuel_cache_returns_latest_per_type()
    {
        using var t = await TempDatabase.CreateAsync();
        var cache = new SqliteFuelPriceCache(t.Database);
        var now = DateTimeOffset.Now;
        await cache.SaveAsync(new FuelPrice { FuelType = FuelType.RegularUnleaded, PricePerGallon = 3.40m, Source = "S", RetrievedAt = now.AddDays(-1), IsLive = true });
        await cache.SaveAsync(new FuelPrice { FuelType = FuelType.RegularUnleaded, PricePerGallon = 3.49m, Source = "S", RetrievedAt = now, IsLive = true, PublishedDate = new DateOnly(2026, 11, 18) });
        await cache.SaveAsync(new FuelPrice { FuelType = FuelType.Diesel, PricePerGallon = 3.99m, Source = "S", RetrievedAt = now, IsLive = true });
        Assert.Equal(3.49m, (await cache.GetLatestAsync(FuelType.RegularUnleaded))!.PricePerGallon);
        Assert.Equal(new DateOnly(2026, 11, 18), (await cache.GetLatestAsync(FuelType.RegularUnleaded))!.PublishedDate);
        Assert.Equal(3.99m, (await cache.GetLatestAsync(FuelType.Diesel))!.PricePerGallon);
    }

    [Fact]
    public async Task Backup_and_restore_round_trip()
    {
        using var t = await TempDatabase.CreateAsync();
        var settings = new SqliteSettingsStore(t.Database);
        var store = new SqliteEstimateStore(t.Database);
        var (team, vehicle) = await SeedAsync(settings);
        await store.SaveNewAsync(Snapshot(team, vehicle, "GB-20261118-001"));

        var backup = System.IO.Path.Combine(t.Directory, "b" + DatabaseBackup.BackupExtension);
        DatabaseBackup.BackupTo(t.Path, backup);
        DatabaseBackup.Validate(backup);

        // Change data after the backup.
        await store.SaveNewAsync(Snapshot(team, vehicle, "GB-20261118-002"));
        await settings.SaveTeamAsync(team with { Name = "Changed" });
        Assert.Equal(2, (await store.GetSummariesAsync()).Count);

        var safety = DatabaseBackup.RestoreFrom(backup, t.Path, t.Directory);
        await t.Database.MigrateAsync();
        Assert.True(File.Exists(safety));
        Assert.Single(await store.GetSummariesAsync());
        Assert.Equal("Team B", (await settings.GetTeamsAsync(true)).Single().Name);

        // The safety copy holds the pre-restore state.
        DatabaseBackup.Validate(safety);
    }

    [Fact]
    public async Task Backup_path_with_a_semicolon_is_treated_as_a_file_name()
    {
        using var t = await TempDatabase.CreateAsync();
        var dir = System.IO.Path.Combine(t.Directory, "odd;Mode=Memory;name");
        Directory.CreateDirectory(dir);
        var backup = System.IO.Path.Combine(dir, "b" + DatabaseBackup.BackupExtension);
        DatabaseBackup.BackupTo(t.Path, backup);
        Assert.True(File.Exists(backup));
        DatabaseBackup.Validate(backup);
    }

    [Fact]
    public void Restore_rejects_non_estimator_files()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "grover-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var junk = System.IO.Path.Combine(dir, "junk.estimator-backup");
        File.WriteAllText(junk, "not a database");
        Assert.Throws<InvalidDataException>(() => DatabaseBackup.Validate(junk));
        Directory.Delete(dir, true);
    }
}
