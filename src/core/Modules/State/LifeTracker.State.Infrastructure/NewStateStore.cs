using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LifeTracker.State.Infrastructure;

/// <summary>Owns the writer lease and a qualified connection.
/// Existing stores are opened only at the exact known schema; upgrades require separate recovery safeguards.</summary>
public sealed class NewStateStore : IAsyncDisposable
{
    private readonly FileStream writerLease;
    internal SemaphoreSlim AccessGate { get; } = new(1, 1);
    public SqliteConnection Connection { get; }
    public string RuntimeVersion { get; }

    private NewStateStore(FileStream lease, SqliteConnection connection, string version) =>
        (writerLease, Connection, RuntimeVersion) = (lease, connection, version);

    public static Task<NewStateStore> CreateAsync(string runtimeRoot, CancellationToken ct = default) =>
        OpenCoreAsync(runtimeRoot, create: true, ct);

    public static Task<NewStateStore> OpenExistingAsync(string runtimeRoot, CancellationToken ct = default) =>
        OpenCoreAsync(runtimeRoot, create: false, ct);

    private static async Task<NewStateStore> OpenCoreAsync(string runtimeRoot, bool create, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        ct.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(runtimeRoot))
        {
            throw new ArgumentException("Runtime root must be absolute.", nameof(runtimeRoot));
        }

        Directory.CreateDirectory(runtimeRoot);
        var lease = new FileStream(Path.Combine(runtimeRoot, ".writer.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        SqliteConnection? connection = null;
        try
        {
            var databasePath = Path.Combine(runtimeRoot, "lifetracker.sqlite");
            // CreateNew is atomic: never truncate or migrate an existing file.
            if (create)
            {
                using (new FileStream(databasePath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            }
            else if (!File.Exists(databasePath))
            {
                throw new FileNotFoundException("Existing store was not found.");
            }
            SQLitePCL.Batteries_V2.Init();
            connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWrite,
                ForeignKeys = true,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
            await connection.OpenAsync(ct);
            var version = await SqliteRuntime.QualifyAsync(connection, ct);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA busy_timeout=5000;";
                await command.ExecuteNonQueryAsync(ct);
                command.CommandText = "PRAGMA journal_mode=WAL;";
                var mode = Convert.ToString(await command.ExecuteScalarAsync(ct));
                if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException("The store requires WAL journaling.");
                }

                command.CommandText = "PRAGMA synchronous=FULL;";
                await command.ExecuteNonQueryAsync(ct);
            }
            var options = new DbContextOptionsBuilder<StateDbContext>().UseSqlite(connection).Options;
            await using (var context = new StateDbContext(options))
            {
                if (create)
                {
                    await context.Database.MigrateAsync(ct);
                }
                else
                {
                    var applied = (await context.Database.GetAppliedMigrationsAsync(ct)).ToArray();
                    if (!applied.SequenceEqual(context.Database.GetMigrations(), StringComparer.Ordinal))
                    {
                        throw new NotSupportedException("Store requires a separate migration/recovery procedure.");
                    }
                    await using var schema = connection.CreateCommand();
                    schema.CommandText = "PRAGMA user_version;";
                    if (Convert.ToInt64(await schema.ExecuteScalarAsync(ct)) != 1)
                    {
                        throw new NotSupportedException("Unsupported schema version.");
                    }
                }
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA foreign_key_check;";
                await using (var reader = await command.ExecuteReaderAsync(ct))
                {
                    if (await reader.ReadAsync(ct))
                    {
                        throw new InvalidOperationException("Foreign key check failed.");
                    }
                }

                command.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals(Convert.ToString(await command.ExecuteScalarAsync(ct)), "ok", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Store integrity check failed.");
                }
            }
            return new NewStateStore(lease, connection, version);
        }
        catch
        {
            try
            {
                if (connection is not null)
                {
                    await connection.DisposeAsync();
                }
            }
            finally
            {
                await lease.DisposeAsync();
            }
            // Preserve failed initialization for inspection/recovery; never silently delete data.
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await Connection.DisposeAsync(); }
        finally { await writerLease.DisposeAsync(); }
    }
}
