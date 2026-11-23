using GBDeliveryCalc.Core.Calculation;

namespace GBDeliveryCalc.Core.Model;

/// <summary>A fuel price used on an estimate, with its source.</summary>
public sealed record FuelPrice
{
    public FuelType FuelType { get; init; }

    /// <summary>Two-letter state whose average this is. Older saved estimates are Mississippi.</summary>
    public string State { get; init; } = UsStates.Default;

    public decimal PricePerGallon { get; init; }

    /// <summary>E.g. "Manual entry" or the name of an approved data source.</summary>
    public string Source { get; init; } = "";

    /// <summary>Date the source published this price, when known.</summary>
    public DateOnly? PublishedDate { get; init; }

    public DateTimeOffset RetrievedAt { get; init; }
    public bool IsManualOverride { get; init; }

    /// <summary>True only for values returned by an approved, live source. Never true for sample data.</summary>
    public bool IsLive { get; init; }

    public string Describe() =>
        IsManualOverride
            ? $"{FuelType.Label()} {Fmt.Rate(PricePerGallon)}/gal (manual entry)"
            : $"{FuelType.Label()} {Fmt.Rate(PricePerGallon)}/gal ({Source}{(PublishedDate is { } d ? $", published {d:yyyy-MM-dd}" : "")})";
}
