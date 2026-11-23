using GBDeliveryCalc.Data;
using Microsoft.Data.Sqlite;

namespace GBDeliveryCalc.Tests;

/// <summary>A migrated SQLite database in a unique temp folder, deleted on dispose.</summary>
internal sealed class TempDatabase : IDisposable
{
    public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "grover-tests-" + Guid.NewGuid().ToString("N"));
    public string Path => System.IO.Path.Combine(Directory, "estimator.db");
    public SqliteDatabase Database { get; }

    private TempDatabase()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Database = new SqliteDatabase(Path);
    }

    public static async Task<TempDatabase> CreateAsync()
    {
        var t = new TempDatabase();
        await t.Database.MigrateAsync();
        return t;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { System.IO.Directory.Delete(Directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
