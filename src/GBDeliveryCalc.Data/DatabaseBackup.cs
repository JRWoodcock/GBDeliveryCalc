using Microsoft.Data.Sqlite;

namespace GBDeliveryCalc.Data;

/// <summary>
/// Online backup and restore using SQLite's backup API, so a consistent copy is taken even while the app is open.
/// </summary>
public static class DatabaseBackup
{
    public const string BackupExtension = ".estimator-backup";

    /// <summary>Writes a consistent copy of the database to <paramref name="destinationPath"/>.</summary>
    public static void BackupTo(string databasePath, string destinationPath)
    {
        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        using var source = new SqliteConnection(ConnectionString(databasePath, SqliteOpenMode.ReadOnly, pooling: true));
        using var dest = new SqliteConnection(ConnectionString(destinationPath, SqliteOpenMode.ReadWriteCreate));
        source.Open();
        dest.Open();
        source.BackupDatabase(dest);
        SqliteConnection.ClearPool(dest);
    }

    /// <summary>
    /// Replaces the live database with a backup file. A safety copy of the current database is written first
    /// and its path returned. Run <see cref="SqliteDatabase.MigrateAsync"/> afterwards to upgrade older backups.
    /// </summary>
    public static string RestoreFrom(string backupPath, string databasePath, string safetyCopyDirectory)
    {
        if (!File.Exists(backupPath)) throw new FileNotFoundException("Backup file not found.", backupPath);
        Validate(backupPath);

        SqliteConnection.ClearAllPools();
        var safety = Path.Combine(safetyCopyDirectory, $"pre-restore-{DateTime.Now:yyyyMMdd-HHmmss}{BackupExtension}");
        if (File.Exists(databasePath)) BackupTo(databasePath, safety);

        using (var source = new SqliteConnection(ConnectionString(backupPath, SqliteOpenMode.ReadOnly)))
        using (var dest = new SqliteConnection(ConnectionString(databasePath, SqliteOpenMode.ReadWriteCreate)))
        {
            source.Open();
            dest.Open();
            source.BackupDatabase(dest);
        }
        SqliteConnection.ClearAllPools();
        return safety;
    }

    /// <summary>Builds a connection string; the builder escapes paths, so a file name can't add connection options.</summary>
    internal static string ConnectionString(string path, SqliteOpenMode mode, bool pooling = false) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = pooling }.ToString();

    /// <summary>Checks the file is a SQLite database created by this app.</summary>
    public static void Validate(string path)
    {
        try
        {
            using var c = new SqliteConnection(ConnectionString(path, SqliteOpenMode.ReadOnly));
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('__EFMigrationsHistory','Estimates','Teams')";
            var n = Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            if (n < 3) throw new InvalidDataException("The file is not a GB DeliveryCalc backup.");
            using var check = c.CreateCommand();
            check.CommandText = "PRAGMA integrity_check";
            var result = check.ExecuteScalar() as string;
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Backup failed the SQLite integrity check: " + result);
        }
        catch (SqliteException ex)
        {
            throw new InvalidDataException("The file is not a readable SQLite database.", ex);
        }
    }
}
