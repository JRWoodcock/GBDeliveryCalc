using System.Text;
using GBDeliveryCalc.Core.Calculation;
using GBDeliveryCalc.Core.Model;

namespace GBDeliveryCalc.Core.Quotes;

/// <summary>
/// Builds the plain-text internal cost estimate (for the salesman to price from). It names the team and vehicle,
/// never the team description.
/// </summary>
public static class CostEstimateFormatter
{
    public const string CompanyName = "Grover Brothers Restaurant Supply";

    public static string Format(EstimateInput input, EstimateResult result, string? estimateNumber = null, DateOnly? date = null, string? customerName = null)
    {
        if (!result.IsCostComplete || result.TotalDirectCost is null)
            throw new InvalidOperationException("Cost estimate is incomplete: " + string.Join("; ", result.CostIssues));

        var sb = new StringBuilder();
        sb.AppendLine(CompanyName);
        sb.AppendLine("Travel & Installation Cost Estimate (internal)");
        if (!string.IsNullOrWhiteSpace(estimateNumber)) sb.AppendLine(Fmt.Culture, $"Estimate #: {estimateNumber}");
        if (date is { } d) sb.AppendLine(Fmt.Culture, $"Date: {d.ToString("MMMM d, yyyy", Fmt.Culture)}");
        if (!string.IsNullOrWhiteSpace(customerName)) sb.AppendLine(Fmt.Culture, $"Customer: {customerName.Trim()}");
        var dest = string.IsNullOrWhiteSpace(input.DestinationAddress) ? input.DestinationQuery : input.DestinationAddress;
        if (!string.IsNullOrWhiteSpace(dest)) sb.AppendLine(Fmt.Culture, $"Job site: {dest.Trim()}");
        sb.AppendLine(Fmt.Culture, $"Trip: {input.TripType.Label()}, {Fmt.Miles(result.TotalMiles)}, {Fmt.Duration(result.TotalDriveMinutes)} driving");
        if (input.Team is { } t) sb.AppendLine(Fmt.Culture, $"Team: {t.Name}");
        if (input.Vehicle is { } v) sb.AppendLine(Fmt.Culture, $"Vehicle: {v.Name}");
        sb.AppendLine();

        const int width = 50;
        foreach (var line in result.CostLines)
        {
            var amount = Fmt.Money(line.Amount ?? 0m);
            sb.AppendLine(Fmt.Culture, $"{line.Label.PadRight(Math.Max(1, width - amount.Length))}{amount}");
            if (!string.IsNullOrWhiteSpace(line.Detail)) sb.AppendLine(Fmt.Culture, $"  {line.Detail}");
        }
        sb.AppendLine(new string('-', width));
        var total = Fmt.Money(result.TotalDirectCost.Value);
        sb.AppendLine(Fmt.Culture, $"{"Total estimated cost".PadRight(width - total.Length)}{total}");

        if (result.Notes.Count > 0)
        {
            sb.AppendLine();
            foreach (var n in result.Notes) sb.AppendLine(Fmt.Culture, $"Note: {n}");
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }
}
