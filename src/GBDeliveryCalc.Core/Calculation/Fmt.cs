using System.Globalization;

namespace GBDeliveryCalc.Core.Calculation;

/// <summary>Formatting helpers. Always en-US so quotes look the same on every PC.</summary>
public static class Fmt
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public static string Money(decimal amount) => amount.ToString("C2", Culture);
    public static string Money(decimal? amount) => amount is { } a ? Money(a) : "—";
    public static string Rate(decimal rate) => "$" + rate.ToString("0.00##", Culture);
    public static string Miles(decimal miles) => miles.ToString("#,##0.0", Culture) + " mi";
    public static string Number(decimal value, string format = "0.##") => value.ToString(format, Culture);

    public static string Duration(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        return $"{h} h {m:00} min";
    }


    public static decimal? ParseDecimal(string? text) =>
        decimal.TryParse((text ?? "").Trim().TrimStart('$'), NumberStyles.Number, Culture, out var v) ? v : null;
}
