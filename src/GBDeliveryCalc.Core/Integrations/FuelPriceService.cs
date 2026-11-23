using GBDeliveryCalc.Core.Model;
using GBDeliveryCalc.Core.Persistence;

namespace GBDeliveryCalc.Core.Integrations;

/// <summary>Result of a fuel price request: the price to show and where it came from.</summary>
public sealed record FuelPriceLookup
{
    /// <summary>Price to show (fresh, cached today, or last known). Null if none has ever been retrieved.</summary>
    public FuelPrice? Price { get; init; }
    public bool FromCacheToday { get; init; }

    /// <summary>True when the source failed and <see cref="Price"/> is an older last-known value.</summary>
    public bool IsLastKnownFallback { get; init; }
    public TimeSpan? Age { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Fetches a state's average at most once per fuel type and state per local calendar day: a verified live value is kept in
/// the database and reused for the rest of that day, also across restarts and new estimates. When a call fails (or
/// returns a value the source marks as not current), the source is not called again for that fuel type and state for
/// <see cref="RetryInterval"/>; the last known value is shown with its age meanwhile. The attempt time is persisted.
/// </summary>
public sealed class FuelPriceService(IFuelPriceSource source, IFuelPriceCache cache, TimeProvider clock)
{
    public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    /// <summary>Today's average for <paramref name="fuelType"/> in <paramref name="state"/>, limited as described above.</summary>
    public async Task<FuelPriceLookup> GetAsync(FuelType fuelType, string state = UsStates.Default, CancellationToken ct = default)
    {
        state = string.IsNullOrWhiteSpace(state) ? UsStates.Default : state.Trim().ToUpperInvariant();
        var now = clock.GetLocalNow();
        var cached = await cache.GetLatestAsync(fuelType, state, ct).ConfigureAwait(false);
        if (cached is { IsLive: true } && cached.FuelType == fuelType && LocalDate(cached.RetrievedAt) == DateOnly.FromDateTime(now.DateTime))
            return new FuelPriceLookup { Price = cached, FromCacheToday = true, Age = now - cached.RetrievedAt };

        FuelPriceLookup Fallback(string error) => new()
        {
            Price = cached,
            IsLastKnownFallback = cached is not null,
            Age = cached is null ? null : now - cached.RetrievedAt,
            Error = error,
        };

        // No key / no approved source: nothing to call, and nothing to throttle.
        if (source.Info.Status != ProviderStatus.Available) return Fallback(source.Info.StatusMessage);

        var attempt = await cache.GetFetchStateAsync(fuelType, state, ct).ConfigureAwait(false);
        if (attempt is { LastError: { } lastError } && now - attempt.LastAttemptAt < RetryInterval)
        {
            var next = TimeZoneInfo.ConvertTime(attempt.LastAttemptAt + RetryInterval, clock.LocalTimeZone);
            return Fallback($"{lastError} The app will try again after {next.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}.");
        }

        try
        {
            var fresh = await source.GetStateAverageAsync(fuelType, state, ct).ConfigureAwait(false);
            if (fresh.FuelType != fuelType)
                throw new ProviderUnavailableException($"Source returned {fresh.FuelType.Label()} when {fuelType.Label()} was requested.");
            if (fresh.PricePerGallon <= 0m)
                throw new ProviderUnavailableException("Source returned a non-positive price.");
            fresh = fresh with { RetrievedAt = now, IsManualOverride = false, State = state };
            if (fresh.IsLive)
            {
                await cache.SaveAsync(fresh, ct).ConfigureAwait(false);
                await cache.SaveFetchStateAsync(new FuelFetchState(fuelType, now, null, state), ct).ConfigureAwait(false);
            }
            else
            {
                // Shown once, never reused as today's price; ask again after the retry interval.
                await cache.SaveFetchStateAsync(new FuelFetchState(fuelType, now, "The source's latest value is not current.", state), ct).ConfigureAwait(false);
            }
            return new FuelPriceLookup { Price = fresh, Age = TimeSpan.Zero };
        }
        catch (Exception ex) when (ex is ProviderUnavailableException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            await cache.SaveFetchStateAsync(new FuelFetchState(fuelType, now, ex.Message, state), CancellationToken.None).ConfigureAwait(false);
            return Fallback(ex.Message);
        }
    }

    private DateOnly LocalDate(DateTimeOffset t) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t, clock.LocalTimeZone).DateTime);

    public static FuelPrice Manual(FuelType fuelType, decimal pricePerGallon, DateTimeOffset at) => new()
    {
        FuelType = fuelType,
        PricePerGallon = pricePerGallon,
        Source = "Manual entry",
        RetrievedAt = at,
        IsManualOverride = true,
        IsLive = false,
    };
}
