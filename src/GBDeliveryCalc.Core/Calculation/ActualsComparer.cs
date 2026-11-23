using GBDeliveryCalc.Core.Model;

namespace GBDeliveryCalc.Core.Calculation;

/// <summary>One line of the estimated vs actual comparison.</summary>
public sealed record ComparisonRow
{
    public string Label { get; init; } = "";
    public decimal? Estimated { get; init; }
    public decimal? Actual { get; init; }
    public decimal? Variance => Estimated is { } e && Actual is { } a ? a - e : null;
}

/// <summary>Actual costs derived from recorded actuals, with the assumptions used.</summary>
public sealed record ActualsComparison
{
    public decimal? ActualLaborCost { get; init; }
    public decimal? ActualFuelCost { get; init; }
    public decimal? ActualOtherVehicleCost { get; init; }
    public decimal ActualOtherDirectCosts { get; init; }
    public decimal? ActualTotalCost { get; init; }

    /// <summary>Estimated vs actual cost per line, then the total.</summary>
    public IReadOnlyList<ComparisonRow> Rows { get; init; } = [];

    /// <summary>Places where an actual figure was derived from an estimate or a saved rate.</summary>
    public IReadOnlyList<string> Assumptions { get; init; } = [];

    public IReadOnlyList<string> Issues { get; init; } = [];
}

/// <summary>Compares a saved cost estimate with recorded actual costs. Uses only the snapshot's frozen rates.</summary>
public static class ActualsComparer
{
    public static ActualsComparison Compare(EstimateSnapshot estimate, EstimateActuals actuals)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentNullException.ThrowIfNull(actuals);

        var input = estimate.Input;
        var est = estimate.Result;
        var assumptions = new List<string>();
        var issues = new List<string>();

        var miles = actuals.TripMiles ?? est.TotalMiles;
        if (actuals.TripMiles is null) assumptions.Add($"Actual miles not entered; using estimated {Fmt.Miles(est.TotalMiles)}.");

        // Labor
        decimal? labor = null;
        if (actuals.LaborCostOverride is { } lo) labor = Money.ToCents(lo);
        else
        {
            decimal paidHours;
            if (actuals.PaidTeamHours is { } ph) paidHours = ph;
            else if (actuals.DriveMinutes is { } dm && actuals.InstallMinutes is { } im)
            {
                paidHours = EstimateCalculator.Hours(dm, input.RoundLaborHoursUp) + EstimateCalculator.Hours(im, input.RoundLaborHoursUp);
                assumptions.Add("Paid hours not entered; using actual drive + installation time" +
                                (input.RoundLaborHoursUp ? ", each rounded up to whole hours as on the estimate." : "."));
            }
            else
            {
                paidHours = est.PaidHours;
                assumptions.Add($"Paid hours not entered; using estimated {Fmt.Number(est.PaidHours, "0.00")} hrs.");
            }

            if (input.Team?.LoadedLaborCostPerHour is { } rate and > 0m)
            {
                labor = Money.ToCents(paidHours * rate);
                assumptions.Add($"Labor cost = paid hours × loaded rate saved with the estimate ({Fmt.Money(rate)}/hr).");
            }
            else issues.Add("Actual labor cost (no loaded labor rate was saved with this estimate; enter a labor cost)");
        }

        // Fuel
        decimal? fuel = null;
        if (actuals.FuelCost is { } fc) fuel = Money.ToCents(fc);
        else if (input.Vehicle?.EstimatedMpg is { } mpg and > 0m && input.Fuel?.PricePerGallon is { } price and > 0m)
        {
            fuel = Money.ToCents(miles / mpg * price);
            assumptions.Add($"Fuel cost not entered; estimated from miles ÷ {Fmt.Number(mpg)} MPG × {Fmt.Rate(price)}/gal saved with the estimate.");
        }
        else issues.Add("Actual fuel cost");

        // Vehicle operating cost (always derived from the saved per-mile rate)
        decimal? vehicle = null;
        if (input.Vehicle?.OperatingCostPerMileExcludingFuel is { } op and >= 0m)
        {
            vehicle = Money.ToCents(miles * op);
            assumptions.Add($"Vehicle operating cost = miles × saved operating rate ({Fmt.Rate(op)}/mi).");
        }
        else issues.Add("Vehicle operating cost (no operating cost per mile was saved with this estimate)");

        // Other direct
        decimal otherDirect;
        if (actuals.OtherDirectCosts is { } list) otherDirect = list.Sum(c => Money.ToCents(c.Amount));
        else
        {
            otherDirect = est.OtherDirectCostsTotal;
            if (otherDirect != 0m) assumptions.Add("Other direct costs not entered; using the estimated amounts.");
        }

        decimal? total = labor is { } l && fuel is { } f && vehicle is { } v ? l + f + v + otherDirect : null;

        var rows = new List<ComparisonRow>
        {
            new() { Label = "Labor cost", Estimated = est.LaborCost, Actual = labor },
            new() { Label = "Fuel", Estimated = est.FuelCost, Actual = fuel },
            new() { Label = "Vehicle operating cost", Estimated = est.OtherVehicleCost, Actual = vehicle },
            new() { Label = "Other direct costs", Estimated = est.OtherDirectCostsTotal, Actual = otherDirect },
            new() { Label = "Total cost", Estimated = est.TotalDirectCost, Actual = total },
        };

        return new ActualsComparison
        {
            ActualLaborCost = labor,
            ActualFuelCost = fuel,
            ActualOtherVehicleCost = vehicle,
            ActualOtherDirectCosts = otherDirect,
            ActualTotalCost = total,
            Rows = rows,
            Assumptions = assumptions,
            Issues = issues,
        };
    }
}
