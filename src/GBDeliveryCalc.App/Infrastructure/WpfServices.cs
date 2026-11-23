using System.Windows;
using GBDeliveryCalc.Data;
using GBDeliveryCalc.Presentation;
using Microsoft.Win32;

namespace GBDeliveryCalc.App.Infrastructure;

/// <summary>Clipboard access for the view models.</summary>
public sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text) => Clipboard.SetText(text);
}

/// <summary>File pickers and message boxes for the view models.</summary>
public sealed class WpfDialogService : IDialogService
{
    private const string Filter = "Estimator backup (*.estimator-backup)|*.estimator-backup|All files (*.*)|*.*";

    public string? PickBackupSavePath(string suggestedFileName)
    {
        var dlg = new SaveFileDialog { FileName = suggestedFileName, Filter = Filter, InitialDirectory = AppDataPaths.BackupDirectory };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? PickBackupToRestore()
    {
        var dlg = new OpenFileDialog { Filter = Filter, InitialDirectory = AppDataPaths.BackupDirectory };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public void ShowError(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    private const string KeysFilter = "API key backup (*.estimator-keys)|*.estimator-keys|All files (*.*)|*.*";

    public string? PickKeysExportPath(string suggestedFileName)
    {
        var dlg = new SaveFileDialog { FileName = suggestedFileName, Filter = KeysFilter };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? PickKeysImportPath()
    {
        var dlg = new OpenFileDialog { Filter = KeysFilter };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? AskPassword(string title, string prompt, bool confirm)
    {
        var window = new Views.PasswordWindow(title, prompt, confirm) { Owner = Application.Current?.MainWindow };
        return window.ShowDialog() == true ? window.Password : null;
    }
}

/// <summary>Backup and restore of the SQLite database file.</summary>
public sealed class SqliteMaintenance(SqliteDatabase database) : IDatabaseMaintenance
{
    public string DatabasePath => database.DatabasePath;
    public string BackupDirectory => AppDataPaths.BackupDirectory;

    public Task BackupAsync(string destinationPath) =>
        Task.Run(() => DatabaseBackup.BackupTo(database.DatabasePath, destinationPath));

    public async Task<string> RestoreAsync(string backupPath)
    {
        var safety = await Task.Run(() => DatabaseBackup.RestoreFrom(backupPath, database.DatabasePath, BackupDirectory));
        await database.MigrateAsync(); // upgrades backups taken by older versions
        return safety;
    }
}
