using GBDeliveryCalc.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GBDeliveryCalc.Tests;

public class MigrationTests
{
    /// <summary>A database from the billing era (before CostOnlyEstimates) upgrades without losing data.</summary>
    [Fact]
    public async Task Billing_era_database_upgrades_to_cost_only()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grover-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "estimator.db");
        try
        {
            await using (var db = new EstimatorDbContext(EstimatorDbContext.OptionsFor(path)))
            {
                await db.GetService<IMigrator>().MigrateAsync("20261118222405_AddFuelFetchState");
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE CompanySettings SET OriginAddress = 'Shop', AllInMileageRatePerMile = '0.725', VehicleRatePlusFuelPerMile = '0.54', DefaultBillingMode = 2 WHERE Id = 1");
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Teams (Id, Name, Description, BillingRatePerHour, LoadedLaborCostPerHour, DefaultVehicleId, IsArchived) VALUES ('020BEF81-E0B6-4286-9040-6311B6B23DF3', 'Alex & Sam', 'Our truck', '55.0', '55.0', NULL, 0)");
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Estimates (Id, QuoteNumber, CreatedAt, CustomerName, Destination, TeamName, QuoteTotal, SnapshotSchemaVersion, SnapshotJson) " +
                    "VALUES ('A1B2C3D4-0000-0000-0000-000000000001', 'GB-20261118-001', '2026-11-18 10:00:00-06:00', 'Old', 'Site', 'Team B', '591.92', 1, {0})",
                    "{\"SchemaVersion\":1,\"Id\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"QuoteNumber\":\"GB-20261118-001\",\"Result\":{\"QuoteTotal\":591.92,\"TotalDirectCost\":394.59,\"CostLines\":[]}}");
            }

            var database = new SqliteDatabase(path);
            await database.MigrateAsync();

            var settings = await new SqliteSettingsStore(database).GetCompanySettingsAsync();
            Assert.Equal("Shop", settings.OriginAddress);
            Assert.False(settings.RoundLaborHoursUp);
            var team = Assert.Single(await new SqliteSettingsStore(database).GetTeamsAsync(true));
            Assert.Equal(55m, team.LoadedLaborCostPerHour);
            var summary = Assert.Single(await new SqliteEstimateStore(database).GetSummariesAsync());
            Assert.Equal(394.59m, summary.TotalCost); // the cost total, not the old quote (revenue) total
            Assert.True(summary.IsLegacy);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
