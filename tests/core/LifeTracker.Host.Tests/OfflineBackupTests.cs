using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using LifeTracker.Host;
using LifeTracker.Infrastructure.Shared;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LifeTracker.Host.Tests;

public sealed class OfflineBackupTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lifetracker-backup-synthetic-" + Guid.NewGuid().ToString("N"));
    private string Profile => Path.Combine(root, "profile");
    private string Backup => Path.Combine(root, "backups", "snapshot");
    private string Restored => Path.Combine(root, "restored");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
        return ValueTask.CompletedTask;
    }

    private async Task SeedAsync()
    {
        await using var host = await LocalApiHost.CreateAsync(new(Profile, 0), ct: Ct);
        await host.StartAsync(Ct);
        using var client = new HttpClient { BaseAddress = host.Address };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await File.ReadAllTextAsync(Path.Combine(Profile, "owner.credential"), Ct));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "synthetic-backup-record");
        using var response = await client.PostAsJsonAsync("/api/v1/assessments", new
        {
            observed_at = DateTimeOffset.UtcNow,
            values = new[] { new { metric = "energy", score = 8m } }
        }, Ct);
        response.EnsureSuccessStatusCode();
    }

    private static Dictionary<string, string> Hashes(string directory) => Directory.GetFiles(directory)
        .ToDictionary(path => Path.GetFileName(path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    [Fact]
    public async Task RoundTripPreservesRowsIdentityCredentialPendingAndImmutableBackup()
    {
        await SeedAsync();
        const string pending = "{\"synthetic_pending\":true}";
        await File.WriteAllTextAsync(Path.Combine(Profile, "desktop-pending-assessment.json"), pending, Ct);
        await OfflineProfileBackup.CreateAsync(Profile, Backup, Ct);
        var before = Hashes(Backup);
        var manifest = await OfflineProfileBackup.VerifyAsync(Backup, Ct);
        await OfflineProfileBackup.RestoreAsync(Backup, Restored, Ct);
        Assert.Equal(before.OrderBy(p => p.Key), Hashes(Backup).OrderBy(p => p.Key));
        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(Profile, "owner.credential"), Ct),
            await File.ReadAllTextAsync(Path.Combine(Restored, "owner.credential"), Ct));
        Assert.Equal(pending, await File.ReadAllTextAsync(Path.Combine(Restored, "desktop-pending-assessment.json"), Ct));
        await using var store = await SqliteStore.OpenExistingAsync(Restored, Ct);
        await using var query = store.Connection.CreateCommand();
        query.CommandText = "SELECT id FROM profiles;";
        Assert.Equal(manifest.ProfileId.ToString("D"), await query.ExecuteScalarAsync(Ct));
        query.CommandText = "SELECT COUNT(*) FROM observations;";
        Assert.Equal(1L, await query.ExecuteScalarAsync(Ct));
    }

    [Fact]
    public async Task ExistingDestinationAndNestedDestinationAreNeverReplaced()
    {
        await SeedAsync();
        Directory.CreateDirectory(Restored);
        await File.WriteAllTextAsync(Path.Combine(Restored, "sentinel"), "synthetic", Ct);
        await Assert.ThrowsAsync<IOException>(() => OfflineProfileBackup.CreateAsync(Profile, Restored, Ct));
        await Assert.ThrowsAsync<IOException>(() => OfflineProfileBackup.CreateAsync(Profile, Path.Combine(Profile, "backup"), Ct));
        await OfflineProfileBackup.CreateAsync(Profile, Backup, Ct);
        await Assert.ThrowsAsync<IOException>(() => OfflineProfileBackup.RestoreAsync(Backup, Restored, Ct));
        Assert.Equal("synthetic", await File.ReadAllTextAsync(Path.Combine(Restored, "sentinel"), Ct));
    }

    [Fact]
    public async Task ActiveCorePreventsBackup()
    {
        await SeedAsync();
        await using var active = await SqliteStore.OpenExistingAsync(Profile, Ct);
        await Assert.ThrowsAsync<IOException>(() => OfflineProfileBackup.CreateAsync(Profile, Backup, Ct));
        Assert.False(Directory.Exists(Backup));
    }

    [Fact]
    public async Task ActiveDesktopPreventsBackup()
    {
        await SeedAsync();
        await using var desktop = new FileStream(Path.Combine(Profile, "desktop-assessment.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => OfflineProfileBackup.CreateAsync(Profile, Backup, Ct));
        Assert.False(Directory.Exists(Backup));
    }

    [Theory]
    [InlineData("unrecognized.txt")]
    [InlineData("future-module")]
    public async Task UnknownProfileArtifactsCannotBeSilentlyOmitted(string name)
    {
        await SeedAsync();
        if (name == "future-module") { Directory.CreateDirectory(Path.Combine(Profile, name)); }
        else { await File.WriteAllTextAsync(Path.Combine(Profile, name), "synthetic", Ct); }
        await Assert.ThrowsAsync<IOException>(() => OfflineProfileBackup.CreateAsync(Profile, Backup, Ct));
        Assert.False(Directory.Exists(Backup));
    }

    [Fact]
    public async Task CorruptionIsRejectedBeforeDestinationIsPublished()
    {
        await SeedAsync();
        await OfflineProfileBackup.CreateAsync(Profile, Backup, Ct);
        await File.WriteAllTextAsync(Path.Combine(Backup, "owner.credential"), new string('a', 43), Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => OfflineProfileBackup.RestoreAsync(Backup, Restored, Ct));
        Assert.False(Directory.Exists(Restored));
    }

    [Fact]
    public async Task ManifestCannotEscapeBackupOrChangeProfileIdentity()
    {
        await SeedAsync();
        await OfflineProfileBackup.CreateAsync(Profile, Backup, Ct);
        var path = Path.Combine(Backup, "backup-manifest.json");
        var original = await File.ReadAllTextAsync(path, Ct);
        var json = JsonNode.Parse(original)!;
        json["sha256"]!["../outside"] = new string('A', 64);
        await File.WriteAllTextAsync(path, json.ToJsonString(), Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => OfflineProfileBackup.VerifyAsync(Backup, Ct));
        json = JsonNode.Parse(original)!;
        json["profile_id"] = Guid.NewGuid().ToString("D");
        await File.WriteAllTextAsync(path, json.ToJsonString(), Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => OfflineProfileBackup.VerifyAsync(Backup, Ct));
    }

    [Fact]
    public async Task UnknownSchemaFailsEvenWithRecomputedChecksum()
    {
        await SeedAsync();
        await OfflineProfileBackup.CreateAsync(Profile, Backup, Ct);
        var database = Path.Combine(Backup, "lifetracker.sqlite");
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
        {
            await connection.OpenAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=99;";
            await command.ExecuteNonQueryAsync(Ct);
        }
        var path = Path.Combine(Backup, "backup-manifest.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path, Ct))!;
        json["sha256"]!["lifetracker.sqlite"] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(database, Ct)));
        await File.WriteAllTextAsync(path, json.ToJsonString(), Ct);
        await Assert.ThrowsAsync<NotSupportedException>(() => OfflineProfileBackup.RestoreAsync(Backup, Restored, Ct));
        Assert.False(Directory.Exists(Restored));
    }

    [Fact]
    public async Task CancellationDoesNotPublishRestore()
    {
        await SeedAsync();
        await OfflineProfileBackup.CreateAsync(Profile, Backup, Ct);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OfflineProfileBackup.RestoreAsync(Backup, Restored, cancellation.Token));
        Assert.False(Directory.Exists(Restored));
    }

    [Fact]
    public async Task AncestorLinkCannotBypassNestedDestinationCheck()
    {
        await SeedAsync();
        var alias = Path.Combine(root, "alias");
        Directory.CreateSymbolicLink(alias, Profile);
        await Assert.ThrowsAsync<IOException>(() => OfflineProfileBackup.CreateAsync(Profile, Path.Combine(alias, "backup"), Ct));
        Assert.False(Directory.Exists(Path.Combine(Profile, "backup")));
        Directory.Delete(alias);
    }
    private sealed class BackupClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static DateTimeOffset BackupMorning => new(2026, 11, 21, 6, 0, 0, TimeSpan.Zero);
    private string Catalog => Path.Combine(root, "automatic");
    private ManagedDailyBackup Daily(DateTimeOffset now, long free = long.MaxValue) =>
        new(new BackupClock(now), _ => free);

    [Fact]
    public async Task DailyScheduleUsesMoscowNineAndDoesNotCatchUpMissedDays()
    {
        await SeedAsync();
        Assert.Equal(DailyBackupResult.BeforeSchedule,
            await Daily(BackupMorning.AddMinutes(-1)).RunAsync(Profile, Catalog, Ct));
        Assert.False(Directory.Exists(Catalog));
        Assert.Equal(DailyBackupResult.Created, await new ManagedDailyBackup(new BackupClock(BackupMorning)).RunAsync(Profile, Catalog, Ct));
        var directory = Assert.Single(Directory.GetDirectories(Catalog));
        var snapshot = Assert.Single(Directory.GetDirectories(directory));
        var hashes = Hashes(snapshot);
        Assert.Equal(DailyBackupResult.AlreadyCurrent, await Daily(BackupMorning.AddHours(1)).RunAsync(Profile, Catalog, Ct));
        Assert.Equal(hashes.OrderBy(p => p.Key), Hashes(snapshot).OrderBy(p => p.Key));
        await Daily(BackupMorning.AddDays(4)).RunAsync(Profile, Catalog, Ct);
        Assert.Equal(2, Directory.GetDirectories(directory).Length);
    }

    [Fact]
    public async Task EighthDailySnapshotRotatesOnlyAfterNewSnapshotCanBeRestored()
    {
        await SeedAsync();
        for (var day = 0; day < 8; day++)
        {
            Assert.Equal(DailyBackupResult.Created,
                await Daily(BackupMorning.AddDays(day)).RunAsync(Profile, Catalog, Ct));
        }
        var directory = Assert.Single(Directory.GetDirectories(Catalog));
        var snapshots = Directory.GetDirectories(directory).Order().ToArray();
        Assert.Equal(7, snapshots.Length);
        Assert.DoesNotContain(snapshots, path => Path.GetFileName(path) == "daily-2026-11-21");
        await OfflineProfileBackup.RestoreAsync(snapshots[^1], Restored, Ct);
        await using var store = await SqliteStore.OpenExistingAsync(Restored, Ct);
        await using var query = store.Connection.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM observations;";
        Assert.Equal(1L, await query.ExecuteScalarAsync(Ct));
    }

    [Fact]
    public async Task LowSpaceAndUnknownContentsNeverRemoveOldSnapshots()
    {
        await SeedAsync();
        await Daily(BackupMorning).RunAsync(Profile, Catalog, Ct);
        var directory = Assert.Single(Directory.GetDirectories(Catalog));
        var snapshot = Assert.Single(Directory.GetDirectories(directory));
        var hashes = Hashes(snapshot);
        await Assert.ThrowsAsync<IOException>(() => Daily(BackupMorning.AddDays(1), 0).RunAsync(Profile, Catalog, Ct));
        Assert.Equal(hashes.OrderBy(p => p.Key), Hashes(snapshot).OrderBy(p => p.Key));
        await File.WriteAllTextAsync(Path.Combine(directory, "manual-or-premigration"), "synthetic", Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => Daily(BackupMorning.AddDays(1)).RunAsync(Profile, Catalog, Ct));
        Assert.Equal(hashes.OrderBy(p => p.Key), Hashes(snapshot).OrderBy(p => p.Key));
        Assert.Single(Directory.GetDirectories(directory));
    }

    [Fact]
    public async Task CatalogQuotaRefusesBeforeAnySnapshotIsDeleted()
    {
        await SeedAsync();
        // A sparse recognized file is enough for conservative preflight, without filling the disk.
        await using (var file = new FileStream(Path.Combine(Profile, "desktop-pending-assessment.json"), FileMode.CreateNew))
        {
            file.SetLength(ManagedDailyBackup.MaximumCatalogBytes);
        }
        await Assert.ThrowsAsync<IOException>(() => Daily(BackupMorning).RunAsync(Profile, Catalog, Ct));
        var directory = Assert.Single(Directory.GetDirectories(Catalog));
        Assert.Empty(Directory.GetDirectories(directory));
    }

    [Fact]
    public async Task DailyRefusesLiveWriterAndNestedOrLinkedCatalogs()
    {
        await SeedAsync();
        await using (var store = await SqliteStore.OpenExistingAsync(Profile, Ct))
        {
            await Assert.ThrowsAsync<IOException>(() => Daily(BackupMorning).RunAsync(Profile, Catalog, Ct));
        }
        await Assert.ThrowsAsync<IOException>(() => Daily(BackupMorning).RunAsync(Profile, Path.Combine(Profile, "automatic"), Ct));
        var alias = Path.Combine(root, "alias");
        Directory.CreateDirectory(Catalog);
        Directory.CreateSymbolicLink(alias, Catalog);
        await Assert.ThrowsAsync<IOException>(() => Daily(BackupMorning).RunAsync(Profile, alias, Ct));
    }

    [Fact]
    public async Task CorruptSnapshotAndCatalogLeaseBlockRotation()
    {
        await SeedAsync();
        await Daily(BackupMorning).RunAsync(Profile, Catalog, Ct);
        var directory = Assert.Single(Directory.GetDirectories(Catalog));
        await using (var lease = new FileStream(Path.Combine(directory, ".daily.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => Daily(BackupMorning.AddDays(1)).RunAsync(Profile, Catalog, Ct));
        }
        var snapshot = Assert.Single(Directory.GetDirectories(directory));
        await File.WriteAllTextAsync(Path.Combine(snapshot, "owner.credential"), new string('a', 43), Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => Daily(BackupMorning.AddDays(1)).RunAsync(Profile, Catalog, Ct));
        Assert.Single(Directory.GetDirectories(directory));
    }

    [Fact]
    public async Task RetryAfterCompletedSnapshotBeforeRotationReconcilesSameDay()
    {
        await SeedAsync();
        await Daily(BackupMorning).RunAsync(Profile, Catalog, Ct);
        var directory = Assert.Single(Directory.GetDirectories(Catalog));
        var first = Assert.Single(Directory.GetDirectories(directory));
        // Model a prior interrupted rotation: all eight snapshots exist and verify successfully.
        for (var day = 1; day < 8; day++)
        {
            var destination = Path.Combine(directory, "daily-" + DateOnly.FromDateTime(BackupMorning.AddDays(day).DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(first)) { File.Copy(file, Path.Combine(destination, Path.GetFileName(file))); }
        }
        Assert.Equal(DailyBackupResult.AlreadyCurrent, await Daily(BackupMorning.AddDays(7)).RunAsync(Profile, Catalog, Ct));
        Assert.Equal(7, Directory.GetDirectories(directory).Length);
        Assert.False(Directory.Exists(first));
    }

}
