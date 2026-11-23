namespace GBDeliveryCalc.Data;

/// <summary>
/// App data lives outside the installation directory, in the user's local app data folder.
/// Under MSIX, Windows redirects this to the package's private LocalCache folder automatically.
/// Override with the GROVER_ESTIMATOR_DATA_DIR environment variable (used by tests and support).
/// </summary>
public static class AppDataPaths
{
    public const string DataDirEnvVar = "GROVER_ESTIMATOR_DATA_DIR";

    public static string DataDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(DataDirEnvVar);
            var dir = !string.IsNullOrWhiteSpace(overridden)
                ? overridden
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GroverBrothers", "Estimator");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DatabasePath => Path.Combine(DataDirectory, "estimator.db");
    public static string BackupDirectory => Directory.CreateDirectory(Path.Combine(DataDirectory, "Backups")).FullName;
}
