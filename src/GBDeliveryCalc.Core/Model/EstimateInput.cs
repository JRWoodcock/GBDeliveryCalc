namespace GBDeliveryCalc.Core.Model;

/// <summary>Everything the calculator needs. Saved verbatim (as a snapshot) with each estimate.</summary>
public sealed record EstimateInput
{
    public TeamSnapshot? Team { get; init; }
    public VehicleSnapshot? Vehicle { get; init; }

    /// <summary>True when staff picked a vehicle other than the team's default for this estimate only.</summary>
    public bool VehicleOverridden { get; init; }

    public string OriginAddress { get; init; } = "";
    public string DestinationQuery { get; init; } = "";

    /// <summary>The full address staff confirmed (or typed).</summary>
    public string DestinationAddress { get; init; } = "";

    /// <summary>Provider that supplied <see cref="DestinationAddress"/>; null when staff typed it or it was
    /// copied into the editable job-address field (Google confirmed this is allowed, 2026-11-19).</summary>
    public string? DestinationProviderName { get; init; }

    /// <summary>False when the address provider's terms do not clearly permit storing the address.</summary>
    public bool DestinationProviderPermitsRetention { get; init; } = true;

    public TripType TripType { get; init; } = TripType.RoundTrip;
    public RouteFigures? Route { get; init; }

    public int InstallMinutes { get; init; } = CompanySettings.FactoryDefaultInstallMinutes;

    /// <summary>Company setting at the time: travel and installation hours are each rounded up to whole hours.</summary>
    public bool RoundLaborHoursUp { get; init; }

    public FuelPrice? Fuel { get; init; }

    /// <summary>Estimate-level override of paid team hours. Null = (costed) drive time + installation time.</summary>
    public decimal? PaidHoursOverride { get; init; }

    public IReadOnlyList<DirectCost> OtherDirectCosts { get; init; } = [];

    public string Notes { get; init; } = "";

    public RouteKey CurrentRouteKey => new(OriginAddress, string.IsNullOrWhiteSpace(DestinationAddress) ? DestinationQuery : DestinationAddress, TripType);
}
