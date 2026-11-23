using GBDeliveryCalc.Core.Model;

namespace GBDeliveryCalc.Core.Calculation;

/// <summary>
/// Pure, deterministic cost estimate. Only actual company costs: travel and installation labor at the team's loaded
/// labor cost, fuel, vehicle operating cost and other direct costs. No customer billing, markup or margin.
/// All arithmetic is decimal; hours and rates keep full precision until each line is rounded to cents, and the
/// total is the sum of the rounded lines.
/// </summary>
public static class EstimateCalculator
{
    public static EstimateResult Calculate(EstimateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var validity = RouteValidation.Evaluate(input.Route, input.CurrentRouteKey);
        var miles = input.Route?.TotalMiles ?? 0m;
        var driveMinutes = input.Route?.TotalDriveMinutes ?? 0;
        var installMinutes = Math.Max(0, input.InstallMinutes);
        var issues = new List<string>();
        var notes = new List<string>();
        var lines = new List<CostLine>();

        // ---------- route ----------
        switch (validity)
        {
            case RouteValidity.Missing: issues.Add("Trip miles and drive time"); break;
            case RouteValidity.Stale: issues.Add("Route changed: re-run the route lookup or confirm the existing miles and drive time"); break;
        }
        if (input.Route is { } r)
        {
            if (r.TotalMiles < 0 || r.TotalDriveMinutes < 0) issues.Add("Miles and drive time cannot be negative");
            if (r.ReturnEstimatedByDoubling) notes.Add("Return leg estimated by doubling the outbound trip.");
        }

        // ---------- labor ----------
        var roundUp = input.RoundLaborHoursUp;
        var travelHours = Hours(driveMinutes, roundUp);
        var installHours = Hours(installMinutes, roundUp);
        var paidHours = input.PaidHoursOverride ?? travelHours + installHours;
        var loaded = input.Team?.LoadedLaborCostPerHour;
        var rateText = loaded is { } lr ? Fmt.Money(lr) : "?";
        if (input.Team is null) issues.Add("Team");
        else if (loaded is not > 0m) issues.Add($"Loaded labor cost per hour for {input.Team.Name}");

        decimal? travelCost = null, installCost = null, laborCost = null;
        if (input.PaidHoursOverride is { } ph)
        {
            if (ph < 0) issues.Add("Paid hours override cannot be negative");
            if (loaded is > 0m) laborCost = Money.ToCents(ph * loaded.Value);
            lines.Add(new CostLine
            {
                Kind = CostLineKind.LaborOverride,
                Label = "Labor cost (paid hours override)",
                Detail = $"{Fmt.Number(ph, "0.00")} paid hrs (override; replaces {HoursText(driveMinutes, travelHours, roundUp)} travel + {HoursText(installMinutes, installHours, roundUp)} installation) @ {rateText}/hr",
                Amount = laborCost,
            });
        }
        else
        {
            if (loaded is > 0m)
            {
                travelCost = Money.ToCents(travelHours * loaded.Value);
                installCost = Money.ToCents(installHours * loaded.Value);
                laborCost = travelCost + installCost;
            }
            lines.Add(new CostLine
            {
                Kind = CostLineKind.TravelLabor,
                Label = "Travel labor cost",
                Detail = $"{HoursText(driveMinutes, travelHours, roundUp)} driving @ {rateText}/hr",
                Amount = travelCost,
            });
            lines.Add(new CostLine
            {
                Kind = CostLineKind.InstallationLabor,
                Label = "Installation labor cost",
                Detail = $"{HoursText(installMinutes, installHours, roundUp)} @ {rateText}/hr",
                Amount = installCost,
            });
        }

        // ---------- fuel ----------
        var mpg = input.Vehicle?.EstimatedMpg;
        var fuelPrice = input.Fuel?.PricePerGallon;
        decimal? gallons = mpg is > 0m ? miles / mpg.Value : null;
        decimal? fuelCost = null;
        if (input.Vehicle is null) issues.Add("Vehicle");
        else
        {
            var mismatch = input.Fuel is not null && input.Fuel.FuelType != input.Vehicle.FuelType;
            if (mpg is not > 0m) issues.Add($"Estimated MPG for {input.Vehicle.Name}");
            if (fuelPrice is not > 0m) issues.Add($"{input.Vehicle.FuelType.Label()} price per gallon");
            if (mismatch) issues.Add($"Fuel price is for {input.Fuel!.FuelType.Label()} but {input.Vehicle.Name} uses {input.Vehicle.FuelType.Label()}");
            else if (gallons is { } g && fuelPrice is > 0m) fuelCost = Money.ToCents(g * fuelPrice.Value);
        }
        lines.Add(new CostLine
        {
            Kind = CostLineKind.Fuel,
            Label = "Fuel",
            Detail = gallons is { } gg && fuelPrice is { } fp
                ? $"{Fmt.Miles(miles)} ÷ {Fmt.Number(mpg!.Value)} MPG = {Fmt.Number(gg, "0.00")} gal {input.Vehicle!.FuelType.Label().ToLowerInvariant()} @ {Fmt.Rate(fp)}/gal"
                : "Needs the vehicle's MPG and a fuel price",
            Amount = fuelCost,
        });

        // ---------- vehicle operating cost ----------
        decimal? vehicleCost = null;
        var opRate = input.Vehicle?.OperatingCostPerMileExcludingFuel;
        if (input.Vehicle is not null)
        {
            if (opRate is >= 0m) vehicleCost = Money.ToCents(miles * opRate.Value);
            else issues.Add($"Operating cost per mile for {input.Vehicle.Name}");
        }
        lines.Add(new CostLine
        {
            Kind = CostLineKind.VehicleOperating,
            Label = "Vehicle operating cost",
            Detail = opRate is { } o ? $"{Fmt.Miles(miles)} @ {Fmt.Rate(o)}/mi (excludes fuel)" : "Needs the vehicle's operating cost per mile",
            Amount = vehicleCost,
        });

        // ---------- other direct costs ----------
        var otherDirect = 0m;
        foreach (var c in input.OtherDirectCosts)
        {
            if (c.Amount < 0) issues.Add($"Other direct cost '{c.Description}' cannot be negative");
            var amount = Money.ToCents(c.Amount);
            otherDirect += amount;
            lines.Add(new CostLine
            {
                Kind = CostLineKind.OtherDirect,
                Label = string.IsNullOrWhiteSpace(c.Description) ? "Other direct cost" : c.Description.Trim(),
                Amount = amount,
            });
        }

        var distinctIssues = issues.Distinct().ToList();
        decimal? total = distinctIssues.Count == 0 ? lines.Sum(l => l.Amount ?? 0m) : null;

        return new EstimateResult
        {
            TotalMiles = miles,
            TotalDriveMinutes = driveMinutes,
            InstallMinutes = installMinutes,
            RouteValidity = validity,
            TravelHoursCosted = travelHours,
            InstallHoursCosted = installHours,
            LaborHoursRoundedUp = roundUp,
            PaidHours = paidHours,
            PaidHoursOverridden = input.PaidHoursOverride is not null,
            CostLines = lines,
            TravelLaborCost = travelCost,
            InstallationLaborCost = installCost,
            LaborCost = laborCost,
            FuelCost = fuelCost,
            OtherVehicleCost = vehicleCost,
            OtherDirectCostsTotal = otherDirect,
            TotalDirectCost = total,
            CostIssues = distinctIssues,
            Notes = notes,
        };
    }

    /// <summary>Exact hours, or whole hours rounded up when the company setting is on.</summary>
    public static decimal Hours(int minutes, bool roundUp)
    {
        var h = Money.Hours(Math.Max(0, minutes));
        return roundUp ? Math.Ceiling(h) : h;
    }

    private static string HoursText(int minutes, decimal costed, bool roundUp) =>
        roundUp && costed != Money.Hours(minutes)
            ? $"{Fmt.Duration(minutes)} (rounded up to {Fmt.Number(costed)} h)"
            : Fmt.Duration(minutes);
}
