using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GBDeliveryCalc.Core.Calculation;
using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Core.Model;
using GBDeliveryCalc.Services;

namespace GBDeliveryCalc.Presentation;

/// <summary>Option for the default-vehicle picker (includes "None").</summary>
public sealed record VehicleOption(Guid? Id, string Label);

/// <summary>Settings tab: company settings, teams, vehicles, backup and restore, API keys and appearance.</summary>
public sealed partial class SettingsViewModel(EstimatorServices services, IDialogService dialogs, IThemeService? theme = null) : ObservableObject
{
    /// <summary>Raised after any settings change so the estimate screen reloads its lists and rates.</summary>
    public event EventHandler? SettingsChanged;

    public ObservableCollection<TeamProfile> Teams { get; } = [];
    public ObservableCollection<VehicleProfile> Vehicles { get; } = [];
    public ObservableCollection<VehicleOption> VehicleOptions { get; } = [];
    public IReadOnlyList<FuelType> FuelTypes { get; } = Enum.GetValues<FuelType>();

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    /// <summary>Settings → Appearance. Applied immediately and remembered for this Windows user.</summary>
    [ObservableProperty] public partial AppTheme Appearance { get; set; } = theme?.Current ?? AppTheme.FollowWindows;

    partial void OnAppearanceChanged(AppTheme value)
    {
        if (theme is not null && theme.Current != value) theme.Apply(value);
    }

    [ObservableProperty] public partial bool ShowArchived { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";

    // company
    [ObservableProperty] public partial string OriginAddress { get; set; } = "";

    /// <summary>Google address matches for the starting address (Look up), confirmed like the job address.</summary>
    public ObservableCollection<PlaceCandidate> OriginCandidates { get; } = [];
    [ObservableProperty] public partial PlaceCandidate? SelectedOriginCandidate { get; set; }
    [ObservableProperty] public partial string OriginLookupMessage { get; private set; } = "";
    [ObservableProperty] public partial bool OriginVerified { get; private set; }
    private string? _originProvider;
    private bool _settingOrigin;

    /// <summary>Google requires "Powered by Google" next to Places results shown without a Google map.</summary>
    public bool ShowOriginPoweredByGoogle => OriginCandidates.Count > 0 &&
        (_originProvider ?? "").StartsWith("Google", StringComparison.OrdinalIgnoreCase);

    partial void OnOriginAddressChanged(string value)
    {
        // Typing over a confirmed address means it is no longer the verified one.
        if (!_settingOrigin) OriginVerified = false;
    }
    [ObservableProperty] public partial string DefaultInstallHoursText { get; set; } = "3";
    [ObservableProperty] public partial string DefaultInstallMinutesText { get; set; } = "0";
    [ObservableProperty] public partial bool RoundLaborHoursUp { get; set; }

    // Google lookups. The saved key is never read back into the UI; only whether one exists.
    [ObservableProperty] public partial bool HasGoogleKey { get; private set; }
    [ObservableProperty] public partial string GoogleKeyMessage { get; private set; } = "";
    public string GoogleKeyStatus => HasGoogleKey ? "Key saved" : "No key";
    public string GoogleSaveButtonText => HasGoogleKey ? "Save / replace key" : "Save key";
    partial void OnHasGoogleKeyChanged(bool value)
    {
        OnPropertyChanged(nameof(GoogleKeyStatus));
        OnPropertyChanged(nameof(GoogleSaveButtonText));
    }

    // Fuel prices (OilPriceAPI). Same rules: the saved key is never read back into the UI.
    [ObservableProperty] public partial bool HasFuelKey { get; private set; }
    [ObservableProperty] public partial string FuelKeyMessage { get; private set; } = "";
    public string FuelKeyStatus => HasFuelKey ? "Key saved" : "No key";
    public string FuelSaveButtonText => HasFuelKey ? "Save / replace key" : "Save key";
    partial void OnHasFuelKeyChanged(bool value)
    {
        OnPropertyChanged(nameof(FuelKeyStatus));
        OnPropertyChanged(nameof(FuelSaveButtonText));
    }

    // team editor
    [ObservableProperty] public partial TeamProfile? SelectedTeam { get; set; }
    [ObservableProperty] public partial string TeamName { get; set; } = "";
    [ObservableProperty] public partial string TeamDescription { get; set; } = "";
    [ObservableProperty] public partial string TeamLoadedCostText { get; set; } = "";
    [ObservableProperty] public partial VehicleOption? TeamDefaultVehicle { get; set; }
    [ObservableProperty] public partial bool TeamIsArchived { get; private set; }
    private Guid? _editingTeamId;

    // vehicle editor
    [ObservableProperty] public partial VehicleProfile? SelectedVehicle { get; set; }
    [ObservableProperty] public partial string VehicleName { get; set; } = "";
    [ObservableProperty] public partial FuelType VehicleFuelType { get; set; }
    [ObservableProperty] public partial string VehicleMpgText { get; set; } = "";
    [ObservableProperty] public partial string VehicleOperatingCostText { get; set; } = "";
    [ObservableProperty] public partial bool VehicleIsArchived { get; private set; }
    private Guid? _editingVehicleId;

    public string DatabasePath => services.Database.DatabasePath;
    public string BackupDirectory => services.Database.BackupDirectory;

    public async Task LoadAsync()
    {
        var c = await services.Settings.GetCompanySettingsAsync().ConfigureAwait(true);
        OriginAddress = c.OriginAddress;
        (DefaultInstallHoursText, DefaultInstallMinutesText) = Parse.Split(c.DefaultInstallMinutes);
        RoundLaborHoursUp = c.RoundLaborHoursUp;
        HasGoogleKey = !string.IsNullOrWhiteSpace(services.Secrets.Get(SecretNames.GoogleMapsApiKey));
        HasFuelKey = !string.IsNullOrWhiteSpace(services.Secrets.Get(SecretNames.OilPriceApiKey));
        await ReloadListsAsync().ConfigureAwait(true);
    }

    partial void OnShowArchivedChanged(bool value) => _ = ReloadListsAsync();

    private async Task ReloadListsAsync()
    {
        var keepTeam = _editingTeamId;
        var keepVehicle = _editingVehicleId;
        var allVehicles = await services.Settings.GetVehiclesAsync(true).ConfigureAwait(true);
        Vehicles.Clear();
        foreach (var v in allVehicles.Where(v => ShowArchived || !v.IsArchived)) Vehicles.Add(v);
        VehicleOptions.Clear();
        VehicleOptions.Add(new VehicleOption(null, "(none)"));
        foreach (var v in allVehicles) VehicleOptions.Add(new VehicleOption(v.Id, v.Name + (v.IsArchived ? " (archived)" : "")));

        Teams.Clear();
        foreach (var t in await services.Settings.GetTeamsAsync(ShowArchived).ConfigureAwait(true)) Teams.Add(t);
        SelectedTeam = Teams.FirstOrDefault(t => t.Id == keepTeam);
        SelectedVehicle = Vehicles.FirstOrDefault(v => v.Id == keepVehicle);
    }

    // ---------------- company ----------------
    /// <summary>Searches Google Places for the starting address, the same way New estimate checks the job address.</summary>
    [RelayCommand]
    private async Task SearchOriginAddressAsync()
    {
        OriginCandidates.Clear();
        OnPropertyChanged(nameof(ShowOriginPoweredByGoogle));
        if (string.IsNullOrWhiteSpace(OriginAddress)) { OriginLookupMessage = "Type the shop's address or name first."; return; }
        var places = services.CreateIntegrations().Places;
        if (places.Info.Status != ProviderStatus.Available)
        {
            OriginLookupMessage = "Address lookup isn't set up. Add a Google Maps Platform API key under API keys to verify the address.";
            return;
        }
        try
        {
            var result = await places.SearchAsync(OriginAddress).ConfigureAwait(true);
            _originProvider = result.ProviderName;
            foreach (var c in result.Candidates) OriginCandidates.Add(c);
            OriginLookupMessage = OriginCandidates.Count == 0
                ? "No matches. Check the spelling or type the full street address."
                : "Pick the correct full address, then Confirm match.";
        }
        catch (ProviderUnavailableException ex) { OriginLookupMessage = ex.Message; }
        OnPropertyChanged(nameof(ShowOriginPoweredByGoogle));
    }

    /// <summary>Uses the chosen Google address as the starting address and saves the company settings.</summary>
    [RelayCommand]
    private async Task ConfirmOriginAddressAsync()
    {
        if (SelectedOriginCandidate is not { } c) { OriginLookupMessage = "Select a match first."; return; }
        _settingOrigin = true;
        try { OriginAddress = c.FormattedAddress; }
        finally { _settingOrigin = false; }
        OriginCandidates.Clear();
        OnPropertyChanged(nameof(ShowOriginPoweredByGoogle));
        await SaveCompanyAsync().ConfigureAwait(true);
        OriginVerified = StatusMessage == "Company settings saved.";
        OriginLookupMessage = OriginVerified
            ? $"Verified by Google and saved: {c.FormattedAddress}"
            : StatusMessage;
    }

    [RelayCommand]
    private async Task SaveCompanyAsync()
    {
        try
        {
            var settings = new CompanySettings
            {
                OriginAddress = OriginAddress.Trim(),
                DefaultInstallMinutes = Parse.Minutes(DefaultInstallHoursText, DefaultInstallMinutesText, "Default installation"),
                RoundLaborHoursUp = RoundLaborHoursUp,
            };
            await services.Settings.SaveCompanySettingsAsync(settings).ConfigureAwait(true);
            StatusMessage = "Company settings saved.";
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { StatusMessage = "Not saved: " + ex.Message; }
    }

    // ---------------- Google lookups ----------------
    /// <summary>Text the Test button searches for: cheap, and always has a match.</summary>
    public const string TestQuery = "Hattiesburg, MS";

    /// <summary>Saves the key typed in the (masked) key box. Returns true when saved, so the view can clear the box.</summary>
    public bool SaveGoogleKey(string? typedKey)
    {
        var key = (typedKey ?? "").Trim();
        if (key.Length == 0) { GoogleKeyMessage = HasGoogleKey ? "Type or paste the new key to replace the saved one." : "Type or paste the key first."; return false; }
        var replacing = HasGoogleKey;
        if (key.Any(char.IsWhiteSpace)) { GoogleKeyMessage = "Not saved: an API key has no spaces. Copy it again from Google Cloud Console."; return false; }
        try { services.Secrets.Set(SecretNames.GoogleMapsApiKey, key); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { GoogleKeyMessage = "Not saved: the key could not be stored on this PC (" + ex.GetType().Name + ")."; return false; }
        HasGoogleKey = true;
        GoogleKeyMessage = (replacing ? "Key replaced" : "Key saved") + " for this Windows user. Click Test key to check it works.";
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Runs one small address search with the typed key, or the saved key when the box is empty.</summary>
    public async Task TestGoogleKeyAsync(string? typedKey)
    {
        var key = string.IsNullOrWhiteSpace(typedKey) ? services.Secrets.Get(SecretNames.GoogleMapsApiKey) : typedKey.Trim();
        if (string.IsNullOrWhiteSpace(key)) { GoogleKeyMessage = "No key to test. Paste one in the box or save one first."; return; }
        GoogleKeyMessage = "Testing…";
        try
        {
            var result = await services.Integrations(new IntegrationKeys(Google: key)).Places.SearchAsync(TestQuery).ConfigureAwait(true);
            GoogleKeyMessage = result.Candidates.Count > 0
                ? $"Test passed: Google found {result.Candidates[0].FormattedAddress}."
                : "Google answered but found no match for the test search. The key works for Places; try a route on New estimate.";
        }
        catch (ProviderUnavailableException ex) { GoogleKeyMessage = "Test failed. " + ex.Message; }
    }

    public void RemoveGoogleKey()
    {
        if (!HasGoogleKey) { GoogleKeyMessage = "No key is saved."; return; }
        if (!dialogs.Confirm("Remove Google key", "Remove the Google key from this PC? Address and route lookups turn off until a key is saved again."))
            return;
        try { services.Secrets.Delete(SecretNames.GoogleMapsApiKey); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { GoogleKeyMessage = "Not removed: " + ex.GetType().Name + "."; return; }
        HasGoogleKey = false;
        GoogleKeyMessage = "Key removed. Lookups are off; enter miles and drive time manually.";
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------- fuel prices (OilPriceAPI) ----------------
    /// <summary>Saves the key typed in the (masked) key box. Returns true when saved, so the view can clear the box.</summary>
    public Task<bool> SaveFuelKeyAsync(string? typedKey) =>
        StoreFuelKeyAsync(typedKey, (HasFuelKey ? "Key replaced" : "Key saved") + " for this Windows user. Click Test key to check it works.");

    private async Task<bool> StoreFuelKeyAsync(string? raw, string successMessage)
    {
        var key = (raw ?? "").Trim();
        if (key.Length == 0) { FuelKeyMessage = HasFuelKey ? "Type or paste the new key to replace the saved one." : "Type or paste the key first."; return false; }
        if (key.Any(char.IsWhiteSpace)) { FuelKeyMessage = "Not saved: an API key has no spaces. Copy it again from the OilPriceAPI dashboard."; return false; }
        try { services.Secrets.Set(SecretNames.OilPriceApiKey, key); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { FuelKeyMessage = "Not saved: the key could not be stored on this PC (" + ex.GetType().Name + ")."; return false; }
        HasFuelKey = true;
        FuelKeyMessage = successMessage;
        await services.FuelCache.ClearFetchStateAsync().ConfigureAwait(true); // a new key may fix a failure, so allow a call now
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Fetches today's regular price with the typed key, or the saved key when the box is empty.</summary>
    public async Task TestFuelKeyAsync(string? typedKey)
    {
        var key = string.IsNullOrWhiteSpace(typedKey) ? services.Secrets.Get(SecretNames.OilPriceApiKey) : typedKey.Trim();
        if (string.IsNullOrWhiteSpace(key)) { FuelKeyMessage = "No key to test. Paste one in the box or save one first."; return; }
        FuelKeyMessage = "Testing…";
        try
        {
            var price = await services.Integrations(new IntegrationKeys(OilPriceApi: key)).Fuel
                .GetStateAverageAsync(FuelType.RegularUnleaded, UsStates.FindInAddress(OriginAddress) ?? UsStates.Default).ConfigureAwait(true);
            FuelKeyMessage = $"Test passed: {price.Describe()}" + (price.IsLive ? "." : ". OilPriceAPI marks it as not current, so it won't be reused as today's price.");
        }
        catch (ProviderUnavailableException ex) { FuelKeyMessage = "Test failed. " + ex.Message; }
    }

    public async Task RemoveFuelKeyAsync()
    {
        if (!HasFuelKey) { FuelKeyMessage = "No key is saved."; return; }
        if (!dialogs.Confirm("Remove OilPriceAPI key", "Remove the OilPriceAPI key from this PC? Fuel prices must then be entered manually until a key is saved again."))
            return;
        try { services.Secrets.Delete(SecretNames.OilPriceApiKey); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { FuelKeyMessage = "Not removed: " + ex.GetType().Name + "."; return; }
        HasFuelKey = false;
        FuelKeyMessage = "Key removed. Enter fuel prices manually.";
        await services.FuelCache.ClearFetchStateAsync().ConfigureAwait(true);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------- teams ----------------
    partial void OnSelectedTeamChanged(TeamProfile? value)
    {
        if (value is null) return;
        _editingTeamId = value.Id;
        TeamName = value.Name;
        TeamDescription = value.Description;
        TeamLoadedCostText = Parse.Text(value.LoadedLaborCostPerHour);
        TeamDefaultVehicle = VehicleOptions.FirstOrDefault(o => o.Id == value.DefaultVehicleId) ?? VehicleOptions.FirstOrDefault();
        TeamIsArchived = value.IsArchived;
    }

    [RelayCommand]
    private void NewTeam()
    {
        SelectedTeam = null;
        _editingTeamId = null;
        TeamName = TeamDescription = TeamLoadedCostText = "";
        TeamDefaultVehicle = VehicleOptions.FirstOrDefault();
        TeamIsArchived = false;
    }

    [RelayCommand]
    private async Task SaveTeamAsync() => await SaveTeamCoreAsync(TeamIsArchived).ConfigureAwait(true);

    [RelayCommand]
    private async Task ToggleTeamArchivedAsync()
    {
        if (_editingTeamId is null) { StatusMessage = "Save the team first."; return; }
        await SaveTeamCoreAsync(!TeamIsArchived).ConfigureAwait(true);
    }

    private async Task SaveTeamCoreAsync(bool archived)
    {
        try
        {
            var team = new TeamProfile
            {
                Id = _editingTeamId ?? Guid.NewGuid(),
                Name = TeamName,
                Description = TeamDescription,
                LoadedLaborCostPerHour = Parse.OptionalDecimal(TeamLoadedCostText, "Loaded labor cost"),
                DefaultVehicleId = TeamDefaultVehicle?.Id,
                IsArchived = archived,
            };
            var saved = await services.Settings.SaveTeamAsync(team).ConfigureAwait(true);
            _editingTeamId = saved.Id;
            TeamIsArchived = saved.IsArchived;
            StatusMessage = $"Team '{saved.Name}' saved{(saved.IsArchived ? " (archived)" : "")}.";
            await ReloadListsAsync().ConfigureAwait(true);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { StatusMessage = "Not saved: " + ex.Message; }
    }

    // ---------------- vehicles ----------------
    partial void OnSelectedVehicleChanged(VehicleProfile? value)
    {
        if (value is null) return;
        _editingVehicleId = value.Id;
        VehicleName = value.Name;
        VehicleFuelType = value.FuelType;
        VehicleMpgText = Parse.Text(value.EstimatedMpg);
        VehicleOperatingCostText = Parse.Text(value.OperatingCostPerMileExcludingFuel);
        VehicleIsArchived = value.IsArchived;
    }

    [RelayCommand]
    private void NewVehicle()
    {
        SelectedVehicle = null;
        _editingVehicleId = null;
        VehicleName = VehicleMpgText = VehicleOperatingCostText = "";
        VehicleFuelType = FuelType.RegularUnleaded;
        VehicleIsArchived = false;
    }

    [RelayCommand]
    private async Task SaveVehicleAsync() => await SaveVehicleCoreAsync(VehicleIsArchived).ConfigureAwait(true);

    [RelayCommand]
    private async Task ToggleVehicleArchivedAsync()
    {
        if (_editingVehicleId is null) { StatusMessage = "Save the vehicle first."; return; }
        await SaveVehicleCoreAsync(!VehicleIsArchived).ConfigureAwait(true);
    }

    private async Task SaveVehicleCoreAsync(bool archived)
    {
        try
        {
            var vehicle = new VehicleProfile
            {
                Id = _editingVehicleId ?? Guid.NewGuid(),
                Name = VehicleName,
                FuelType = VehicleFuelType,
                EstimatedMpg = Parse.OptionalDecimal(VehicleMpgText, "MPG"),
                OperatingCostPerMileExcludingFuel = Parse.OptionalDecimal(VehicleOperatingCostText, "Operating cost per mile"),
                IsArchived = archived,
            };
            var saved = await services.Settings.SaveVehicleAsync(vehicle).ConfigureAwait(true);
            _editingVehicleId = saved.Id;
            VehicleIsArchived = saved.IsArchived;
            StatusMessage = $"Vehicle '{saved.Name}' saved{(saved.IsArchived ? " (archived)" : "")}.";
            await ReloadListsAsync().ConfigureAwait(true);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { StatusMessage = "Not saved: " + ex.Message; }
    }

    // ---------------- API key backup ----------------
    [ObservableProperty] public partial string KeyBackupMessage { get; private set; } = "";

    /// <summary>Saves both API keys to a password-protected file, for moving them to another PC.</summary>
    [RelayCommand]
    private async Task ExportKeysAsync()
    {
        var keys = new Dictionary<string, string>();
        foreach (var name in new[] { SecretNames.GoogleMapsApiKey, SecretNames.OilPriceApiKey })
            if (services.Secrets.Get(name) is { Length: > 0 } value) keys[name] = value;
        if (keys.Count == 0) { KeyBackupMessage = "No API keys are saved, so there's nothing to back up."; return; }

        var password = dialogs.AskPassword("Back up API keys",
            $"Choose a password for the key backup (at least {KeyBackup.MinimumPasswordLength} characters). You'll need it to restore the keys.", confirm: true);
        if (password is null) return;
        if (password.Length < KeyBackup.MinimumPasswordLength)
        {
            KeyBackupMessage = $"Not backed up: the password must be at least {KeyBackup.MinimumPasswordLength} characters.";
            return;
        }
        var path = dialogs.PickKeysExportPath($"grover-estimator-api-keys-{DateTime.Now:yyyyMMdd}{KeyBackup.Extension}");
        if (path is null) return;
        try
        {
            var bytes = await Task.Run(() => KeyBackup.Export(keys, password)).ConfigureAwait(true);
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(true);
            KeyBackupMessage = $"Backed up {keys.Count} API key(s) to {path}. Keep the password separately from the file.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            KeyBackupMessage = "Not backed up: " + ex.Message;
        }
    }

    /// <summary>Restores API keys from a key backup, replacing the keys saved on this PC.</summary>
    [RelayCommand]
    private async Task ImportKeysAsync()
    {
        var path = dialogs.PickKeysImportPath();
        if (path is null) return;
        var password = dialogs.AskPassword("Restore API keys", "Enter the password used when the keys were backed up.", confirm: false);
        if (password is null) return;
        IReadOnlyDictionary<string, string> keys;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
            keys = await Task.Run(() => KeyBackup.Import(bytes, password)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            KeyBackupMessage = "Not restored: " + ex.Message;
            return;
        }
        var known = keys.Where(k => k.Key is SecretNames.GoogleMapsApiKey or SecretNames.OilPriceApiKey && !string.IsNullOrWhiteSpace(k.Value)).ToList();
        if (known.Count == 0) { KeyBackupMessage = "Not restored: the backup contains no API keys."; return; }
        if ((HasGoogleKey || HasFuelKey) && !dialogs.Confirm("Restore API keys", "Replace the API keys saved on this PC with the ones in the backup?"))
            return;
        foreach (var (name, value) in known) services.Secrets.Set(name, value.Trim());
        HasGoogleKey = !string.IsNullOrWhiteSpace(services.Secrets.Get(SecretNames.GoogleMapsApiKey));
        HasFuelKey = !string.IsNullOrWhiteSpace(services.Secrets.Get(SecretNames.OilPriceApiKey));
        await services.FuelCache.ClearFetchStateAsync().ConfigureAwait(true);
        KeyBackupMessage = $"Restored {known.Count} API key(s). They're saved encrypted for this Windows user.";
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------- backup / restore ----------------
    [RelayCommand]
    private async Task BackupAsync()
    {
        var path = dialogs.PickBackupSavePath($"grover-estimator-{DateTime.Now:yyyyMMdd-HHmm}.estimator-backup");
        if (path is null) return;
        try
        {
            await services.Database.BackupAsync(path).ConfigureAwait(true);
            StatusMessage = $"Backup written to {path}";
        }
        catch (Exception ex) { dialogs.ShowError("Backup failed", ex.Message); }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        var path = dialogs.PickBackupToRestore();
        if (path is null) return;
        if (!dialogs.Confirm("Restore backup", "Replace ALL current teams, vehicles, settings and estimates with this backup? A safety copy of the current data is saved first."))
            return;
        try
        {
            var safety = await services.Database.RestoreAsync(path).ConfigureAwait(true);
            StatusMessage = $"Restored. Previous data saved to {safety}";
            await LoadAsync().ConfigureAwait(true);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { dialogs.ShowError("Restore failed", ex.Message); }
    }
}
