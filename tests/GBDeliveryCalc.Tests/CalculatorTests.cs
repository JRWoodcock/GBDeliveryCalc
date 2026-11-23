using GBDeliveryCalc.Core.Calculation;
using GBDeliveryCalc.Core.Model;
using GBDeliveryCalc.Core.Quotes;

namespace GBDeliveryCalc.Tests;

/// <summary>Cost-only estimates. Regression example: Neshoba, $40/hr loaded, 19 MPG at $3.49, $0.20/mi.</summary>
public class CalculatorTests
{
    private static decimal? Line(EstimateResult r, CostLineKind kind) => r.CostLines.Single(l => l.Kind == kind).Amount;

    [Fact]
    public void Regression_Neshoba_round_trip_costs()
    {
        var r = EstimateCalculator.Calculate(TestData.Neshoba());
        Assert.Empty(r.CostIssues);
        Assert.Equal(250m, r.TotalMiles);
        Assert.Equal(268, r.TotalDriveMinutes);
        Assert.Equal(178.67m, Line(r, CostLineKind.TravelLabor));       // 268 min × $40/hr = 178.666…
        Assert.Equal(120.00m, Line(r, CostLineKind.InstallationLabor)); // 180 min × $40/hr
        Assert.Equal(45.92m, Line(r, CostLineKind.Fuel));               // 250 ÷ 19 × 3.49 = 45.921…
        Assert.Equal(50.00m, Line(r, CostLineKind.VehicleOperating));   // 250 × 0.20
        Assert.Equal(394.59m, r.TotalDirectCost);
        Assert.Equal(298.67m, r.LaborCost);
        Assert.Equal(7.4667m, Math.Round(r.PaidHours, 4));
        Assert.Contains("4 h 28 min driving @ $40.00/hr", r.CostLines[0].Detail);
        Assert.Equal(4, r.CostLines.Count);
    }

    [Fact]
    public void Round_up_setting_costs_whole_hours_and_says_so()
    {
        var r = EstimateCalculator.Calculate(TestData.Neshoba(roundUp: true));
        Assert.Equal(5m, r.TravelHoursCosted);
        Assert.Equal(3m, r.InstallHoursCosted);
        Assert.Equal(200.00m, Line(r, CostLineKind.TravelLabor));
        Assert.Equal(120.00m, Line(r, CostLineKind.InstallationLabor));
        Assert.Equal(415.92m, r.TotalDirectCost); // 200 + 120 + 45.92 + 50
        Assert.Equal(8m, r.PaidHours);
        Assert.True(r.LaborHoursRoundedUp);
        Assert.Contains("4 h 28 min (rounded up to 5 h) driving", r.CostLines[0].Detail);
        Assert.DoesNotContain("rounded", r.CostLines[1].Detail); // 3 h is already whole
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(60, 1)]
    [InlineData(61, 2)]
    [InlineData(268, 5)]
    public void Round_up_is_per_category(int minutes, int hours) => Assert.Equal(hours, EstimateCalculator.Hours(minutes, roundUp: true));

    [Fact]
    public void One_way_halves_travel_but_not_installation()
    {
        var r = EstimateCalculator.Calculate(TestData.Neshoba(TripType.OneWay));
        Assert.Equal(125m, r.TotalMiles);
        Assert.Equal(89.33m, Line(r, CostLineKind.TravelLabor)); // 134 min × 40/60
        Assert.Equal(120.00m, Line(r, CostLineKind.InstallationLabor));
        Assert.Equal(22.96m, Line(r, CostLineKind.Fuel));
        Assert.Equal(25.00m, Line(r, CostLineKind.VehicleOperating));
        Assert.Equal(257.29m, r.TotalDirectCost);
    }

    [Fact]
    public void Manual_trip_totals_match_per_leg_entry()
    {
        var i = TestData.Neshoba();
        var totals = i with { Route = RouteFigures.ManualTripTotal(TripType.RoundTrip, 250m, 268, i.CurrentRouteKey) };
        Assert.Equal(394.59m, EstimateCalculator.Calculate(totals).TotalDirectCost);
    }

    [Fact]
    public void Paid_hours_override_replaces_travel_and_installation_lines()
    {
        var i = TestData.Neshoba() with { PaidHoursOverride = 9m };
        var r = EstimateCalculator.Calculate(i);
        Assert.True(r.PaidHoursOverridden);
        Assert.DoesNotContain(r.CostLines, l => l.Kind is CostLineKind.TravelLabor or CostLineKind.InstallationLabor);
        var labor = r.CostLines.Single(l => l.Kind == CostLineKind.LaborOverride);
        Assert.Equal(360.00m, labor.Amount);
        Assert.Contains("override", labor.Detail);
        Assert.Contains("4 h 28 min travel + 3 h 00 min installation", labor.Detail);
        Assert.Equal(455.92m, r.TotalDirectCost); // 360 + 45.92 + 50
    }

    [Fact]
    public void Other_direct_costs_are_lines_and_add_to_total()
    {
        var i = TestData.Neshoba() with { OtherDirectCosts = [new DirectCost { Description = "Parking", Amount = 12.505m }, new DirectCost { Description = " ", Amount = 7m }] };
        var r = EstimateCalculator.Calculate(i);
        Assert.Equal(12.51m, r.CostLines.Single(l => l.Label == "Parking").Amount);
        Assert.Contains(r.CostLines, l => l.Label == "Other direct cost" && l.Amount == 7m);
        Assert.Equal(19.51m, r.OtherDirectCostsTotal);
        Assert.Equal(414.10m, r.TotalDirectCost);
    }

    [Fact]
    public void Lookup_without_return_leg_is_doubled_and_noted()
    {
        var i = TestData.Neshoba();
        var lookup = RouteFigures.FromLookup(TripType.RoundTrip, new RouteLeg(125m, 134), null, i.CurrentRouteKey, "Google Routes API", true);
        var r = EstimateCalculator.Calculate(i with { Route = lookup });
        Assert.Equal(250m, r.TotalMiles);
        Assert.Contains(r.Notes, n => n.Contains("doubling"));
    }

    [Fact]
    public void Copied_lookup_figures_go_stale_when_destination_changes_until_confirmed()
    {
        var i = TestData.Neshoba();
        var copied = RouteFigures.ManualPerLeg(TripType.RoundTrip, 125m, 134, i.CurrentRouteKey) with { CopiedFromLookupProvider = "Google Routes API" };
        Assert.Equal(RouteValidity.Current, EstimateCalculator.Calculate(i with { Route = copied }).RouteValidity);
        Assert.Contains("From Google route lookup, copied into manual entry (per leg)", copied.ProvenanceLabel);

        var moved = i with { DestinationAddress = "Somewhere else, MS", Route = copied };
        var r = EstimateCalculator.Calculate(moved);
        Assert.Equal(RouteValidity.Stale, r.RouteValidity);
        Assert.Null(r.TotalDirectCost);
        var confirmed = RouteValidation.ConfirmFor(copied, moved.CurrentRouteKey);
        Assert.Equal(394.59m, EstimateCalculator.Calculate(moved with { Route = confirmed }).TotalDirectCost);
    }

    [Fact]
    public void Plain_manual_per_leg_figures_never_go_stale()
    {
        var i = TestData.Neshoba();
        Assert.Equal(RouteValidity.Current, EstimateCalculator.Calculate(i with { DestinationAddress = "Elsewhere" }).RouteValidity);
    }

    [Fact]
    public void Manual_trip_totals_go_stale_when_trip_type_changes()
    {
        var i = TestData.Neshoba();
        var totals = RouteFigures.ManualTripTotal(TripType.RoundTrip, 250m, 268, i.CurrentRouteKey);
        var r = EstimateCalculator.Calculate(i with { TripType = TripType.OneWay, Route = totals });
        Assert.Equal(RouteValidity.Stale, r.RouteValidity);
    }

    [Fact]
    public void Missing_route_flags_the_total_incomplete()
    {
        var r = EstimateCalculator.Calculate(TestData.Neshoba() with { Route = null });
        Assert.Null(r.TotalDirectCost);
        Assert.Contains("Trip miles and drive time", r.CostIssues);
    }

    [Fact]
    public void Missing_loaded_cost_leaves_labor_lines_blank_and_total_incomplete()
    {
        var r = EstimateCalculator.Calculate(TestData.Neshoba(loaded: null));
        Assert.Null(Line(r, CostLineKind.TravelLabor));
        Assert.Null(Line(r, CostLineKind.InstallationLabor));
        Assert.Equal(45.92m, Line(r, CostLineKind.Fuel)); // other lines still computed
        Assert.Null(r.TotalDirectCost);
        Assert.Contains("Loaded labor cost per hour for Team B", r.CostIssues);
    }

    [Fact]
    public void Missing_vehicle_inputs_are_each_listed()
    {
        var i = TestData.Neshoba() with { Vehicle = TestData.Sierra(opCost: null) with { EstimatedMpg = null }, Fuel = null };
        var r = EstimateCalculator.Calculate(i);
        Assert.Contains("Estimated MPG for 2028 GMC Sierra", r.CostIssues);
        Assert.Contains("Regular unleaded price per gallon", r.CostIssues);
        Assert.Contains("Operating cost per mile for 2028 GMC Sierra", r.CostIssues);
        Assert.Null(r.TotalDirectCost);

        var noVehicle = EstimateCalculator.Calculate(TestData.Neshoba() with { Vehicle = null });
        Assert.Contains("Vehicle", noVehicle.CostIssues);
        var noTeam = EstimateCalculator.Calculate(TestData.Neshoba() with { Team = null });
        Assert.Contains("Team", noTeam.CostIssues);
    }

    [Fact]
    public void Zero_operating_cost_is_valid()
    {
        var r = EstimateCalculator.Calculate(TestData.Neshoba(opCost: 0m));
        Assert.Equal(0m, Line(r, CostLineKind.VehicleOperating));
        Assert.Equal(344.59m, r.TotalDirectCost);
    }

    [Fact]
    public void Fuel_type_mismatch_is_reported()
    {
        var i = TestData.Neshoba() with { Fuel = TestData.Fuel349 with { FuelType = FuelType.Diesel } };
        var r = EstimateCalculator.Calculate(i);
        Assert.Contains(r.CostIssues, x => x.Contains("Fuel price is for Diesel"));
        Assert.Null(Line(r, CostLineKind.Fuel));
    }

    [Fact]
    public void Rounding_is_half_away_from_zero_per_line()
    {
        Assert.Equal(0.13m, Money.ToCents(0.125m));
        Assert.Equal(-0.13m, Money.ToCents(-0.125m));
        Assert.Equal(178.67m, Money.ToCents(268m / 60m * 40m));
    }

    [Fact]
    public void Lines_always_sum_to_the_total()
    {
        var rnd = new Random(1234);
        for (var n = 0; n < 1500; n++)
        {
            var i = TestData.Neshoba(loaded: Math.Round((decimal)rnd.NextDouble() * 90m + 10m, 2), opCost: Math.Round((decimal)rnd.NextDouble(), 3),
                roundUp: rnd.Next(2) == 0) with
            {
                InstallMinutes = rnd.Next(0, 600),
                Route = RouteFigures.ManualPerLeg(TripType.RoundTrip, Math.Round((decimal)rnd.NextDouble() * 300m, 1), rnd.Next(1, 400), TestData.Neshoba().CurrentRouteKey),
                Fuel = TestData.Fuel349 with { PricePerGallon = Math.Round((decimal)rnd.NextDouble() * 4m + 2m, 3) },
                OtherDirectCosts = rnd.Next(3) == 0 ? [new DirectCost { Description = "x", Amount = Math.Round((decimal)rnd.NextDouble() * 50m, 3) }] : [],
            };
            var r = EstimateCalculator.Calculate(i);
            Assert.Equal(r.CostLines.Sum(l => l.Amount!.Value), r.TotalDirectCost);
            Assert.All(r.CostLines, l => Assert.Equal(Money.ToCents(l.Amount!.Value), l.Amount));
        }
    }

    [Fact]
    public void Copied_cost_estimate_names_team_and_vehicle_but_not_the_description()
    {
        var i = TestData.Neshoba();
        var r = EstimateCalculator.Calculate(i);
        var text = CostEstimateFormatter.Format(i, r, "GB-20261118-001", new DateOnly(2026, 11, 18), "Neshoba County Schools");
        Assert.Contains("Cost Estimate", text);
        Assert.Contains("Estimate #: GB-20261118-001", text);
        Assert.Contains("Job site: Neshoba Central High School, Philadelphia, MS", text);
        Assert.Contains("Trip: Round trip, 250.0 mi, 4 h 28 min driving", text);
        Assert.Contains("Team: Team B", text);
        Assert.Contains("Vehicle: 2028 GMC Sierra", text);
        Assert.DoesNotContain("Alex", text);
        foreach (var amount in new[] { "$178.67", "$120.00", "$45.92", "$50.00", "$394.59" }) Assert.Contains(amount, text);
        Assert.Contains("Total estimated cost", text);
        Assert.DoesNotContain("margin", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profit", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formatter_refuses_incomplete_estimate()
    {
        var i = TestData.Neshoba(loaded: null);
        Assert.Throws<InvalidOperationException>(() => CostEstimateFormatter.Format(i, EstimateCalculator.Calculate(i)));
    }
}
