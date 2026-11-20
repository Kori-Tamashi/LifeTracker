using System.Globalization;

namespace LifeTracker.Infrastructure.Shared;

public enum DailyBackupResult { BeforeSchedule, AlreadyCurrent, Created }

/// <summary>Startup-only State snapshots. Rotation never makes room before a new verified restore.</summary>
public sealed class ManagedDailyBackup(TimeProvider? clock = null, Func<string, long>? availableBytes = null)
{
    public const long MaximumCatalogBytes = 2_000_000_000;
    public const long FreeReserveBytes = 256 * 1024 * 1024;
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly Func<string, long> availableBytes = availableBytes ?? (path => new DriveInfo(path).AvailableFreeSpace);

    public async Task<DailyBackupResult> RunAsync(string sourceRoot, string backupRoot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"));
        if (local.Hour < 9) { return DailyBackupResult.BeforeSchedule; }
        var today = DateOnly.FromDateTime(local.DateTime);
        var source = OfflineProfileBackup.Root(sourceRoot);
        var root = OfflineProfileBackup.Root(backupRoot);
        OfflineProfileBackup.Separate(source, root);
        var profile = await OfflineProfileBackup.InspectProfileAsync(source, ct);
        var catalog = Path.Combine(root, profile.ToString("D"));
        if (new DirectoryInfo(catalog).LinkTarget is not null) { throw new IOException("Catalog links are unsupported."); }
        CreatePrivateDirectory(catalog);
        var lockPath = Path.Combine(catalog, ".daily.lock");
        if (new FileInfo(lockPath).LinkTarget is not null) { throw new IOException("Catalog links are unsupported."); }
        await using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var snapshots = new List<(DateOnly Date, string Path)>();
        long total = 0;
        foreach (var entry in new DirectoryInfo(catalog).EnumerateFileSystemInfos())
        {
            if (entry.LinkTarget is not null) { throw new IOException("Catalog links are unsupported."); }
            if (entry.Name == ".daily.lock" && entry is FileInfo) { continue; }
            if (entry is not DirectoryInfo || !entry.Name.StartsWith("daily-", StringComparison.Ordinal)
                || !DateOnly.TryParseExact(entry.Name[6..], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date) || date > today)
            {
                throw new InvalidDataException("Unknown catalog entry requires operator inspection.");
            }
            var databaseBytes = new FileInfo(Path.Combine(entry.FullName, "lifetracker.sqlite")).Length;
            if (availableBytes(Path.GetTempPath()) < checked(databaseBytes + FreeReserveBytes))
            {
                throw new IOException("Insufficient verification capacity; previous snapshots retained.");
            }
            var manifest = await OfflineProfileBackup.VerifyAsync(entry.FullName, ct);
            if (manifest.ProfileId != profile) { throw new InvalidDataException("Catalog profile mismatch."); }
            total = checked(total + Directory.GetFiles(entry.FullName).Sum(path => new FileInfo(path).Length));
            snapshots.Add((date, entry.FullName));
        }
        if (total > MaximumCatalogBytes) { throw new IOException("Catalog budget exceeded."); }
        var existing = snapshots.FirstOrDefault(item => item.Date == today);
        if (existing.Path is not null)
        {
            if (snapshots.Count > 7) { await FinalizeAsync(existing.Path); }
            return DailyBackupResult.AlreadyCurrent;
        }
        // WAL is included conservatively; temp verification and restore may live on another volume.
        var sourceBytes = Directory.GetFiles(source).Sum(path => new FileInfo(path).Length);
        var required = checked(2 * sourceBytes + 4 * 1024 * 1024);
        if (checked(total + required) > MaximumCatalogBytes
            || availableBytes(catalog) < checked(required + FreeReserveBytes)
            || availableBytes(Path.GetTempPath()) < checked(required + FreeReserveBytes))
        {
            throw new IOException("Insufficient backup capacity; previous snapshots retained.");
        }
        var destination = Path.Combine(catalog, "daily-" + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await OfflineProfileBackup.CreateAsync(source, destination, ct);
        snapshots.Add((today, destination));
        await FinalizeAsync(destination);
        return DailyBackupResult.Created;

        async Task FinalizeAsync(string completed)
        {
            var created = await OfflineProfileBackup.VerifyAsync(completed, ct);
            if (created.ProfileId != profile) { throw new InvalidDataException("Source profile changed during snapshot."); }
            var rehearsal = Path.Combine(catalog, ".restore-check-" + Guid.NewGuid().ToString("N"));
            try { await OfflineProfileBackup.RestoreAsync(completed, rehearsal, ct); }
            finally { if (Directory.Exists(rehearsal)) { Directory.Delete(rehearsal, recursive: true); } }
            // No other cleanup is authorized: manual/pre-migration/unknown files never participate.
            foreach (var old in snapshots.OrderByDescending(item => item.Date).Skip(7))
            {
                ct.ThrowIfCancellationRequested();
                var manifest = await OfflineProfileBackup.VerifyAsync(old.Path, ct);
                if (manifest.ProfileId != profile) { throw new InvalidDataException("Catalog changed during rotation."); }
                Directory.Delete(old.Path, recursive: true);
            }
        }
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) { Directory.CreateDirectory(path); }
        else { Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }
}
