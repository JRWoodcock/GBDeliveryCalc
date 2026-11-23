using GBDeliveryCalc.Core.Model;

namespace GBDeliveryCalc.Tests;

/// <summary>The regression example from the specification (manual example inputs, not verified route results).</summary>
internal static class TestData
{
    public const string Origin = "Grover Brothers, Hattiesburg, MS";
    public const string Destination = "Neshoba Central High School, Philadelphia, MS";

    public static TeamSnapshot TeamA(decimal? loaded = 40m) => new()
    {
        TeamId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Name = "Team B",
        Description = "Alex and Sam, Company Truck",
        LoadedLaborCostPerHour = loaded,
    };

    public static VehicleSnapshot Sierra(decimal? opCost = 0.20m) => new()
    {
        VehicleId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Name = "2028 GMC Sierra",
        FuelType = FuelType.RegularUnleaded,
        EstimatedMpg = 19m,
        OperatingCostPerMileExcludingFuel = opCost,
    };

    public static FuelPrice Fuel349 => new()
    {
        FuelType = FuelType.RegularUnleaded,
        PricePerGallon = 3.49m,
        Source = "Manual entry",
        IsManualOverride = true,
        RetrievedAt = new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.FromHours(-6)),
    };

    /// <summary>Neshoba example: 125 mi / 2 h 14 min each way, 3 h install, $40/hr loaded, 19 MPG at $3.49, $0.20/mi.</summary>
    public static EstimateInput Neshoba(TripType trip = TripType.RoundTrip, decimal? loaded = 40m, decimal? opCost = 0.20m, bool roundUp = false)
    {
        var key = new RouteKey(Origin, Destination, trip);
        return new EstimateInput
        {
            Team = TeamA(loaded),
            Vehicle = Sierra(opCost),
            OriginAddress = Origin,
            DestinationQuery = "Neshoba Central High School",
            DestinationAddress = Destination,
            TripType = trip,
            Route = RouteFigures.ManualPerLeg(trip, 125m, 134, key),
            InstallMinutes = 180,
            RoundLaborHoursUp = roundUp,
            Fuel = Fuel349,
        };
    }
}
