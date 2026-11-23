using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GBDeliveryCalc.Core.Calculation;
using GBDeliveryCalc.Core.Model;

namespace GBDeliveryCalc.Presentation;

/// <summary>A formatted comparison row for display.</summary>
public sealed record ComparisonView(string Label, string Estimated, string Actual, string Variance);

/// <summary>History tab: lists saved estimates, shows one, records actuals, and starts edit or delete.</summary>
public sealed partial class HistoryViewModel(EstimatorServices services, IClipboardService clipboard, IDialogService dialogs) : ObservableObject
{
    private bool _loading;

    /// <summary>Raised when staff click Edit; the main window loads it into the New estimate form.</summary>
    public event EventHandler<EstimateSnapshot>? EditRequested;

    /// <summary>Raised after an estimate is deleted (id).</summary>
    public event EventHandler<Guid>? EstimateDeleted;

    public ObservableCollection<EstimateSummary> Estimates { get; } = [];
    public ObservableCollection<DirectCostRow> ActualOtherCosts { get; } = [];

    [ObservableProperty] public partial EstimateSummary? SelectedSummary { get; set; }
    [ObservableProperty] public partial EstimateSnapshot? Selected { get; private set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";

    // read-only estimate details (from the snapshot, never from current settings)
    [ObservableProperty] public partial IReadOnlyList<LineView> CostLines { get; private set; } = [];
    [ObservableProperty] public partial string EstimateHeader { get; private set; } = "";
    [ObservableProperty] public partial string TeamAndVehicleText { get; private set; } = "";
    [ObservableProperty] public partial string RouteText { get; private set; } = "";
    [ObservableProperty] public partial string FuelText { get; private set; } = "";
    [ObservableProperty] public partial string LegacyNote { get; private set; } = "";

    // actuals inputs
    [ObservableProperty] public partial string ActualMilesText { get; set; } = "";
    [ObservableProperty] public partial string ActualDriveHoursText { get; set; } = "";
    [ObservableProperty] public partial string ActualDriveMinutesText { get; set; } = "";
    [ObservableProperty] public partial string ActualInstallHoursText { get; set; } = "";
    [ObservableProperty] public partial string ActualInstallMinutesText { get; set; } = "";
    [ObservableProperty] public partial string ActualPaidHoursText { get; set; } = "";
    [ObservableProperty] public partial string ActualLaborCostText { get; set; } = "";
    [ObservableProperty] public partial string ActualFuelCostText { get; set; } = "";
    [ObservableProperty] public partial bool OtherCostsRecorded { get; set; }
    [ObservableProperty] public partial string ActualNotes { get; set; } = "";

    // comparison outputs
    [ObservableProperty] public partial IReadOnlyList<ComparisonView> Comparison { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> Assumptions { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> Issues { get; private set; } = [];
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";

    public bool HasSelection => Selected is not null;

    /// <summary>Billing-era estimates are read-only.</summary>
    public bool CanEdit => Selected is { IsLegacy: false };

    public async Task LoadAsync()
    {
        var keep = SelectedSummary?.Id;
        Estimates.Clear();
        var q = SearchText.Trim();
        foreach (var s in await services.Estimates.GetSummariesAsync().ConfigureAwait(true))
        {
            if (q.Length == 0 || $"{s.QuoteNumber} {s.CustomerName} {s.Destination} {s.TeamName}".Contains(q, StringComparison.CurrentCultureIgnoreCase))
                Estimates.Add(s);
        }
        SelectedSummary = Estimates.FirstOrDefault(e => e.Id == keep);
        if (SelectedSummary is null && keep is not null) await SelectAsync(null).ConfigureAwait(true);
    }

    partial void OnSearchTextChanged(string value) => _ = LoadAsync();

    async partial void OnSelectedSummaryChanged(EstimateSummary? value)
    {
        try { await SelectAsync(value?.Id).ConfigureAwait(true); }
        catch (Exception ex) { StatusMessage = "Could not open estimate: " + ex.Message; }
    }

    public async Task SelectAsync(Guid? id)
    {
        _loading = true;
        try
        {
            Selected = id is { } i ? await services.Estimates.GetAsync(i).ConfigureAwait(true) : null;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanEdit));
            ActualOtherCosts.Clear();
            if (Selected is not { } s)
            {
                CostLines = [];
                Comparison = [];
                return;
            }
            var input = s.Input;
            var res = s.Result;
            EstimateHeader = $"{s.QuoteNumber} · {s.CreatedAt.LocalDateTime:g}" +
                             (s.UpdatedAt is { } u ? $" · edited {u.LocalDateTime:g} (version {s.Revision})" : "") +
                             $" · {(string.IsNullOrWhiteSpace(s.CustomerName) ? "(no customer name)" : s.CustomerName)}";
            LegacyNote = s.IsLegacy
                ? "Saved before estimates became cost-only (it included customer billing). Shown read-only with its stored cost lines; it can't be edited."
                : "";
            CostLines = res.CostLines.Select(l => new LineView(l.Label, l.Detail, l.Amount is { } a ? Fmt.Money(a) : "Incomplete"))
                .Append(new LineView("Total estimated cost", "", res.TotalDirectCost is { } c ? Fmt.Money(c) : "Incomplete"))
                .ToList();
            TeamAndVehicleText =
                $"Team: {input.Team?.Name}{(input.Team?.WasArchived == true ? " (archived)" : "")} — {input.Team?.Description}; " +
                $"loaded labor cost {Fmt.Money(input.Team?.LoadedLaborCostPerHour)}/hr. " +
                $"Vehicle: {input.Vehicle?.Name ?? "none"}{(input.VehicleOverridden ? " (overridden for this estimate)" : "")}" +
                (input.Vehicle is { } v ? $", {v.FuelType.Label()}, {Fmt.Number(v.EstimatedMpg ?? 0)} MPG, {Fmt.Money(v.OperatingCostPerMileExcludingFuel)}/mi operating." : ".");
            RouteText = $"{input.OriginAddress} → {(string.IsNullOrWhiteSpace(input.DestinationAddress) ? input.DestinationQuery : input.DestinationAddress)}; " +
                        $"{input.TripType.Label()}, {Fmt.Miles(res.TotalMiles)}, {Fmt.Duration(res.TotalDriveMinutes)} driving, {Fmt.Duration(res.InstallMinutes)} installation. " +
                        (input.Route?.ProvenanceLabel ?? "") + (input.RoundLaborHoursUp ? " Labor hours rounded up to whole hours." : "");
            FuelText = input.Fuel?.Describe() ?? "No fuel price recorded.";

            var a = await services.Estimates.GetActualsAsync(s.Id).ConfigureAwait(true);
            ActualMilesText = Parse.Text(a?.TripMiles);
            (ActualDriveHoursText, ActualDriveMinutesText) = a?.DriveMinutes is { } dm ? Parse.Split(dm) : ("", "");
            (ActualInstallHoursText, ActualInstallMinutesText) = a?.InstallMinutes is { } im ? Parse.Split(im) : ("", "");
            ActualPaidHoursText = Parse.Text(a?.PaidTeamHours);
            ActualLaborCostText = Parse.Text(a?.LaborCostOverride);
            ActualFuelCostText = Parse.Text(a?.FuelCost);
            ActualNotes = a?.Notes ?? "";
            OtherCostsRecorded = a?.OtherDirectCosts is not null;
            foreach (var oc in a?.OtherDirectCosts ?? [])
                AddRow(new DirectCostRow { Description = oc.Description, AmountText = Parse.Text(oc.Amount) });
            StatusMessage = a is null ? "No actuals recorded yet." : $"Actuals last updated {a.UpdatedAt.LocalDateTime:g}.";
        }
        finally { _loading = false; }
        RecomputeComparison();
    }

    private void AddRow(DirectCostRow row)
    {
        row.PropertyChanged += (_, _) => RecomputeComparison();
        ActualOtherCosts.Add(row);
    }

    [RelayCommand]
    private void AddActualCost()
    {
        OtherCostsRecorded = true;
        AddRow(new DirectCostRow());
    }

    [RelayCommand]
    private void RemoveActualCost(DirectCostRow? row)
    {
        if (row is null) return;
        ActualOtherCosts.Remove(row);
        RecomputeComparison();
    }

    [RelayCommand]
    private void CopySavedEstimate()
    {
        if (Selected is null) return;
        clipboard.SetText(Selected.DisplayText);
        StatusMessage = "Saved estimate copied to the clipboard.";
    }

    [RelayCommand]
    private void Edit()
    {
        if (Selected is not { } s) return;
        if (s.IsLegacy) { StatusMessage = LegacyNote; return; }
        EditRequested?.Invoke(this, s);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is not { } s) return;
        if (!dialogs.Confirm("Delete estimate",
                $"Delete estimate {s.QuoteNumber}{(string.IsNullOrWhiteSpace(s.CustomerName) ? "" : $" ({s.CustomerName})")} with its actuals and previous versions? This can't be undone."))
            return;
        await services.Estimates.DeleteAsync(s.Id).ConfigureAwait(true);
        SelectedSummary = null;
        await LoadAsync().ConfigureAwait(true);
        await SelectAsync(null).ConfigureAwait(true);
        StatusMessage = $"Deleted estimate {s.QuoteNumber}.";
        EstimateDeleted?.Invoke(this, s.Id);
    }

    private static readonly HashSet<string> ActualInputs =
    [
        nameof(ActualMilesText), nameof(ActualDriveHoursText), nameof(ActualDriveMinutesText), nameof(ActualInstallHoursText),
        nameof(ActualInstallMinutesText), nameof(ActualPaidHoursText), nameof(ActualLaborCostText), nameof(ActualFuelCostText),
        nameof(OtherCostsRecorded),
    ];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is { } n && ActualInputs.Contains(n)) RecomputeComparison();
    }

    /// <summary>Builds actuals from the form. Throws FormatException for invalid entries.</summary>
    public EstimateActuals BuildActuals()
    {
        var hasDrive = !string.IsNullOrWhiteSpace(ActualDriveHoursText) || !string.IsNullOrWhiteSpace(ActualDriveMinutesText);
        var hasInstall = !string.IsNullOrWhiteSpace(ActualInstallHoursText) || !string.IsNullOrWhiteSpace(ActualInstallMinutesText);
        return new EstimateActuals
        {
            TripMiles = Parse.OptionalDecimal(ActualMilesText, "Actual miles"),
            DriveMinutes = hasDrive ? Parse.Minutes(ActualDriveHoursText, ActualDriveMinutesText, "Actual drive time") : null,
            InstallMinutes = hasInstall ? Parse.Minutes(ActualInstallHoursText, ActualInstallMinutesText, "Actual installation time") : null,
            PaidTeamHours = Parse.OptionalDecimal(ActualPaidHoursText, "Actual paid hours"),
            LaborCostOverride = Parse.OptionalDecimal(ActualLaborCostText, "Actual labor cost"),
            FuelCost = Parse.OptionalDecimal(ActualFuelCostText, "Actual fuel cost"),
            OtherDirectCosts = OtherCostsRecorded ? ActualOtherCosts.Select(r => r.ToModel()).ToList() : null,
            Notes = ActualNotes.Trim(),
            UpdatedAt = services.Clock.GetLocalNow(),
        };
    }

    public void RecomputeComparison()
    {
        if (Selected is not { } s) return;
        EstimateActuals actuals;
        try { actuals = BuildActuals(); }
        catch (FormatException ex)
        {
            Issues = [ex.Message];
            return;
        }
        var cmp = ActualsComparer.Compare(s, actuals);
        Comparison = cmp.Rows.Select(r => new ComparisonView(r.Label, Fmt.Money(r.Estimated), Fmt.Money(r.Actual),
            r.Variance is { } v ? (v >= 0 ? "+" : "") + Fmt.Money(v) : "—")).ToList();
        Assumptions = cmp.Assumptions;
        Issues = cmp.Issues;
    }

    [RelayCommand]
    private async Task SaveActualsAsync()
    {
        if (Selected is not { } s) return;
        try
        {
            await services.Estimates.SaveActualsAsync(s.Id, BuildActuals()).ConfigureAwait(true);
            StatusMessage = "Actuals saved. The estimate itself is unchanged.";
            await LoadAsync().ConfigureAwait(true);
        }
        catch (FormatException ex) { StatusMessage = "Not saved: " + ex.Message; }
    }
}
