using System.Text.RegularExpressions;
using LifeTracker.Infrastructure.Shared;
using LifeTracker.State.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeTracker.State.Infrastructure.Tests;

public sealed class StorageBootstrapTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lifetracker-synthetic-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }

        return ValueTask.CompletedTask;
    }

    private static async Task<object?> Scalar(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(Ct);
    }

    private static async Task Execute(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Ct);
    }

    [Fact]
    public async Task FreshFileUsesQualifiedNativeRuntimeAndDurabilityPragmas()
    {
        await using var store = await SqliteStore.CreateAsync(root, Ct);
        Assert.True(Version.Parse(store.RuntimeVersion) >= SqliteRuntime.MinimumVersion);
        Assert.Equal(1L, await Scalar(store.Connection, "PRAGMA foreign_keys;"));
        Assert.Equal(2L, await Scalar(store.Connection, "PRAGMA synchronous;"));
        Assert.Equal(5000L, await Scalar(store.Connection, "PRAGMA busy_timeout;"));
        Assert.Equal("wal", await Scalar(store.Connection, "PRAGMA journal_mode;"));
        Assert.Equal(1L, await Scalar(store.Connection, "PRAGMA user_version;"));
        Assert.Equal(2L, await Scalar(store.Connection, "SELECT COUNT(*) FROM __EFMigrationsHistory;"));
        await Execute(store.Connection, "CREATE VIRTUAL TABLE temp.search_probe USING fts5(body); INSERT INTO search_probe VALUES ('synthetic');");
        Assert.Equal(1L, await Scalar(store.Connection, "SELECT COUNT(*) FROM search_probe WHERE search_probe MATCH 'synthetic';"));
    }

    [Theory]
    [InlineData("3.51.2")]
    [InlineData("3.50.4")]
    [InlineData("invalid")]
    public void OldOrUnparseableRuntimeFailsBeforeMigration(string version) =>
        Assert.Throws<NotSupportedException>(() => SqliteRuntime.RequireSupportedVersion(version));

    [Fact]
    public async Task SecondWriterIsRefusedAndExistingDatabaseNeverTruncated()
    {
        await using (var store = await SqliteStore.CreateAsync(root, Ct))
        {
            await Execute(store.Connection, "INSERT INTO profiles(id) VALUES ('synthetic-profile');");
            await Assert.ThrowsAnyAsync<IOException>(() => SqliteStore.CreateAsync(root, Ct));
        }
        await Assert.ThrowsAnyAsync<IOException>(() => SqliteStore.CreateAsync(root, Ct));
        await using var reopened = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path.Combine(root, "lifetracker.sqlite"), Pooling = false, ForeignKeys = true }.ToString());
        await reopened.OpenAsync(Ct);
        Assert.Equal(1L, await Scalar(reopened, "SELECT COUNT(*) FROM profiles;"));
        Assert.Equal("ok", await Scalar(reopened, "PRAGMA integrity_check;"));
    }

    [Fact]
    public async Task RelativePathAndPreCancelledRequestDoNotCreateFiles()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SqliteStore.CreateAsync("relative-root", Ct));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SqliteStore.CreateAsync(root, cancellation.Token));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task DeferredIdentityAndRevisionAreCommittedTogether()
    {
        await using var store = await SqliteStore.CreateAsync(root, Ct);
        await Execute(store.Connection, """
            BEGIN IMMEDIATE;
            INSERT INTO profiles(id) VALUES ('synthetic-profile');
            INSERT INTO entities VALUES ('synthetic-profile','synthetic-state','observation',1);
            INSERT INTO revisions VALUES ('synthetic-profile','synthetic-state',1,1,'owner','accepted',NULL,'{}');
            INSERT INTO observations VALUES ('synthetic-profile','synthetic-state',1,'energy',16,1,'self_report');
            COMMIT;
            """);
        Assert.Equal(16L, await Scalar(store.Connection, "SELECT score_half FROM observations;"));
        Assert.Equal(0L, await Scalar(store.Connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    [Fact]
    public async Task ConstraintsRejectForeignProfileInvalidScoreAndSecondActiveProfile()
    {
        await using var store = await SqliteStore.CreateAsync(root, Ct);
        await Execute(store.Connection, """
            INSERT INTO profiles(id) VALUES ('synthetic-profile');
            BEGIN IMMEDIATE;
            INSERT INTO entities VALUES ('synthetic-profile','synthetic-state','observation',1);
            INSERT INTO revisions VALUES ('synthetic-profile','synthetic-state',1,1,'owner','accepted',NULL,'{}');
            COMMIT;
            """);
        foreach (var sql in new[] {
            "INSERT INTO profiles(id) VALUES ('synthetic-other');",
            "INSERT INTO observations VALUES ('synthetic-profile','synthetic-state',1,'energy',21,1,'self_report');",
            "INSERT INTO observations VALUES ('foreign-profile','synthetic-state',1,'energy',16,1,'self_report');",
            "INSERT INTO observations VALUES ('synthetic-profile','synthetic-state',1,'invalid',16,1,'self_report');",
            "INSERT INTO revisions VALUES ('synthetic-profile','missing',1,1,'owner','accepted',NULL,'{}');" })
        {
            await Assert.ThrowsAsync<SqliteException>(() => Execute(store.Connection, sql));
        }

        Assert.Equal(0L, await Scalar(store.Connection, "SELECT COUNT(*) FROM observations;"));
    }

    [Fact]
    public async Task MigrationFailureRollsBackApplicationDdl()
    {
        Directory.CreateDirectory(root);
        SQLitePCL.Batteries_V2.Init();
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(root, "failure.sqlite")};Pooling=False;Foreign Keys=True");
        await connection.OpenAsync(Ct);
        await Execute(connection, "CREATE TABLE observations(existing_marker INTEGER); INSERT INTO observations VALUES (42);");
        var options = new DbContextOptionsBuilder<CoreDbContext>().UseSqlite(connection).Options;
        await using var context = new CoreDbContext(options);
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.MigrateAsync(Ct));
        Assert.Equal(42L, await Scalar(connection, "SELECT existing_marker FROM observations;"));
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name='profiles';"));
        Assert.Equal(0L, await Scalar(connection, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task AppliedSubsetExactlyMatchesNormativeTableAndIndexDefinitions()
    {
        await using var store = await SqliteStore.CreateAsync(root, Ct);
        var baseline = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures/schema-v1.sql"), Ct);
        foreach (var name in new[] { "profiles", "entities", "revisions", "source_events", "observations", "one_active_profile", "observations_selection" })
        {
            var pattern = @"CREATE (?:TABLE|(?:UNIQUE )?INDEX) " + name + @"\b.*?;";
            var expected = Regex.Match(baseline, pattern, RegexOptions.Singleline).Value;
            Assert.NotEmpty(expected);
            var actual = (string)(await Scalar(store.Connection, $"SELECT sql FROM sqlite_master WHERE name='{name}';"))!;
            static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim().TrimEnd(';');
            Assert.Equal(Normalize(expected), Normalize(actual));
        }
    }
}
