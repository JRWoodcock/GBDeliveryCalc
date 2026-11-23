using GBDeliveryCalc.Core.Calculation;

namespace GBDeliveryCalc.Presentation;

/// <summary>Parses form input with messages staff can act on.</summary>
internal static class Parse
{
    /// <summary>Null for blank; throws FormatException with a friendly message for junk.</summary>
    public static decimal? OptionalDecimal(string? text, string field, bool allowNegative = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var v = Fmt.ParseDecimal(text) ?? throw new FormatException($"{field} is not a number");
        if (!allowNegative && v < 0) throw new FormatException($"{field} cannot be negative");
        return v;
    }

    public static int Minutes(string? hours, string? minutes, string field)
    {
        var h = OptionalDecimal(hours, field + " hours") ?? 0m;
        var m = OptionalDecimal(minutes, field + " minutes") ?? 0m;
        if (m != Math.Floor(m)) throw new FormatException($"{field} minutes must be a whole number");
        var total = h * 60m + m;
        if (total != Math.Floor(total)) throw new FormatException($"{field} hours must convert to whole minutes");
        return (int)total;
    }

    public static (string Hours, string Minutes) Split(int totalMinutes) =>
        ((totalMinutes / 60).ToString(Fmt.Culture), (totalMinutes % 60).ToString(Fmt.Culture));

    public static string Text(decimal? value) => value is { } v ? v.ToString("0.####", Fmt.Culture) : "";
}
