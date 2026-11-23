namespace GBDeliveryCalc.Data.Entities;

/// <summary>Teams table.</summary>
public sealed class TeamEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal? LoadedLaborCostPerHour { get; set; }
    public Guid? DefaultVehicleId { get; set; }
    public bool IsArchived { get; set; }
}

/// <summary>Vehicles table.</summary>
public sealed class VehicleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int FuelType { get; set; }
    public decimal? EstimatedMpg { get; set; }
    public decimal? OperatingCostPerMileExcludingFuel { get; set; }
    public bool IsArchived { get; set; }
}

/// <summary>Single-row table (Id = 1).</summary>
public sealed class CompanySettingsEntity
{
    public int Id { get; set; } = 1;
    public string OriginAddress { get; set; } = "";
    public int DefaultInstallMinutes { get; set; }
    public bool RoundLaborHoursUp { get; set; }
}

/// <summary>
/// Saved estimate. <see cref="SnapshotJson"/> is the source of truth; the other columns are denormalised copies
/// for listing. Edits replace the row in place after copying the previous version to EstimateRevisions.
/// </summary>
public sealed class EstimateEntity
{
    public Guid Id { get; set; }
    public string QuoteNumber { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string CustomerName { get; set; } = "";
    public string Destination { get; set; } = "";
    public string TeamName { get; set; } = "";
    public decimal? TotalCost { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int SnapshotSchemaVersion { get; set; }
    public string SnapshotJson { get; set; } = "";
    public EstimateActualsEntity? Actuals { get; set; }
}

/// <summary>Previous version of an edited estimate.</summary>
public sealed class EstimateRevisionEntity
{
    public int Id { get; set; }
    public Guid EstimateId { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset ReplacedAt { get; set; }
    public string SnapshotJson { get; set; } = "";
}

/// <summary>Actuals live in their own table so recording them can never touch the estimate row.</summary>
public sealed class EstimateActualsEntity
{
    public Guid EstimateId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string ActualsJson { get; set; } = "";
}

/// <summary>Last call to the fuel source per fuel type (drives once-per-day fetching and the hourly retry limit).</summary>
public sealed class FuelFetchStateEntity
{
    public int FuelType { get; set; }
    public string State { get; set; } = Core.Model.UsStates.Default;
    public DateTimeOffset LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>FuelPriceCache table: verified live prices, one row per fetch.</summary>
public sealed class FuelPriceCacheEntity
{
    public int Id { get; set; }
    public int FuelType { get; set; }
    public string State { get; set; } = Core.Model.UsStates.Default;
    public decimal PricePerGallon { get; set; }
    public string Source { get; set; } = "";
    public DateOnly? PublishedDate { get; set; }
    public DateTimeOffset RetrievedAt { get; set; }
    public bool IsLive { get; set; }
}
