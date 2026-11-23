using GBDeliveryCalc.Core.Model;

namespace GBDeliveryCalc.Core.Persistence;

/// <summary>
/// Persistence boundary for settings. v1 implementation: local SQLite. A shared company backend can
/// implement the same interfaces when several estimators need one history.
/// </summary>
public interface ISettingsStore
{
    Task<CompanySettings> GetCompanySettingsAsync(CancellationToken ct = default);
    Task SaveCompanySettingsAsync(CompanySettings settings, CancellationToken ct = default);

    Task<IReadOnlyList<TeamProfile>> GetTeamsAsync(bool includeArchived, CancellationToken ct = default);
    Task<TeamProfile> SaveTeamAsync(TeamProfile team, CancellationToken ct = default);

    Task<IReadOnlyList<VehicleProfile>> GetVehiclesAsync(bool includeArchived, CancellationToken ct = default);
    Task<VehicleProfile> SaveVehicleAsync(VehicleProfile vehicle, CancellationToken ct = default);
}

/// <summary>Saved estimates, their revisions and actuals.</summary>
public interface IEstimateStore
{
    /// <summary>Saves a new estimate. Saving an id that already exists throws; use <see cref="UpdateAsync"/>.</summary>
    Task<EstimateSnapshot> SaveNewAsync(EstimateSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// Replaces an existing estimate in place (same id and number). The previous version is kept in the revisions
    /// table first. Returns the stored snapshot (revision + 1, UpdatedAt set). Billing-era estimates can't be edited.
    /// </summary>
    Task<EstimateSnapshot> UpdateAsync(EstimateSnapshot snapshot, CancellationToken ct = default);

    /// <summary>Deletes an estimate with its actuals and revisions. Returns false if it didn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<EstimateRevision>> GetRevisionsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<EstimateSummary>> GetSummariesAsync(CancellationToken ct = default);
    Task<EstimateSnapshot?> GetAsync(Guid id, CancellationToken ct = default);
    Task<string> NextQuoteNumberAsync(DateOnly day, CancellationToken ct = default);

    Task<EstimateActuals?> GetActualsAsync(Guid estimateId, CancellationToken ct = default);
    Task SaveActualsAsync(Guid estimateId, EstimateActuals actuals, CancellationToken ct = default);
}

/// <summary>When the fuel source was last called for a fuel type and state, and the error if that call failed.</summary>
public sealed record FuelFetchState(FuelType FuelType, DateTimeOffset LastAttemptAt, string? LastError, string State = UsStates.Default);

/// <summary>Fetched fuel prices and fetch attempts, used to limit calls to the fuel source.</summary>
public interface IFuelPriceCache
{
    /// <summary>The latest price saved for this fuel type and state.</summary>
    Task<FuelPrice?> GetLatestAsync(FuelType fuelType, string state = UsStates.Default, CancellationToken ct = default);
    Task SaveAsync(FuelPrice price, CancellationToken ct = default);

    /// <summary>Last call to the source for this fuel type (persisted, so a restart doesn't refetch or retry early).</summary>
    Task<FuelFetchState?> GetFetchStateAsync(FuelType fuelType, string state = UsStates.Default, CancellationToken ct = default);
    Task SaveFetchStateAsync(FuelFetchState state, CancellationToken ct = default);

    /// <summary>Forgets failed attempts (e.g. after the API key changes) so the next request may call the source.</summary>
    Task ClearFetchStateAsync(CancellationToken ct = default);
}

/// <summary>Raised when an estimate holds provider data whose terms don't allow storing it.</summary>
public sealed class RetentionNotPermittedException(string message) : InvalidOperationException(message);

/// <summary>Checks applied by every estimate store before writing.</summary>
public static class RetentionPolicy
{
    /// <summary>
    /// Throws when a snapshot holds provider data still marked as a provider lookup whose terms do not permit storage.
    /// Google confirmed (2026-11-19) that looked-up miles, drive time and addresses copied into the manual-entry
    /// fields may be saved; the app now always copies them there, so Google lookups no longer trigger this.
    /// Called by every <see cref="IEstimateStore"/> implementation before writing.
    /// </summary>
    public static void EnsurePersistable(EstimateSnapshot snapshot)
    {
        if (snapshot.Input.Route is { Source: RouteFigureSource.Lookup, ProviderPermitsRetention: false } r)
            throw new RetentionNotPermittedException(
                $"Route figures from {r.ProviderName ?? "the routing provider"} cannot be saved under its terms. " +
                "Enter the miles and drive time manually to save this estimate.");
        if (snapshot.Input is { DestinationProviderName: { } p, DestinationProviderPermitsRetention: false })
            throw new RetentionNotPermittedException(
                $"The address returned by {p} cannot be saved under its terms. Type the job address to save this estimate.");
    }
}
