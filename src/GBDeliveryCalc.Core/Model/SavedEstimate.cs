using System.Text.Json.Serialization;

namespace GBDeliveryCalc.Core.Model;

/// <summary>
/// A saved cost estimate: the exact inputs (including frozen team, vehicle, rates and fuel price), the computed
/// result and the text as copied. History is always rendered from this snapshot, never recomputed from current
/// settings. Editing saves a new version in place (same number) and keeps the previous one as a revision.
/// </summary>
public sealed record EstimateSnapshot
{
    /// <summary>2 = cost-only estimates (2026-11-19). 1 = billing-era quotes, shown read-only.</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string QuoteNumber { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Set when the estimate was edited after it was first saved.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>1 for the first save, +1 for each edit.</summary>
    public int Revision { get; init; } = 1;

    public string CustomerName { get; init; } = "";
    public EstimateInput Input { get; init; } = new();
    public EstimateResult Result { get; init; } = new();

    /// <summary>The plain-text cost estimate as copied ("Copy cost estimate").</summary>
    public string EstimateText { get; init; } = "";

    /// <summary>Customer quote text of billing-era (schema 1) estimates. Kept so they still display.</summary>
    public string CustomerQuoteText { get; init; } = "";

    /// <summary>Billing-era estimates are shown read-only with their stored lines; they can't be edited.</summary>
    [JsonIgnore] public bool IsLegacy => SchemaVersion < 2;

    [JsonIgnore] public string DisplayText => !string.IsNullOrWhiteSpace(EstimateText) ? EstimateText : CustomerQuoteText;
}

/// <summary>A previous version of an edited estimate.</summary>
public sealed record EstimateRevision
{
    public Guid EstimateId { get; init; }
    public int Revision { get; init; }
    public DateTimeOffset ReplacedAt { get; init; }
    public EstimateSnapshot Snapshot { get; init; } = new();
}

/// <summary>Lightweight row for the history list.</summary>
public sealed record EstimateSummary
{
    public Guid Id { get; init; }
    public string QuoteNumber { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public string CustomerName { get; init; } = "";
    public string Destination { get; init; } = "";
    public string TeamName { get; init; } = "";

    /// <summary>Total estimated cost (for billing-era estimates, the cost total stored with them).</summary>
    public decimal? TotalCost { get; init; }
    public bool HasActuals { get; init; }
    public bool IsLegacy { get; init; }
}

/// <summary>What actually happened on the job. Stored separately; never modifies the estimate.</summary>
public sealed record EstimateActuals
{
    public decimal? TripMiles { get; init; }
    public int? DriveMinutes { get; init; }
    public int? InstallMinutes { get; init; }
    public decimal? PaidTeamHours { get; init; }

    /// <summary>When set, used as the actual labor cost instead of hours × loaded rate.</summary>
    public decimal? LaborCostOverride { get; init; }

    public decimal? FuelCost { get; init; }

    /// <summary>Null = not recorded (estimate is assumed). Empty list = recorded as none.</summary>
    public IReadOnlyList<DirectCost>? OtherDirectCosts { get; init; }

    public string Notes { get; init; } = "";
    public DateTimeOffset UpdatedAt { get; init; }
}
