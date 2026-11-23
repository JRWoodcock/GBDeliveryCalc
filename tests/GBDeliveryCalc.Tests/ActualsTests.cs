using GBDeliveryCalc.Core.Calculation;
using GBDeliveryCalc.Core.Model;

namespace GBDeliveryCalc.Tests;

public class ActualsTests
{
    private static EstimateSnapshot Estimated(decimal? loaded = 40m, decimal? op = 0.20m, bool roundUp = false)
    {
        var input = TestData.Neshoba(loaded: loaded, opCost: op, roundUp: roundUp);
        return new EstimateSnapshot { QuoteNumber = "GB-1", Input = input, Result = EstimateCalculator.Calculate(input) };
    }

    private static ComparisonRow Row(ActualsComparison c, string label) => c.Rows.Single(r => r.Label == label);

    [Fact]
    public void Empty_actuals_use_labelled_assumptions()
    {
        var c = ActualsComparer.Compare(Estimated(), new EstimateActuals());
        Assert.Equal(394.59m, c.ActualTotalCost);
        Assert.Contains(c.Assumptions, a => a.Contains("Actual miles not entered"));
        Assert.Contains(c.Assumptions, a => a.Contains("Fuel cost not entered"));
        Assert.Contains(c.Assumptions, a => a.Contains("Paid hours not entered"));
        Assert.Empty(c.Issues);
        Assert.All(c.Rows, r => Assert.Equal(0m, r.Variance));
    }

    [Fact]
    public void Rows_compare_estimated_and_actual_cost_per_line_and_total()
    {
        var c = ActualsComparer.Compare(Estimated(), new EstimateActuals
        {
            TripMiles = 262m,
            PaidTeamHours = 9m,
            FuelCost = 52.10m,
            OtherDirectCosts = [new DirectCost { Description = "Parking", Amount = 15m }],
        });
        Assert.Equal(new[] { "Labor cost", "Fuel", "Vehicle operating cost", "Other direct costs", "Total cost" }, c.Rows.Select(r => r.Label));
        Assert.Equal(298.67m, Row(c, "Labor cost").Estimated);
        Assert.Equal(360.00m, Row(c, "Labor cost").Actual);
        Assert.Equal(61.33m, Row(c, "Labor cost").Variance);
        Assert.Equal(52.10m, Row(c, "Fuel").Actual);
        Assert.Equal(52.40m, Row(c, "Vehicle operating cost").Actual); // 262 × 0.20
        Assert.Equal(15m, Row(c, "Other direct costs").Actual);
        Assert.Equal(394.59m, Row(c, "Total cost").Estimated);
        Assert.Equal(479.50m, Row(c, "Total cost").Actual);
        Assert.Equal(84.91m, Row(c, "Total cost").Variance);
    }

    [Fact]
    public void Labor_override_wins_and_time_based_paid_hours_are_labelled()
    {
        var c = ActualsComparer.Compare(Estimated(), new EstimateActuals { LaborCostOverride = 410m });
        Assert.Equal(410m, c.ActualLaborCost);

        var timed = ActualsComparer.Compare(Estimated(), new EstimateActuals { DriveMinutes = 300, InstallMinutes = 200 });
        Assert.Equal(333.33m, timed.ActualLaborCost); // 500 min × 40/60
        Assert.Contains(timed.Assumptions, a => a.Contains("actual drive + installation time"));
    }

    [Fact]
    public void Actual_time_is_rounded_up_when_the_estimate_was()
    {
        var c = ActualsComparer.Compare(Estimated(roundUp: true), new EstimateActuals { DriveMinutes = 241, InstallMinutes = 150 });
        Assert.Equal(320.00m, c.ActualLaborCost); // 5 h + 3 h at $40
        Assert.Contains(c.Assumptions, a => a.Contains("rounded up"));
    }

    [Fact]
    public void Missing_loaded_rate_needs_a_labor_cost()
    {
        var c = ActualsComparer.Compare(Estimated(loaded: null), new EstimateActuals());
        Assert.Null(c.ActualLaborCost);
        Assert.Null(c.ActualTotalCost);
        Assert.Contains(c.Issues, i => i.Contains("Actual labor cost"));
    }
}
