using GBDeliveryCalc.Core.Model;

namespace GBDeliveryCalc.Core.Integrations;

/// <summary>Whether an integration can be used.</summary>
public enum ProviderStatus
{
    /// <summary>No approved source or no API key: feature is off and labelled in the UI.</summary>
    NotConfigured,
    Available,
}

/// <summary>Name, status and retention rule of an integration, shown in the UI.</summary>
public sealed record ProviderInfo(string Name, ProviderStatus Status, string StatusMessage, bool PermitsRetention);

/// <summary>Raised by an integration with a message that is safe to show to staff.</summary>
public sealed class ProviderUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>One address match from an address search.</summary>
public sealed record PlaceCandidate
{
    public string DisplayName { get; init; } = "";
    public string FormattedAddress { get; init; } = "";
    public string? ProviderPlaceId { get; init; }
}

/// <summary>Address autocomplete / place search (e.g. "Neshoba Central High School").</summary>
public interface IPlaceSearchProvider
{
    ProviderInfo Info { get; }
    Task<PlaceSearchResult> SearchAsync(string query, CancellationToken ct = default);
}

/// <summary>Driving distance and time from a routing provider.</summary>
public sealed record RouteLookupResult
{
    public RouteLeg Outbound { get; init; } = new();

    /// <summary>Null when the provider could not route the return direction (caller doubles and labels it).</summary>
    public RouteLeg? Return { get; init; }

    /// <summary>Underlying data provider (e.g. "Google Routes API"), for labelling.</summary>
    public string ProviderName { get; init; } = "";

    /// <summary>Whether the provider's terms clearly permit storing these figures. Defaults to false (not permitted).</summary>
    public bool PermitsRetention { get; init; }
}

/// <summary>Address matches from an address search.</summary>
public sealed record PlaceSearchResult
{
    public IReadOnlyList<PlaceCandidate> Candidates { get; init; } = [];
    public string ProviderName { get; init; } = "";
    public bool PermitsRetention { get; init; }
}

/// <summary>Driving miles and time between two addresses.</summary>
public interface IRoutingProvider
{
    ProviderInfo Info { get; }

    /// <summary>For round trips, implementations request both directions when the provider supports it.</summary>
    Task<RouteLookupResult> GetRouteAsync(string origin, string destination, TripType tripType, CancellationToken ct = default);
}

/// <summary>Current statewide average fuel price for a fuel type.</summary>
public interface IFuelPriceSource
{
    ProviderInfo Info { get; }

    /// <param name="fuelType">Regular unleaded or diesel.</param>
    /// <param name="state">Two-letter state code, e.g. "MS".</param>
    /// <param name="ct">Cancels the request.</param>
    Task<FuelPrice> GetStateAverageAsync(FuelType fuelType, string state = UsStates.Default, CancellationToken ct = default);
}

/// <summary>Used when no provider is configured. Always fails with a clear reason.</summary>
public sealed class NotConfiguredPlaceSearch(string reason) : IPlaceSearchProvider
{
    public ProviderInfo Info { get; } = new("Address lookup", ProviderStatus.NotConfigured, reason, false);
    public Task<PlaceSearchResult> SearchAsync(string query, CancellationToken ct = default) =>
        throw new ProviderUnavailableException(reason);
}

/// <summary>Used when no routing provider is configured. Always fails with a clear reason.</summary>
public sealed class NotConfiguredRouting(string reason) : IRoutingProvider
{
    public ProviderInfo Info { get; } = new("Route lookup", ProviderStatus.NotConfigured, reason, false);
    public Task<RouteLookupResult> GetRouteAsync(string origin, string destination, TripType tripType, CancellationToken ct = default) =>
        throw new ProviderUnavailableException(reason);
}

/// <summary>Used when no fuel price source is configured. Always fails with a clear reason.</summary>
public sealed class NotConfiguredFuelPriceSource(string reason) : IFuelPriceSource
{
    public ProviderInfo Info { get; } = new("Fuel price", ProviderStatus.NotConfigured, reason, false);
    public Task<FuelPrice> GetStateAverageAsync(FuelType fuelType, string state = UsStates.Default, CancellationToken ct = default) =>
        throw new ProviderUnavailableException(reason);
}
