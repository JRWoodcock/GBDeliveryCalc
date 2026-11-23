using GBDeliveryCalc.Core.Integrations;
using GBDeliveryCalc.Core.Model;
using GBDeliveryCalc.Core.Persistence;
using GBDeliveryCalc.Services;

namespace GBDeliveryCalc.Presentation;

/// <summary>Clipboard access (implemented by the desktop app).</summary>
public interface IClipboardService
{
    void SetText(string text);
}

/// <summary>Colour theme choice in Settings → Appearance.</summary>
public enum AppTheme
{
    FollowWindows = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Applies and remembers the colour theme (implemented by the desktop app).</summary>
public interface IThemeService
{
    AppTheme Current { get; }
    void Apply(AppTheme theme);
}

/// <summary>File pickers and message boxes (implemented by the desktop app).</summary>
public interface IDialogService
{
    string? PickBackupSavePath(string suggestedFileName);
    string? PickBackupToRestore();
    bool Confirm(string title, string message);
    void ShowError(string title, string message);

    /// <summary>Save-file picker for an API key backup (<see cref="KeyBackup.Extension"/>).</summary>
    string? PickKeysExportPath(string suggestedFileName);

    /// <summary>Open-file picker for an API key backup.</summary>
    string? PickKeysImportPath();

    /// <summary>Asks for a password (twice when <paramref name="confirm"/> is true). Null when cancelled.</summary>
    string? AskPassword(string title, string prompt, bool confirm);
}

/// <summary>Database maintenance operations exposed to Settings (implemented over SQLite in the app).</summary>
public interface IDatabaseMaintenance
{
    string DatabasePath { get; }
    string BackupDirectory { get; }
    Task BackupAsync(string destinationPath);

    /// <summary>Returns the path of the safety copy taken before restoring.</summary>
    Task<string> RestoreAsync(string backupPath);
}

/// <summary>
/// Small per-user secret storage (the app encrypts with Windows DPAPI). Kept outside the app's database,
/// so backups and restores never contain secrets.
/// </summary>
public interface ISecretStore
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
}

/// <summary>Names of the secrets kept in <see cref="ISecretStore"/>.</summary>
public static class SecretNames
{
    /// <summary>Google Maps Platform API key used for address and route lookups.</summary>
    public const string GoogleMapsApiKey = "google-key";

    /// <summary>OilPriceAPI key used for state average fuel prices.</summary>
    public const string OilPriceApiKey = "oilpriceapi-key";
}

/// <summary>Everything the view models need, so tests can supply in-memory or temp-file implementations.</summary>
public sealed record EstimatorServices(
    ISettingsStore Settings,
    IEstimateStore Estimates,
    IFuelPriceCache FuelCache,
    IDatabaseMaintenance Database,
    ISecretStore Secrets,
    Func<IntegrationKeys, IntegrationSet> Integrations,
    TimeProvider Clock)
{
    /// <summary>Builds the providers from the keys currently saved on this PC (either may be missing).</summary>
    public IntegrationSet CreateIntegrations() =>
        Integrations(new IntegrationKeys(Secrets.Get(SecretNames.GoogleMapsApiKey), Secrets.Get(SecretNames.OilPriceApiKey)));
}
