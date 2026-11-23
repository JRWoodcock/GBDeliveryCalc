namespace GBDeliveryCalc.Core.Model;

/// <summary>One driving leg.</summary>
public sealed record RouteLeg
{
    public decimal Miles { get; init; }
    public int DriveMinutes { get; init; }

    public RouteLeg() { }
    public RouteLeg(decimal miles, int driveMinutes)
    {
        Miles = miles;
        DriveMinutes = driveMinutes;
    }
}

/// <summary>Identifies the route a set of figures was produced for. Used to detect stale figures.</summary>
public sealed record RouteKey
{
    public string Origin { get; init; } = "";
    public string Destination { get; init; } = "";
    public TripType TripType { get; init; }

    public RouteKey() { }
    public RouteKey(string origin, string destination, TripType tripType)
    {
        Origin = Normalize(origin);
        Destination = Normalize(destination);
        TripType = tripType;
    }

    private static string Normalize(string? s) =>
        string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}

/// <summary>
/// Trip miles and drive time with their provenance. Totals are always whole-trip figures.
/// </summary>
public sealed record RouteFigures
{
    public TripType TripType { get; init; }
    public RouteFigureSource Source { get; init; }

    /// <summary>For manual figures: whether staff typed per-leg values or trip totals.</summary>
    public ManualEntryBasis? ManualBasis { get; init; }

    public RouteLeg? Outbound { get; init; }
    public RouteLeg? Return { get; init; }

    public decimal TotalMiles { get; init; }
    public int TotalDriveMinutes { get; init; }

    /// <summary>True when the return leg was not looked up and was assumed equal to the outbound leg.</summary>
    public bool ReturnEstimatedByDoubling { get; init; }

    /// <summary>Route the figures were produced or confirmed for.</summary>
    public RouteKey? Key { get; init; }

    /// <summary>True when staff explicitly kept these figures after the origin/destination/trip type changed.</summary>
    public bool ConfirmedAfterRouteChange { get; init; }

    public string? ProviderName { get; init; }

    /// <summary>
    /// Set when a route lookup filled the manual-entry boxes (e.g. "Google Routes API") and staff haven't changed them.
    /// These are saved like manual figures (Google confirmed this is allowed, 2026-11-19) but still go stale when the
    /// origin, destination or trip type changes.
    /// </summary>
    public string? CopiedFromLookupProvider { get; init; }

    /// <summary>False when the provider's terms do not clearly permit storing these figures in a saved estimate.</summary>
    public bool ProviderPermitsRetention { get; init; } = true;

    public static RouteFigures ManualPerLeg(TripType trip, decimal milesPerLeg, int minutesPerLeg, RouteKey? key = null)
    {
        var leg = new RouteLeg(milesPerLeg, minutesPerLeg);
        var legs = trip == TripType.RoundTrip ? 2 : 1;
        return new RouteFigures
        {
            TripType = trip,
            Source = RouteFigureSource.Manual,
            ManualBasis = ManualEntryBasis.PerLeg,
            Outbound = leg,
            Return = trip == TripType.RoundTrip ? leg : null,
            TotalMiles = milesPerLeg * legs,
            TotalDriveMinutes = minutesPerLeg * legs,
            Key = key,
        };
    }

    public static RouteFigures ManualTripTotal(TripType trip, decimal totalMiles, int totalMinutes, RouteKey? key = null) => new()
    {
        TripType = trip,
        Source = RouteFigureSource.Manual,
        ManualBasis = ManualEntryBasis.TripTotal,
        TotalMiles = totalMiles,
        TotalDriveMinutes = totalMinutes,
        Key = key,
    };

    /// <summary>Figures from a routing provider. If a round trip has no looked-up return leg, it is doubled and labelled.</summary>
    public static RouteFigures FromLookup(TripType trip, RouteLeg outbound, RouteLeg? returnLeg, RouteKey key,
        string providerName, bool providerPermitsRetention)
    {
        var doubled = trip == TripType.RoundTrip && returnLeg is null;
        var ret = trip == TripType.RoundTrip ? (returnLeg ?? outbound) : null;
        return new RouteFigures
        {
            TripType = trip,
            Source = RouteFigureSource.Lookup,
            Outbound = outbound,
            Return = ret,
            TotalMiles = outbound.Miles + (ret?.Miles ?? 0m),
            TotalDriveMinutes = outbound.DriveMinutes + (ret?.DriveMinutes ?? 0),
            ReturnEstimatedByDoubling = doubled,
            Key = key,
            ProviderName = providerName,
            ProviderPermitsRetention = providerPermitsRetention,
        };
    }

    /// <summary>Human-readable provenance, shown next to the figures and stored with the estimate.</summary>
    public string ProvenanceLabel
    {
        get
        {
            var how = ManualBasis == ManualEntryBasis.TripTotal ? "trip totals" : "per leg";
            var basis = Source == RouteFigureSource.Lookup
                ? $"Looked up via {ProviderName ?? "routing provider"}"
                : CopiedFromLookupProvider is { } p
                    ? $"From {(p.StartsWith("Google", StringComparison.OrdinalIgnoreCase) ? "Google" : p)} route lookup, copied into manual entry ({how})"
                    : $"Manually entered ({how})";
            if (ReturnEstimatedByDoubling) basis += "; return leg assumed equal to outbound (doubled)";
            if (ConfirmedAfterRouteChange) basis += "; kept by staff after route details changed";
            return basis;
        }
    }
}

/// <summary>Whether route figures match the current origin, destination and trip type.</summary>
public enum RouteValidity
{
    Missing,
    Current,
    /// <summary>Origin, destination or trip type changed since the figures were produced.</summary>
    Stale,
}

/// <summary>Staleness rules for route figures.</summary>
public static class RouteValidation
{
    /// <summary>
    /// Looked-up figures become stale if origin, destination or trip type changes, until refreshed or confirmed.
    /// Manual trip totals become stale if the trip type changes (the total no longer matches the direction).
    /// Manual per-leg figures are recomputed by the caller and stay current.
    /// </summary>
    public static RouteValidity Evaluate(RouteFigures? figures, RouteKey current)
    {
        if (figures is null) return RouteValidity.Missing;
        if (figures.Source == RouteFigureSource.Lookup || figures.CopiedFromLookupProvider is not null)
            return figures.Key == current ? RouteValidity.Current : RouteValidity.Stale;
        if (figures.ManualBasis == ManualEntryBasis.TripTotal && figures.TripType != current.TripType)
            return RouteValidity.Stale;
        return RouteValidity.Current;
    }

    /// <summary>Staff explicitly keeps existing figures for the changed route.</summary>
    public static RouteFigures ConfirmFor(RouteFigures figures, RouteKey current) => figures with
    {
        Key = current,
        TripType = current.TripType,
        ConfirmedAfterRouteChange = true,
    };
}
