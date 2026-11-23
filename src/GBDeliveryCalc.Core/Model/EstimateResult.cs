namespace GBDeliveryCalc.Core.Model;

/// <summary>What a cost line represents.</summary>
public enum CostLineKind
{
    /// <summary>Lines saved before cost lines had a kind (billing-era estimates).</summary>
    Unspecified = 0,
    TravelLabor,
    InstallationLabor,

    /// <summary>Single labor line used when paid hours are overridden.</summary>
    LaborOverride,
    Fuel,
    VehicleOperating,
    OtherDirect,
}

/// <summary>An internal cost line, rounded to cents. Amount is null when an input is missing.</summary>
public sealed record CostLine
{
    public CostLineKind Kind { get; init; }
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public decimal? Amount { get; init; }
}

/// <summary>
/// The cost estimate: actual company costs only (labor at the loaded rate, fuel, vehicle operating cost and other
/// direct costs). There is no customer price, markup or margin; the salesman sets the markup from this total.
/// </summary>
public sealed record EstimateResult
{
    public decimal TotalMiles { get; init; }
    public int TotalDriveMinutes { get; init; }
    public int InstallMinutes { get; init; }
    public RouteValidity RouteValidity { get; init; }

    /// <summary>Hours used for costing (after the optional round-up).</summary>
    public decimal TravelHoursCosted { get; init; }
    public decimal InstallHoursCosted { get; init; }
    public bool LaborHoursRoundedUp { get; init; }

    /// <summary>Hours the team is paid for: costed travel + installation hours, or the override.</summary>
    public decimal PaidHours { get; init; }
    public bool PaidHoursOverridden { get; init; }

    public IReadOnlyList<CostLine> CostLines { get; init; } = [];
    public decimal? TravelLaborCost { get; init; }
    public decimal? InstallationLaborCost { get; init; }

    /// <summary>Total labor cost (travel + installation, or paid-hours override × loaded rate).</summary>
    public decimal? LaborCost { get; init; }
    public decimal? FuelCost { get; init; }

    /// <summary>Vehicle operating cost (miles × cost per mile excluding fuel).</summary>
    public decimal? OtherVehicleCost { get; init; }
    public decimal OtherDirectCostsTotal { get; init; }

    /// <summary>Total estimated cost: the sum of the rounded lines. Null when any required input is missing.</summary>
    public decimal? TotalDirectCost { get; init; }

    /// <summary>Missing or invalid inputs. Empty = the total is complete.</summary>
    public IReadOnlyList<string> CostIssues { get; init; } = [];
    public bool IsCostComplete => CostIssues.Count == 0;

    /// <summary>Notes such as "return leg estimated by doubling".</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
