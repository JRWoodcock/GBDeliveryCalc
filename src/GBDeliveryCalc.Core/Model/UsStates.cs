using System.Text.RegularExpressions;

namespace GBDeliveryCalc.Core.Model;

/// <summary>US state codes and names, and finding the state in an address.</summary>
public static partial class UsStates
{
    /// <summary>Used when no state can be found in the job or starting address.</summary>
    public const string Default = "MS";

    public static IReadOnlyDictionary<string, string> Names { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "Alabama", ["AK"] = "Alaska", ["AZ"] = "Arizona", ["AR"] = "Arkansas", ["CA"] = "California",
        ["CO"] = "Colorado", ["CT"] = "Connecticut", ["DE"] = "Delaware", ["DC"] = "District of Columbia", ["FL"] = "Florida",
        ["GA"] = "Georgia", ["HI"] = "Hawaii", ["ID"] = "Idaho", ["IL"] = "Illinois", ["IN"] = "Indiana",
        ["IA"] = "Iowa", ["KS"] = "Kansas", ["KY"] = "Kentucky", ["LA"] = "Louisiana", ["ME"] = "Maine",
        ["MD"] = "Maryland", ["MA"] = "Massachusetts", ["MI"] = "Michigan", ["MN"] = "Minnesota", ["MS"] = "Mississippi",
        ["MO"] = "Missouri", ["MT"] = "Montana", ["NE"] = "Nebraska", ["NV"] = "Nevada", ["NH"] = "New Hampshire",
        ["NJ"] = "New Jersey", ["NM"] = "New Mexico", ["NY"] = "New York", ["NC"] = "North Carolina", ["ND"] = "North Dakota",
        ["OH"] = "Ohio", ["OK"] = "Oklahoma", ["OR"] = "Oregon", ["PA"] = "Pennsylvania", ["RI"] = "Rhode Island",
        ["SC"] = "South Carolina", ["SD"] = "South Dakota", ["TN"] = "Tennessee", ["TX"] = "Texas", ["UT"] = "Utah",
        ["VT"] = "Vermont", ["VA"] = "Virginia", ["WA"] = "Washington", ["WV"] = "West Virginia", ["WI"] = "Wisconsin",
        ["WY"] = "Wyoming",
    };

    public static string Name(string? code) => code is not null && Names.TryGetValue(code, out var n) ? n : code ?? "";

    /// <summary>
    /// Finds the state in an address such as "1125 Golf Course Rd, Philadelphia, MS 39350, USA" or
    /// "Mobile, Alabama". Returns the two-letter code, or null when no state is named.
    /// </summary>
    public static string? FindInAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        string? found = null;
        // ", MS 39350", ", MS", ", MS," (upper-case codes only, so ordinary words aren't mistaken for states)
        foreach (Match m in CodePattern().Matches(address))
            if (Names.ContainsKey(m.Groups[1].Value)) found = m.Groups[1].Value.ToUpperInvariant();
        if (found is not null) return found;
        // ", Mississippi" / ", West Virginia 25301"
        var bestIndex = -1;
        foreach (var (code, name) in Names)
        {
            var i = address.LastIndexOf(", " + name, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            var end = i + 2 + name.Length;
            if (end < address.Length && char.IsLetter(address[end])) continue; // "Virginia" inside "Virginia Beach" etc.
            if (i > bestIndex || (i == bestIndex && name.Length > Name(found).Length)) { bestIndex = i; found = code; }
        }
        return found;
    }

    [GeneratedRegex(@",\s*([A-Z]{2})(?=\s+\d{5}(?:-\d{4})?\b|\s*,|\s*$)")]
    private static partial Regex CodePattern();
}
