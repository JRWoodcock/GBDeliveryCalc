using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GBDeliveryCalc.Core.Calculation;
using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Core.Model;
using GBDeliveryCalc.Core.Persistence;
using GBDeliveryCalc.Core.Quotes;
using GBDeliveryCalc.Services;

namespace GBDeliveryCalc.Presentation;

/// <summary>
/// The cost-estimate form. Miles and drive time always live in the manual-entry boxes: a route lookup (run
/// automatically when an address is confirmed) just fills them. Fuel prices are fetched automatically.
/// </summary>
public sealed partial class NewEstimateViewModel : ObservableObject, IDisposable
{
    /// <summary>How often the app checks for a new local day (and retries a failed fuel fetch, hourly at most).</summary>
    public static readonly TimeSpan FuelCheckInterval = TimeSpan.FromMinutes(15);

    private readonly EstimatorServices _services;
    private readonly IClipboardService _clipboard;
    private readonly SynchronizationContext? _ui;
    private readonly ITimer? _fuelTimer;
    private CompanySettings _company = new();
    private IntegrationSet _integrations;
    private FuelPriceService _fuelService;
    private TripType _manualTotalTripType = TripType.RoundTrip;
    private readonly Dictionary<(FuelType, string), FuelPriceLookup> _fuel = [];
    private DateOnly? _fuelDate;
    private FuelPrice? _pinnedFuel; // fetched price saved with an estimate being edited
    private bool _suspendRecalc;
    private bool _filling;
    private CopiedRoute? _copied;
    private EstimateInput _lastInput = new();
    private EstimateInput? _savedInput;
    private (Guid Id, string Number)? _editing;

    /// <summary>What a route lookup put into the manual boxes, so later edits can be detected.</summary>
    private sealed record CopiedRoute(string Provider, RouteKey Key, RouteLeg Outbound, RouteLeg? Return, TripType TripType,
        string Miles = "", string Hours = "", string Minutes = "");

    /// <summary>Raised after an estimate is saved or updated, so History can refresh.</summary>
    public event EventHandler<EstimateSnapshot>? EstimateSaved;

    public NewEstimateViewModel(EstimatorServices services, IClipboardService clipboard)
    {
        _services = services;
        _clipboard = clipboard;
        _ui = SynchronizationContext.Current;
        _integrations = services.CreateIntegrations();
        _fuelService = new FuelPriceService(_integrations.Fuel, services.FuelCache, services.Clock);
        Candidates.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowPoweredByGoogle));
        OtherCosts.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null) foreach (DirectCostRow r in e.NewItems) r.PropertyChanged += (_, _) => Recalculate();
            Recalculate();
        };
        // Only when there is a UI thread to come back to (the desktop app); tests call CheckFuelPricesAsync directly.
        if (_ui is not null)
            _fuelTimer = services.Clock.CreateTimer(_ => _ui.Post(async _ => await CheckFuelPricesAsync().ConfigureAwait(true), null),
                null, FuelCheckInterval, FuelCheckInterval);
    }

    public void Dispose() => _fuelTimer?.Dispose();

    // ---------------- reference data ----------------
    public ObservableCollection<TeamProfile> Teams { get; } = [];
    public ObservableCollection<VehicleProfile> Vehicles { get; } = [];
    public ObservableCollection<PlaceCandidate> Candidates { get; } = [];
    public ObservableCollection<DirectCostRow> OtherCosts { get; } = [];

    // ---------------- inputs ----------------
    [ObservableProperty] public partial TeamProfile? SelectedTeam { get; set; }
    [ObservableProperty] public partial VehicleProfile? SelectedVehicle { get; set; }
    [ObservableProperty] public partial string CustomerName { get; set; } = "";
    [ObservableProperty] public partial string OriginAddress { get; set; } = "";
    [ObservableProperty] public partial string DestinationQuery { get; set; } = "";
    [ObservableProperty] public partial string DestinationAddress { get; set; } = "";
    [ObservableProperty] public partial PlaceCandidate? SelectedCandidate { get; set; }
    [ObservableProperty] public partial bool IsRoundTrip { get; set; } = true;
    [ObservableProperty] public partial bool ManualIsTripTotal { get; set; }
    [ObservableProperty] public partial string ManualMilesText { get; set; } = "";
    [ObservableProperty] public partial string ManualDriveHoursText { get; set; } = "";
    [ObservableProperty] public partial string ManualDriveMinutesText { get; set; } = "";
    [ObservableProperty] public partial string InstallHoursText { get; set; } = "3";
    [ObservableProperty] public partial string InstallMinutesText { get; set; } = "0";
    [ObservableProperty] public partial string ManualFuelPriceText { get; set; } = "";
    [ObservableProperty] public partial string PaidHoursOverrideText { get; set; } = "";
    [ObservableProperty] public partial string Notes { get; set; } = "";

    // ---------------- outputs ----------------
    [ObservableProperty] public partial EstimateResult Result { get; private set; } = new();
    [ObservableProperty] public partial IReadOnlyList<LineView> CostLines { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> CostIssues { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> CostNotes { get; private set; } = [];
    [ObservableProperty] public partial string TotalCostText { get; private set; } = "Incomplete";
    [ObservableProperty] public partial string EstimatePreview { get; private set; } = "";
    [ObservableProperty] public partial string TripSummary { get; private set; } = "";
    [ObservableProperty] public partial string RouteSourceText { get; private set; } = "";
    [ObservableProperty] public partial bool RouteIsStale { get; private set; }
    [ObservableProperty] public partial string TeamRatesText { get; private set; } = "";
    [ObservableProperty] public partial string VehicleText { get; private set; } = "";
    [ObservableProperty] public partial bool VehicleOverridden { get; private set; }
    [ObservableProperty] public partial string FuelStatusText { get; private set; } = "";
    [ObservableProperty] public partial bool FuelIsStaleOrMissing { get; private set; } = true;
    [ObservableProperty] public partial string LaborHoursText { get; private set; } = "";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";
    [ObservableProperty] public partial string LookupMessage { get; private set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string EditingText { get; private set; } = "";

    public string ManualMilesLabel => ManualIsTripTotal ? "Total trip miles" : (IsRoundTrip ? "Miles per leg (each way)" : "Miles (one way)");
    public string ManualDriveLabel => ManualIsTripTotal ? "Total drive time" : (IsRoundTrip ? "Drive time per leg (each way)" : "Drive time (one way)");
    public ProviderInfo PlacesInfo => _integrations.Places.Info;
    public ProviderInfo RoutingInfo => _integrations.Routing.Info;
    public ProviderInfo FuelInfo => _integrations.Fuel.Info;
    public bool CanLookUpAddress => PlacesInfo.Status == ProviderStatus.Available;
    public bool CanLookUpRoute => RoutingInfo.Status == ProviderStatus.Available;
    public bool ShowRerunRouteLookup => RouteIsStale && CanLookUpRoute;
    public string IntegrationBanner =>
        string.Join(Environment.NewLine, new[] { PlacesInfo, RoutingInfo, FuelInfo }
            .Where(i => i.Status != ProviderStatus.Available)
            .Select(i => $"{i.Name}: {i.StatusMessage}").Distinct());

    /// <summary>Google requires "Powered by Google" next to Places results shown without a Google map.</summary>
    public bool ShowPoweredByGoogle => Candidates.Count > 0 &&
        (_placeProvider ?? "").StartsWith("Google", StringComparison.OrdinalIgnoreCase);

    public TripType TripType => IsRoundTrip ? TripType.RoundTrip : TripType.OneWay;

    /// <summary>The saved estimate being edited (null for a new one).</summary>
    public Guid? EditingId => _editing?.Id;
    public bool IsEditing => _editing is not null;
    public string SaveButtonText => IsEditing ? "Save changes" : "Save estimate";

    // ---------------- lifecycle ----------------
    public async Task LoadAsync()
    {
        _suspendRecalc = true;
        try
        {
            _company = await _services.Settings.GetCompanySettingsAsync().ConfigureAwait(true);
            _integrations = _services.CreateIntegrations();
            _fuelService = new FuelPriceService(_integrations.Fuel, _services.FuelCache, _services.Clock);
            var keepTeam = SelectedTeam;
            var keepVehicle = SelectedVehicle;
            Teams.Clear();
            foreach (var t in await _services.Settings.GetTeamsAsync(false).ConfigureAwait(true)) Teams.Add(t);
            Vehicles.Clear();
            foreach (var v in await _services.Settings.GetVehiclesAsync(false).ConfigureAwait(true)) Vehicles.Add(v);
            SelectedTeam = keepTeam is null ? null : Teams.FirstOrDefault(t => t.Id == keepTeam.Id) ?? KeepListed(Teams, keepTeam, IsEditing);
            SelectedVehicle = keepVehicle is null ? null : Vehicles.FirstOrDefault(v => v.Id == keepVehicle.Id) ?? KeepListed(Vehicles, keepVehicle, IsEditing);
            if (string.IsNullOrWhiteSpace(OriginAddress)) OriginAddress = _company.OriginAddress;
            foreach (var n in new[] { nameof(PlacesInfo), nameof(RoutingInfo), nameof(FuelInfo), nameof(CanLookUpAddress), nameof(CanLookUpRoute),
                         nameof(IntegrationBanner), nameof(ShowPoweredByGoogle), nameof(ShowRerunRouteLookup) })
                OnPropertyChanged(n);
        }
        finally { _suspendRecalc = false; }
        _fuel.Clear(); // the fuel key may have changed
        await RefreshFuelPricesAsync().ConfigureAwait(true);
    }

    /// <summary>An archived team or vehicle on an estimate being edited stays selectable.</summary>
    private static T? KeepListed<T>(ObservableCollection<T> list, T item, bool editing) where T : class
    {
        if (!editing) return null;
        list.Add(item);
        return item;
    }

    [RelayCommand]
    public async Task StartNewAsync()
    {
        _suspendRecalc = true;
        try
        {
            SelectedTeam = null;
            SelectedVehicle = null;
            CustomerName = DestinationQuery = DestinationAddress = ManualMilesText = ManualDriveHoursText = ManualDriveMinutesText = "";
            ManualFuelPriceText = PaidHoursOverrideText = Notes = LookupMessage = StatusMessage = "";
            Candidates.Clear();
            OtherCosts.Clear();
            _copied = null;
            _pinnedFuel = null;
            _savedInput = null;
            SetEditing(null);
            ManualIsTripTotal = false;
            IsRoundTrip = true;
            OriginAddress = "";
        }
        finally { _suspendRecalc = false; }
        await LoadAsync().ConfigureAwait(true);
        (InstallHoursText, InstallMinutesText) = Parse.Split(_company.DefaultInstallMinutes);
        Recalculate();
    }

    /// <summary>Loads a saved estimate into the form. Saving then updates it in place (same number).</summary>
    public async Task LoadForEditAsync(EstimateSnapshot s)
    {
        if (s.IsLegacy) throw new InvalidOperationException("Estimates saved before the cost-only change are read-only.");
        await StartNewAsync().ConfigureAwait(true);
        _suspendRecalc = true;
        try
        {
            var i = s.Input;
            SetEditing((s.Id, s.QuoteNumber));
            CustomerName = s.CustomerName;
            // The estimate keeps the team and vehicle values it was saved with.
            if (i.Team is { } t)
            {
                var team = new TeamProfile
                {
                    Id = t.TeamId,
                    Name = t.Name,
                    Description = t.Description,
                    LoadedLaborCostPerHour = t.LoadedLaborCostPerHour,
                    IsArchived = t.WasArchived,
                    DefaultVehicleId = Teams.FirstOrDefault(x => x.Id == t.TeamId)?.DefaultVehicleId
                };
                ReplaceOrAdd(Teams, team, x => x.Id == t.TeamId);
                SelectedTeam = team;
            }
            if (i.Vehicle is { } v)
            {
                var vehicle = new VehicleProfile
                {
                    Id = v.VehicleId,
                    Name = v.Name,
                    FuelType = v.FuelType,
                    EstimatedMpg = v.EstimatedMpg,
                    OperatingCostPerMileExcludingFuel = v.OperatingCostPerMileExcludingFuel,
                    IsArchived = v.WasArchived
                };
                ReplaceOrAdd(Vehicles, vehicle, x => x.Id == v.VehicleId);
                SelectedVehicle = vehicle;
            }
            OriginAddress = i.OriginAddress;
            DestinationQuery = i.DestinationQuery;
            DestinationAddress = i.DestinationAddress;
            IsRoundTrip = i.TripType == TripType.RoundTrip;
            if (i.Route is { } r)
            {
                ManualIsTripTotal = r.ManualBasis == ManualEntryBasis.TripTotal;
                _manualTotalTripType = r.TripType;
                var (miles, minutes) = ManualIsTripTotal ? (r.TotalMiles, r.TotalDriveMinutes) : (r.Outbound?.Miles ?? r.TotalMiles, r.Outbound?.DriveMinutes ?? r.TotalDriveMinutes);
                ManualMilesText = Parse.Text(miles);
                (ManualDriveHoursText, ManualDriveMinutesText) = Parse.Split(minutes);
                if (r.CopiedFromLookupProvider is { } p && r.Key is { } key)
                    _copied = new CopiedRoute(p, key, r.Outbound ?? new RouteLeg(miles, minutes), r.Return, r.TripType, ManualMilesText, ManualDriveHoursText, ManualDriveMinutesText);
            }
            (InstallHoursText, InstallMinutesText) = Parse.Split(i.InstallMinutes);
            PaidHoursOverrideText = Parse.Text(i.PaidHoursOverride);
            if (i.Fuel is { IsManualOverride: true } mf) ManualFuelPriceText = Parse.Text(mf.PricePerGallon);
            else _pinnedFuel = i.Fuel;
            foreach (var c in i.OtherDirectCosts) OtherCosts.Add(new DirectCostRow { Description = c.Description, AmountText = Parse.Text(c.Amount) });
            Notes = i.Notes;
        }
        finally { _suspendRecalc = false; }
        Recalculate();
        _savedInput = _lastInput;
        StatusMessage = $"Editing {s.QuoteNumber}. Save changes updates it in place; the previous version is kept.";
    }

    private static void ReplaceOrAdd<T>(ObservableCollection<T> list, T item, Func<T, bool> match)
    {
        var existing = list.FirstOrDefault(match);
        if (existing is not null) list[list.IndexOf(existing)] = item;
        else list.Add(item);
    }

    /// <summary>Called when the estimate being edited was deleted from History.</summary>
    public void DetachDeleted(Guid id)
    {
        if (_editing?.Id != id) return;
        SetEditing(null);
        _savedInput = null;
        StatusMessage = "The estimate you were editing was deleted. Saving now creates a new estimate.";
    }

    private void SetEditing((Guid Id, string Number)? editing)
    {
        _editing = editing;
        EditingText = editing is { } e ? $"Editing saved estimate {e.Number}" : "";
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditingId));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    // ---------------- reactions ----------------
    private static readonly HashSet<string> Outputs =
    [
        nameof(Result), nameof(CostLines), nameof(CostIssues), nameof(CostNotes), nameof(TotalCostText), nameof(EstimatePreview),
        nameof(TripSummary), nameof(RouteSourceText), nameof(RouteIsStale), nameof(TeamRatesText), nameof(VehicleText),
        nameof(VehicleOverridden), nameof(FuelStatusText), nameof(FuelIsStaleOrMissing), nameof(StatusMessage), nameof(LaborHoursText),
        nameof(LookupMessage), nameof(IsBusy), nameof(SelectedCandidate), nameof(ManualMilesLabel), nameof(ManualDriveLabel),
        nameof(EditingText), nameof(IsEditing), nameof(EditingId), nameof(SaveButtonText), nameof(ShowRerunRouteLookup),
    ];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is { } n && !Outputs.Contains(n)) Recalculate();
    }

    partial void OnSelectedTeamChanged(TeamProfile? value)
    {
        if (_suspendRecalc) return;
        // Selecting a team fills its default vehicle.
        if (value?.DefaultVehicleId is { } vid && Vehicles.FirstOrDefault(v => v.Id == vid) is { } def)
            SelectedVehicle = def;
    }

    partial void OnSelectedVehicleChanged(VehicleProfile? oldValue, VehicleProfile? newValue)
    {
        // A price saved with an edited estimate only applies to that fuel type.
        if (_pinnedFuel is not null && newValue?.FuelType != _pinnedFuel.FuelType) _pinnedFuel = null;
    }

    partial void OnIsRoundTripChanged(bool value)
    {
        OnPropertyChanged(nameof(ManualMilesLabel));
        OnPropertyChanged(nameof(ManualDriveLabel));
        if (_suspendRecalc) return;
        if (_copied is { } c)
        {
            // Looked-up legs are re-entered for the new trip type. A lookup made one way has no return leg, so
            // switching to a round trip looks the route up again (or doubles the outbound leg if that isn't possible).
            if (TripType == TripType.RoundTrip && c.Return is null && CanLookUpRoute)
                _ = RunRouteLookupCommand.ExecuteAsync(null);
            else
                FillFromLookup(c with { TripType = TripType, Key = c.Key with { TripType = TripType } });
        }
        else if (ManualIsTripTotal) ConvertTripTotals();
    }

    /// <summary>Hand-typed trip totals are doubled or halved when the trip type changes, and labelled.</summary>
    private void ConvertTripTotals()
    {
        if (_manualTotalTripType == TripType) return;
        decimal? miles;
        int minutes;
        try
        {
            miles = Parse.OptionalDecimal(ManualMilesText, "Miles");
            minutes = Parse.Minutes(ManualDriveHoursText, ManualDriveMinutesText, "Drive time");
        }
        catch (FormatException) { return; } // the input error is shown by Recalculate
        if (miles is null) { _manualTotalTripType = TripType; return; }

        var toRoundTrip = TripType == TripType.RoundTrip;
        var newMiles = toRoundTrip ? miles.Value * 2m : Math.Round(miles.Value / 2m, 1, MidpointRounding.AwayFromZero);
        var newMinutes = toRoundTrip ? minutes * 2 : (int)Math.Round(minutes / 2m, MidpointRounding.AwayFromZero);
        _filling = true;
        try
        {
            ManualMilesText = Parse.Text(newMiles);
            (ManualDriveHoursText, ManualDriveMinutesText) = Parse.Split(newMinutes);
        }
        finally { _filling = false; }
        _manualTotalTripType = TripType;
        LookupMessage = toRoundTrip
            ? "Trip totals were doubled for the round trip. Adjust them if the way back differs."
            : "Trip totals were halved for the one-way trip. Adjust them if needed.";
        Recalculate();
    }

    partial void OnManualIsTripTotalChanged(bool value)
    {
        _manualTotalTripType = TripType;
        OnPropertyChanged(nameof(ManualMilesLabel));
        OnPropertyChanged(nameof(ManualDriveLabel));
        // Looked-up legs are re-entered in the new basis rather than reinterpreted.
        if (_copied is not null && !_suspendRecalc) FillFromLookup(_copied with { });
    }

    partial void OnManualMilesTextChanged(string value) => ManualFiguresEdited();
    partial void OnManualDriveHoursTextChanged(string value) => ManualFiguresEdited();
    partial void OnManualDriveMinutesTextChanged(string value) => ManualFiguresEdited();

    private void ManualFiguresEdited()
    {
        if (_suspendRecalc || _filling) return;
        if (ManualIsTripTotal) _manualTotalTripType = TripType;
        // Staff typed over the looked-up figures: they are now plain manual entries.
        if (_copied is { } c && (c.Miles != ManualMilesText || c.Hours != ManualDriveHoursText || c.Minutes != ManualDriveMinutesText)) _copied = null;
    }

    // ---------------- address and route ----------------
    private string? _placeProvider;

    [RelayCommand]
    private async Task SearchAddressAsync()
    {
        LookupMessage = "";
        Candidates.Clear();
        if (string.IsNullOrWhiteSpace(DestinationQuery)) { LookupMessage = "Type an address or place name first."; return; }
        try
        {
            IsBusy = true;
            var result = await _integrations.Places.SearchAsync(DestinationQuery).ConfigureAwait(true);
            _placeProvider = result.ProviderName;
            foreach (var c in result.Candidates) Candidates.Add(c);
            LookupMessage = Candidates.Count == 0 ? "No matches. Check the spelling or type the full address." : "Pick the correct full address, then Confirm match.";
        }
        catch (ProviderUnavailableException ex) { LookupMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Copies the chosen address into the editable job-address field and, when route lookup is available, looks up
    /// the miles and drive time (both directions for a round trip) straight into the manual boxes.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmCandidateAsync()
    {
        if (SelectedCandidate is null) { LookupMessage = "Select a match first."; return; }
        DestinationAddress = SelectedCandidate.FormattedAddress;
        LookupMessage = $"Confirmed: {DestinationAddress}";
        Candidates.Clear();
        if (CanLookUpRoute) await RunRouteLookupAsync().ConfigureAwait(true);
    }

    /// <summary>Use what staff typed as the job address; looks up the route too when available.</summary>
    [RelayCommand]
    private async Task UseTypedAddressAsync()
    {
        DestinationAddress = DestinationQuery.Trim();
        LookupMessage = string.IsNullOrWhiteSpace(DestinationAddress) ? "" : "Using the address as typed.";
        if (CanLookUpRoute && !string.IsNullOrWhiteSpace(DestinationAddress)) await RunRouteLookupAsync().ConfigureAwait(true);
    }

    /// <summary>Looks up the route and copies the miles and drive time into the manual-entry boxes.</summary>
    [RelayCommand]
    private async Task RunRouteLookupAsync()
    {
        var destination = EffectiveDestination;
        if (string.IsNullOrWhiteSpace(OriginAddress) || string.IsNullOrWhiteSpace(destination))
        {
            LookupMessage = "Enter both a starting address and a job address to look up the route.";
            return;
        }
        try
        {
            IsBusy = true;
            var r = await _integrations.Routing.GetRouteAsync(OriginAddress, destination, TripType).ConfigureAwait(true);
            var provider = string.IsNullOrWhiteSpace(r.ProviderName) ? _integrations.Routing.Info.Name : r.ProviderName;
            FillFromLookup(new CopiedRoute(provider, CurrentKey, r.Outbound, r.Return, TripType));
            LookupMessage = (TripType == TripType.RoundTrip && r.Return is null
                ? "Route found, but the return leg couldn't be looked up, so the outbound leg was doubled. "
                : "Route found. ") + "Miles and drive time were copied into the boxes below; adjust them if needed.";
        }
        catch (ProviderUnavailableException ex) { LookupMessage = ex.Message + " Enter miles and drive time manually."; }
        finally { IsBusy = false; }
        Recalculate();
    }

    private void FillFromLookup(CopiedRoute c)
    {
        var ret = c.TripType == TripType.RoundTrip ? c.Return ?? c.Outbound : null;
        decimal miles;
        int minutes;
        if (ManualIsTripTotal)
        {
            miles = c.Outbound.Miles + (ret?.Miles ?? 0m);
            minutes = c.Outbound.DriveMinutes + (ret?.DriveMinutes ?? 0);
            _manualTotalTripType = c.TripType;
        }
        else if (ret is not null)
        {
            // Per leg: the average of the two directions, so the trip total matches the lookup (within rounding).
            miles = Math.Round((c.Outbound.Miles + ret.Miles) / 2m, 1, MidpointRounding.AwayFromZero);
            minutes = (int)Math.Round((c.Outbound.DriveMinutes + ret.DriveMinutes) / 2m, MidpointRounding.AwayFromZero);
        }
        else
        {
            miles = c.Outbound.Miles;
            minutes = c.Outbound.DriveMinutes;
        }
        _filling = true;
        try
        {
            ManualMilesText = Parse.Text(miles);
            (ManualDriveHoursText, ManualDriveMinutesText) = Parse.Split(minutes);
        }
        finally { _filling = false; }
        _copied = c with { Miles = ManualMilesText, Hours = ManualDriveHoursText, Minutes = ManualDriveMinutesText };
        Recalculate();
    }

    /// <summary>Keep the existing figures even though origin, destination or trip type changed.</summary>
    [RelayCommand]
    private void ConfirmRouteFigures()
    {
        if (_copied is not null) _copied = _copied with { Key = CurrentKey, TripType = TripType };
        if (ManualIsTripTotal) _manualTotalTripType = TripType;
        Recalculate();
    }

    // ---------------- fuel ----------------
    /// <summary>
    /// Gets today's average for regular and diesel in the shop's state. The service calls the source at most once per
    /// fuel type and state per day (and retries a failure at most hourly), so this is cheap to call often.
    /// </summary>
    public async Task RefreshFuelPricesAsync()
    {
        var st = FuelState;
        _fuelDate = DateOnly.FromDateTime(_services.Clock.GetLocalNow().DateTime);
        if (FuelInfo.Status == ProviderStatus.Available)
        {
            foreach (var t in Enum.GetValues<FuelType>())
            {
                try { _fuel[(t, st)] = await _fuelService.GetAsync(t, st).ConfigureAwait(true); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException) { _fuel[(t, st)] = new FuelPriceLookup { Error = ex.Message }; }
            }
        }
        Recalculate();
    }

    /// <summary>
    /// State whose fuel average applies: the state in the shop's starting address (Settings → Company), or
    /// Mississippi when that address doesn't name a state.
    /// </summary>
    public string FuelState => UsStates.FindInAddress(_company.OriginAddress) ?? UsStates.Default;

    /// <summary>Timer tick: refresh on a new local day, or when a fuel type has no current price yet.</summary>
    public async Task CheckFuelPricesAsync()
    {
        var today = DateOnly.FromDateTime(_services.Clock.GetLocalNow().DateTime);
        var missing = FuelInfo.Status == ProviderStatus.Available &&
                      Enum.GetValues<FuelType>().Any(t => !_fuel.TryGetValue((t, FuelState), out var l) || l.Error is not null || l.Price is null);
        if (_fuelDate != today || missing) await RefreshFuelPricesAsync().ConfigureAwait(true);
    }

    // ---------------- other costs ----------------
    [RelayCommand]
    private void AddOtherCost() => OtherCosts.Add(new DirectCostRow());

    [RelayCommand]
    private void RemoveOtherCost(DirectCostRow? row)
    {
        if (row is not null) OtherCosts.Remove(row);
    }

    // ---------------- copy and save ----------------
    [RelayCommand]
    private void CopyCostEstimate()
    {
        if (!Result.IsCostComplete) { StatusMessage = "Cost estimate is incomplete: " + string.Join("; ", Result.CostIssues); return; }
        _clipboard.SetText(EstimatePreview);
        StatusMessage = "Cost estimate copied to the clipboard.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Result.IsCostComplete) { StatusMessage = "Cannot save: " + string.Join("; ", Result.CostIssues); return; }
        if (_savedInput is not null && ReferenceEquals(_savedInput, _lastInput))
        {
            StatusMessage = $"No changes since {(_editing is { } e ? e.Number : "the last save")} was saved.";
            return;
        }
        try
        {
            var now = _services.Clock.GetLocalNow();
            EstimateSnapshot saved;
            if (_editing is { } editing)
            {
                var existing = await _services.Estimates.GetAsync(editing.Id).ConfigureAwait(true)
                               ?? throw new InvalidOperationException("The estimate being edited no longer exists. Start a new estimate.");
                var text = CostEstimateFormatter.Format(_lastInput, Result, existing.QuoteNumber, DateOnly.FromDateTime(existing.CreatedAt.DateTime), CustomerName);
                saved = await _services.Estimates.UpdateAsync(existing with
                {
                    CustomerName = CustomerName.Trim(),
                    Input = _lastInput,
                    Result = Result,
                    EstimateText = text,
                    SchemaVersion = EstimateSnapshot.CurrentSchemaVersion,
                    UpdatedAt = now,
                }).ConfigureAwait(true);
                StatusMessage = $"Saved changes to {saved.QuoteNumber} (version {saved.Revision}; the previous version is kept).";
            }
            else
            {
                var number = await _services.Estimates.NextQuoteNumberAsync(DateOnly.FromDateTime(now.DateTime)).ConfigureAwait(true);
                var text = CostEstimateFormatter.Format(_lastInput, Result, number, DateOnly.FromDateTime(now.DateTime), CustomerName);
                saved = await _services.Estimates.SaveNewAsync(new EstimateSnapshot
                {
                    QuoteNumber = number,
                    CreatedAt = now,
                    CustomerName = CustomerName.Trim(),
                    Input = _lastInput,
                    Result = Result,
                    EstimateText = text,
                }).ConfigureAwait(true);
                StatusMessage = $"Saved estimate {number}. Further saves update it; click New estimate to start another.";
            }
            EstimatePreview = saved.EstimateText;
            SetEditing((saved.Id, saved.QuoteNumber));
            _savedInput = _lastInput;
            EstimateSaved?.Invoke(this, saved);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never fail silently: show why the save didn't happen.
            StatusMessage = "Not saved: " + ex.Message;
        }
    }

    // ---------------- calculation ----------------
    private string EffectiveDestination => string.IsNullOrWhiteSpace(DestinationAddress) ? DestinationQuery : DestinationAddress;
    private RouteKey CurrentKey => new(OriginAddress, EffectiveDestination, TripType);

    private static string Age(TimeSpan? age) => age is not { } a ? "unknown"
        : a.TotalDays >= 1 ? $"{(int)a.TotalDays} day(s)"
        : a.TotalHours >= 1 ? $"{(int)a.TotalHours} hour(s)" : $"{(int)a.TotalMinutes} minute(s)";

    public void Recalculate()
    {
        if (_suspendRecalc) return;
        var inputErrors = new List<string>();

        RouteFigures? route = null;
        try
        {
            var miles = Parse.OptionalDecimal(ManualMilesText, "Miles");
            var hasTime = !string.IsNullOrWhiteSpace(ManualDriveHoursText) || !string.IsNullOrWhiteSpace(ManualDriveMinutesText);
            if (miles is { } m && hasTime)
            {
                var minutes = Parse.Minutes(ManualDriveHoursText, ManualDriveMinutesText, "Drive time");
                var key = _copied?.Key ?? CurrentKey;
                var trip = _copied?.TripType ?? TripType;
                route = ManualIsTripTotal
                    ? RouteFigures.ManualTripTotal(_copied is null ? _manualTotalTripType : trip, m, minutes, key)
                    : RouteFigures.ManualPerLeg(_copied is null ? TripType : trip, m, minutes, key);
                if (_copied is { } c)
                    route = route with
                    {
                        CopiedFromLookupProvider = c.Provider,
                        ReturnEstimatedByDoubling = c.TripType == TripType.RoundTrip && c.Return is null,
                    };
            }
        }
        catch (FormatException ex) { inputErrors.Add(ex.Message); }

        var installMinutes = 0;
        try { installMinutes = Parse.Minutes(InstallHoursText, InstallMinutesText, "Installation"); }
        catch (FormatException ex) { inputErrors.Add(ex.Message); }

        decimal? paidOverride = null;
        try { paidOverride = Parse.OptionalDecimal(PaidHoursOverrideText, "Paid hours override"); }
        catch (FormatException ex) { inputErrors.Add(ex.Message); }

        var vehicleFuel = SelectedVehicle?.FuelType ?? FuelType.RegularUnleaded;
        var fuelState = FuelState;
        _fuel.TryGetValue((vehicleFuel, fuelState), out var fuelLookup);
        FuelPrice? fuel = null;
        try
        {
            if (Parse.OptionalDecimal(ManualFuelPriceText, "Fuel price") is { } manual)
                fuel = FuelPriceService.Manual(vehicleFuel, manual, _services.Clock.GetLocalNow());
            else if (_pinnedFuel is { } pinned && pinned.FuelType == vehicleFuel) fuel = pinned;
            else if (fuelLookup?.Price is { } p && p.FuelType == vehicleFuel) fuel = p;
        }
        catch (FormatException ex) { inputErrors.Add(ex.Message); }

        var costs = new List<DirectCost>();
        foreach (var row in OtherCosts)
        {
            try { costs.Add(row.ToModel()); }
            catch (FormatException ex) { inputErrors.Add(ex.Message); }
        }

        var input = new EstimateInput
        {
            Team = SelectedTeam?.ToSnapshot(),
            Vehicle = SelectedVehicle?.ToSnapshot(),
            VehicleOverridden = SelectedVehicle is not null && SelectedTeam is not null && SelectedVehicle.Id != SelectedTeam.DefaultVehicleId,
            OriginAddress = OriginAddress.Trim(),
            DestinationQuery = DestinationQuery.Trim(),
            DestinationAddress = DestinationAddress.Trim(),
            TripType = TripType,
            Route = route,
            InstallMinutes = installMinutes,
            RoundLaborHoursUp = _company.RoundLaborHoursUp,
            Fuel = fuel,
            PaidHoursOverride = paidOverride,
            OtherDirectCosts = costs,
            Notes = Notes,
        };
        _lastInput = input;

        var result = EstimateCalculator.Calculate(input);
        if (inputErrors.Count > 0)
            result = result with { CostIssues = inputErrors.Concat(result.CostIssues).Distinct().ToList(), TotalDirectCost = null };
        Result = result;

        CostLines = result.CostLines.Select(l => new LineView(l.Label, l.Detail, l.Amount is { } a ? Fmt.Money(a) : "Incomplete")).ToList();
        CostIssues = result.CostIssues;
        CostNotes = result.Notes;
        TotalCostText = result.TotalDirectCost is { } t ? Fmt.Money(t) : "Incomplete";
        EstimatePreview = result.IsCostComplete
            ? CostEstimateFormatter.Format(input, result, _editing?.Number, DateOnly.FromDateTime(_services.Clock.GetLocalNow().DateTime), CustomerName)
            : "";
        LaborHoursText = result.PaidHoursOverridden
            ? $"Paid hours override: {Fmt.Number(result.PaidHours, "0.00")} h (instead of {Fmt.Number(result.TravelHoursCosted + result.InstallHoursCosted, "0.##")} h)"
            : $"Paid hours: {Fmt.Number(result.PaidHours, "0.##")} h ({Fmt.Number(result.TravelHoursCosted, "0.##")} travel + {Fmt.Number(result.InstallHoursCosted, "0.##")} installation)" +
              (result.LaborHoursRoundedUp ? ", each rounded up to whole hours (company setting)" : "");

        TripSummary = route is null ? "No miles or drive time yet."
            : $"{TripType.Label()}: {Fmt.Miles(result.TotalMiles)} total, {Fmt.Duration(result.TotalDriveMinutes)} total driving";
        RouteIsStale = result.RouteValidity == RouteValidity.Stale;
        OnPropertyChanged(nameof(ShowRerunRouteLookup));
        RouteSourceText = route is null ? "" : route.ProvenanceLabel + (RouteIsStale ? " — route details changed since the lookup." : "");

        TeamRatesText = SelectedTeam is { } team
            ? $"{team.Description}  ·  Loaded labor cost {(team.LoadedLaborCostPerHour is { } lc ? Fmt.Money(lc) + "/hr" : "not set")}"
            : "Select a team.";
        VehicleOverridden = input.VehicleOverridden;
        VehicleText = SelectedVehicle is { } v
            ? $"{v.FuelType.Label()} · {(v.EstimatedMpg is { } mpg ? Fmt.Number(mpg) + " MPG" : "MPG not set")} · operating cost {(v.OperatingCostPerMileExcludingFuel is { } op ? Fmt.Rate(op) + "/mi" : "not set")}{(VehicleOverridden ? " · overriding team default for this estimate" : "")}"
            : "No vehicle selected.";

        var fuelError = fuelLookup?.Error;
        if (fuel is null)
        {
            FuelIsStaleOrMissing = true;
            FuelStatusText = (fuelError is { } err ? $"Fuel price unavailable: {err} " : "") +
                $"No {vehicleFuel.Label().ToLowerInvariant()} price for {UsStates.Name(fuelState)} yet. " +
                (FuelInfo.Status == ProviderStatus.Available ? "Type a price to use one now." : FuelInfo.StatusMessage);
        }
        else
        {
            var age = _services.Clock.GetLocalNow() - fuel.RetrievedAt;
            var isFallback = !fuel.IsManualOverride && ReferenceEquals(fuel, fuelLookup?.Price) && fuelLookup!.IsLastKnownFallback;
            FuelIsStaleOrMissing = !fuel.IsManualOverride && (isFallback || age.TotalDays >= 1);
            FuelStatusText = fuel.Describe()
                + (fuel.IsManualOverride ? "" : ReferenceEquals(fuel, _pinnedFuel) ? "; saved with this estimate" : $"; retrieved {Age(age)} ago")
                + (isFallback && fuelError is { } err ? $". Source unavailable ({err}); showing the last known value, {Age(age)} old." : "")
                + (fuel.IsLive || fuel.IsManualOverride ? "" : " — NOT LIVE DATA");
        }
    }
}
