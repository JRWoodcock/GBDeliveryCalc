namespace GBDeliveryCalc.Core.Calculation;

/// <summary>Currency rounding and time conversion shared by all calculations.</summary>
public static class Money
{
    /// <summary>Rounds a currency amount to cents, half away from zero (customary for invoices).</summary>
    public static decimal ToCents(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Exact hours from whole minutes. Precision is kept until currency is rounded.</summary>
    public static decimal Hours(int minutes) => minutes / 60m;
}
