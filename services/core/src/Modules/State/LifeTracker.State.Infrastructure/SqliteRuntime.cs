using Microsoft.Data.Sqlite;

namespace LifeTracker.State.Infrastructure;

public static class SqliteRuntime
{
    public static readonly Version MinimumVersion = new(3, 51, 3);

    public static void RequireSupportedVersion(string version)
    {
        if (!Version.TryParse(version, out var parsed) || parsed < MinimumVersion)
            throw new NotSupportedException("SQLite runtime is below the supported version floor.");
    }

    public static async Task<string> QualifyAsync(SqliteConnection connection, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        var version = (string)(await command.ExecuteScalarAsync(ct))!;
        RequireSupportedVersion(version);
        command.CommandText = "SELECT json_valid('{}') AND sqlite_compileoption_used('ENABLE_FTS5');";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) != 1)
            throw new NotSupportedException("SQLite requires JSON and FTS5 support.");
        return version;
    }
}
