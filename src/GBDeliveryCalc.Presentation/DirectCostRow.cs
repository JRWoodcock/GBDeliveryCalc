using CommunityToolkit.Mvvm.ComponentModel;
using GBDeliveryCalc.Core.Model;

namespace GBDeliveryCalc.Presentation;

/// <summary>An editable other-cost row (description and amount).</summary>
public sealed partial class DirectCostRow : ObservableObject
{
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty] public partial string AmountText { get; set; } = "";

    public DirectCost ToModel() => new()
    {
        Description = Description.Trim(),
        Amount = Parse.OptionalDecimal(AmountText, $"Cost '{Description}'") ?? 0m,
    };
}

/// <summary>A formatted cost line for display.</summary>
public sealed record LineView(string Label, string Detail, string Amount)
{
    public override string ToString() => $"{Label}: {Amount}";
}
