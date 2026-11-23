using System.Globalization;
using GBDeliveryCalc.Core.Model;
using GBDeliveryCalc.Core.Persistence;
using GBDeliveryCalc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GBDeliveryCalc.Data;

/// <summary>Opens a short-lived DbContext per operation (safe for a desktop app with async UI).</summary>
public sealed class SqliteDatabase(string databasePath)
{
    public string DatabasePath { get; } = databasePath;

    internal EstimatorDbContext Open() => new(EstimatorDbContext.OptionsFor(DatabasePath));

    /// <summary>Creates the database if needed and applies pending schema migrations.</summary>
    public async Task MigrateAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(DatabasePath))!);
        await using var db = Open();
        await db.Database.MigrateAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>Company settings, teams and vehicles in SQLite.</summary>
public sealed class SqliteSettingsStore(SqliteDatabase database) : ISettingsStore
{
    public async Task<CompanySettings> GetCompanySettingsAsync(CancellationToken ct = default)
    {
        await using var db = database.Open();
        var e = await db.CompanySettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct).ConfigureAwait(false);
        return new CompanySettings
        {
            OriginAddress = e.OriginAddress,
            DefaultInstallMinutes = e.DefaultInstallMinutes,
            RoundLaborHoursUp = e.RoundLaborHoursUp,
        };
    }

    public async Task SaveCompanySettingsAsync(CompanySettings settings, CancellationToken ct = default)
    {
        if (settings.DefaultInstallMinutes < 0) throw new ArgumentException("Default installation time cannot be negative.");
        await using var db = database.Open();
        var e = await db.CompanySettings.SingleAsync(x => x.Id == 1, ct).ConfigureAwait(false);
        e.OriginAddress = settings.OriginAddress.Trim();
        e.DefaultInstallMinutes = settings.DefaultInstallMinutes;
        e.RoundLaborHoursUp = settings.RoundLaborHoursUp;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TeamProfile>> GetTeamsAsync(bool includeArchived, CancellationToken ct = default)
    {
        await using var db = database.Open();
        var rows = await db.Teams.AsNoTracking().Where(t => includeArchived || !t.IsArchived).ToListAsync(ct).ConfigureAwait(false);
        return rows.OrderBy(t => t.IsArchived).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).Select(ToModel).ToList();
    }

    public async Task<TeamProfile> SaveTeamAsync(TeamProfile team, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(team.Name)) throw new ArgumentException("Team name is required.");
        if (team.LoadedLaborCostPerHour < 0) throw new ArgumentException("Loaded labor cost cannot be negative.");
        await using var db = database.Open();
        var e = await db.Teams.FindAsync([team.Id], ct).ConfigureAwait(false);
        if (e is null) { e = new TeamEntity { Id = team.Id }; db.Teams.Add(e); }
        e.Name = team.Name.Trim();
        e.Description = team.Description.Trim();
        e.LoadedLaborCostPerHour = team.LoadedLaborCostPerHour;
        e.DefaultVehicleId = team.DefaultVehicleId;
        e.IsArchived = team.IsArchived;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToModel(e);
    }

    public async Task<IReadOnlyList<VehicleProfile>> GetVehiclesAsync(bool includeArchived, CancellationToken ct = default)
    {
        await using var db = database.Open();
        var rows = await db.Vehicles.AsNoTracking().Where(v => includeArchived || !v.IsArchived).ToListAsync(ct).ConfigureAwait(false);
        return rows.OrderBy(v => v.IsArchived).ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).Select(ToModel).ToList();
    }

    public async Task<VehicleProfile> SaveVehicleAsync(VehicleProfile vehicle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vehicle.Name)) throw new ArgumentException("Vehicle name is required.");
        if (vehicle.EstimatedMpg <= 0) throw new ArgumentException("MPG must be greater than zero.");
        if (vehicle.OperatingCostPerMileExcludingFuel < 0) throw new ArgumentException("Operating cost cannot be negative.");
        await using var db = database.Open();
        var e = await db.Vehicles.FindAsync([vehicle.Id], ct).ConfigureAwait(false);
        if (e is null) { e = new VehicleEntity { Id = vehicle.Id }; db.Vehicles.Add(e); }
        e.Name = vehicle.Name.Trim();
        e.FuelType = (int)vehicle.FuelType;
        e.EstimatedMpg = vehicle.EstimatedMpg;
        e.OperatingCostPerMileExcludingFuel = vehicle.OperatingCostPerMileExcludingFuel;
        e.IsArchived = vehicle.IsArchived;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToModel(e);
    }

    private static TeamProfile ToModel(TeamEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        LoadedLaborCostPerHour = e.LoadedLaborCostPerHour,
        DefaultVehicleId = e.DefaultVehicleId,
        IsArchived = e.IsArchived,
    };

    private static VehicleProfile ToModel(VehicleEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        FuelType = (FuelType)e.FuelType,
        EstimatedMpg = e.EstimatedMpg,
        OperatingCostPerMileExcludingFuel = e.OperatingCostPerMileExcludingFuel,
        IsArchived = e.IsArchived,
    };
}

/// <summary>Saved estimates, revisions and actuals in SQLite.</summary>
public sealed class SqliteEstimateStore(SqliteDatabase database) : IEstimateStore
{
    public async Task<EstimateSnapshot> SaveNewAsync(EstimateSnapshot snapshot, CancellationToken ct = default)
    {
        RetentionPolicy.EnsurePersistable(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.QuoteNumber)) throw new ArgumentException("Quote number is required.");
        await using var db = database.Open();
        if (await db.Estimates.AnyAsync(e => e.Id == snapshot.Id, ct).ConfigureAwait(false))
            throw new InvalidOperationException("This estimate is already saved. Edit it from History instead.");

        var row = new EstimateEntity { Id = snapshot.Id, QuoteNumber = snapshot.QuoteNumber, CreatedAt = snapshot.CreatedAt };
        Fill(row, snapshot);
        db.Estimates.Add(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return snapshot;
    }

    public async Task<EstimateSnapshot> UpdateAsync(EstimateSnapshot snapshot, CancellationToken ct = default)
    {
        RetentionPolicy.EnsurePersistable(snapshot);
        await using var db = database.Open();
        var row = await db.Estimates.SingleOrDefaultAsync(e => e.Id == snapshot.Id, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException("Estimate not found. It may have been deleted.");
        var previous = SnapshotJson.Deserialize<EstimateSnapshot>(row.SnapshotJson);
        if (previous.IsLegacy) throw new InvalidOperationException("Estimates saved before the cost-only change are read-only.");

        var now = DateTimeOffset.Now;
        db.EstimateRevisions.Add(new EstimateRevisionEntity
        {
            EstimateId = row.Id,
            Revision = previous.Revision,
            ReplacedAt = now,
            SnapshotJson = row.SnapshotJson,
        });
        // Number and creation time never change on edit.
        var updated = snapshot with
        {
            QuoteNumber = row.QuoteNumber,
            CreatedAt = row.CreatedAt,
            Revision = previous.Revision + 1,
            UpdatedAt = snapshot.UpdatedAt ?? now,
        };
        Fill(row, updated);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = database.Open();
        var row = await db.Estimates.Include(e => e.Actuals).SingleOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (row is null) return false;
        db.EstimateRevisions.RemoveRange(db.EstimateRevisions.Where(r => r.EstimateId == id));
        if (row.Actuals is { } a) db.EstimateActuals.Remove(a);
        db.Estimates.Remove(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<EstimateRevision>> GetRevisionsAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = database.Open();
        var rows = await db.EstimateRevisions.AsNoTracking().Where(r => r.EstimateId == id).ToListAsync(ct).ConfigureAwait(false);
        return rows.OrderBy(r => r.Revision).Select(r => new EstimateRevision
        {
            EstimateId = r.EstimateId,
            Revision = r.Revision,
            ReplacedAt = r.ReplacedAt,
            Snapshot = SnapshotJson.Deserialize<EstimateSnapshot>(r.SnapshotJson),
        }).ToList();
    }

    private static void Fill(EstimateEntity row, EstimateSnapshot s)
    {
        row.CustomerName = s.CustomerName;
        row.Destination = string.IsNullOrWhiteSpace(s.Input.DestinationAddress) ? s.Input.DestinationQuery : s.Input.DestinationAddress;
        row.TeamName = s.Input.Team?.Name ?? "";
        row.TotalCost = s.Result.TotalDirectCost;
        row.UpdatedAt = s.UpdatedAt;
        row.SnapshotSchemaVersion = s.SchemaVersion;
        row.SnapshotJson = SnapshotJson.Serialize(s);
    }

    public async Task<IReadOnlyList<EstimateSummary>> GetSummariesAsync(CancellationToken ct = default)
    {
        await using var db = database.Open();
        var rows = await db.Estimates.AsNoTracking()
            .Select(e => new EstimateSummary
            {
                Id = e.Id,
                QuoteNumber = e.QuoteNumber,
                CreatedAt = e.CreatedAt,
                CustomerName = e.CustomerName,
                Destination = e.Destination,
                TeamName = e.TeamName,
                TotalCost = e.TotalCost,
                HasActuals = e.Actuals != null,
                IsLegacy = e.SnapshotSchemaVersion < 2,
            })
            .ToListAsync(ct).ConfigureAwait(false);
        // SQLite cannot ORDER BY DateTimeOffset server-side; sort in memory.
        return rows.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<EstimateSnapshot?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = database.Open();
        var json = await db.Estimates.AsNoTracking().Where(e => e.Id == id).Select(e => e.SnapshotJson).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return json is null ? null : SnapshotJson.Deserialize<EstimateSnapshot>(json);
    }

    public async Task<string> NextQuoteNumberAsync(DateOnly day, CancellationToken ct = default)
    {
        var prefix = "GB-" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-";
        await using var db = database.Open();
        var existing = await db.Estimates.AsNoTracking().Where(e => e.QuoteNumber.StartsWith(prefix)).Select(e => e.QuoteNumber).ToListAsync(ct).ConfigureAwait(false);
        var max = existing.Select(q => int.TryParse(q[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return prefix + (max + 1).ToString("000", CultureInfo.InvariantCulture);
    }

    public async Task<EstimateActuals?> GetActualsAsync(Guid estimateId, CancellationToken ct = default)
    {
        await using var db = database.Open();
        var json = await db.EstimateActuals.AsNoTracking().Where(a => a.EstimateId == estimateId).Select(a => a.ActualsJson).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return json is null ? null : SnapshotJson.Deserialize<EstimateActuals>(json);
    }

    public async Task SaveActualsAsync(Guid estimateId, EstimateActuals actuals, CancellationToken ct = default)
    {
        await using var db = database.Open();
        if (!await db.Estimates.AnyAsync(e => e.Id == estimateId, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Estimate not found.");
        var row = await db.EstimateActuals.FindAsync([estimateId], ct).ConfigureAwait(false);
        if (row is null) { row = new EstimateActualsEntity { EstimateId = estimateId }; db.EstimateActuals.Add(row); }
        row.UpdatedAt = actuals.UpdatedAt;
        row.ActualsJson = SnapshotJson.Serialize(actuals);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>Fuel prices and fetch attempts in SQLite.</summary>
public sealed class SqliteFuelPriceCache(SqliteDatabase database) : IFuelPriceCache
{
    public async Task<FuelPrice?> GetLatestAsync(FuelType fuelType, string state = UsStates.Default, CancellationToken ct = default)
    {
        await using var db = database.Open();
        // Rows are appended as prices are fetched, so the highest Id is the latest. (SQLite can't order by DateTimeOffset.)
        var e = await db.FuelPriceCache.AsNoTracking().Where(f => f.FuelType == (int)fuelType && f.State == state)
            .OrderByDescending(f => f.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return e is null ? null : new FuelPrice
        {
            FuelType = (FuelType)e.FuelType,
            State = e.State,
            PricePerGallon = e.PricePerGallon,
            Source = e.Source,
            PublishedDate = e.PublishedDate,
            RetrievedAt = e.RetrievedAt,
            IsLive = e.IsLive,
        };
    }

    public async Task SaveAsync(FuelPrice price, CancellationToken ct = default)
    {
        await using var db = database.Open();
        db.FuelPriceCache.Add(new FuelPriceCacheEntity
        {
            FuelType = (int)price.FuelType,
            State = price.State,
            PricePerGallon = price.PricePerGallon,
            Source = price.Source,
            PublishedDate = price.PublishedDate,
            RetrievedAt = price.RetrievedAt,
            IsLive = price.IsLive,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<FuelFetchState?> GetFetchStateAsync(FuelType fuelType, string state = UsStates.Default, CancellationToken ct = default)
    {
        await using var db = database.Open();
        var e = await db.FuelFetchStates.AsNoTracking().SingleOrDefaultAsync(x => x.FuelType == (int)fuelType && x.State == state, ct).ConfigureAwait(false);
        return e is null ? null : new FuelFetchState((FuelType)e.FuelType, e.LastAttemptAt, e.LastError, e.State);
    }

    public async Task SaveFetchStateAsync(FuelFetchState state, CancellationToken ct = default)
    {
        await using var db = database.Open();
        var e = await db.FuelFetchStates.SingleOrDefaultAsync(x => x.FuelType == (int)state.FuelType && x.State == state.State, ct).ConfigureAwait(false);
        if (e is null) db.FuelFetchStates.Add(e = new FuelFetchStateEntity { FuelType = (int)state.FuelType, State = state.State });
        e.LastAttemptAt = state.LastAttemptAt;
        e.LastError = state.LastError;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearFetchStateAsync(CancellationToken ct = default)
    {
        await using var db = database.Open();
        await db.FuelFetchStates.ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }
}
