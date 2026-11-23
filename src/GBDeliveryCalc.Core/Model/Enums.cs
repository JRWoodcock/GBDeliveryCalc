namespace GBDeliveryCalc.Core.Model;

/// <summary>Direction of travel for an estimate.</summary>
public enum TripType
{
    RoundTrip = 0,
    OneWay = 1,
}

/// <summary>Fuel a vehicle uses; prices are fetched and applied per fuel type.</summary>
public enum FuelType
{
    RegularUnleaded = 0,
    Diesel = 1,
}

/// <summary>Where the trip miles and drive time came from.</summary>
public enum RouteFigureSource
{
    Manual = 0,
    Lookup = 1,
}

/// <summary>Whether manually entered figures describe one leg or the whole trip.</summary>
public enum ManualEntryBasis
{
    PerLeg = 0,
    TripTotal = 1,
}

/// <summary>Display labels for enums.</summary>
public static class EnumLabels
{
    public static string Label(this FuelType fuel) => fuel switch
    {
        FuelType.RegularUnleaded => "Regular unleaded",
        FuelType.Diesel => "Diesel",
        _ => fuel.ToString(),
    };

    public static string Label(this TripType trip) => trip switch
    {
        TripType.RoundTrip => "Round trip",
        TripType.OneWay => "One way",
        _ => trip.ToString(),
    };
}
