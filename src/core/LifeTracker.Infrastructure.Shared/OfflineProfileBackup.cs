using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace LifeTracker.Infrastructure.Shared;

/// <summary>Offline State-profile snapshot. Never replaces a profile or silently excludes unknown data.</summary>
public static class OfflineProfileBackup
{
    private const string Database = "lifetracker.sqlite";
    private const string Credential = "owner.credential";
    private const string Pending = "desktop-pending-assessment.json";
    private const string ManifestName = "backup-manifest.json";
    private static readonly HashSet<string> Artifacts = [Database, Credential, Pending];
    private static readonly HashSet<string> RuntimeFiles = [Database, Credential, Pending, ".writer.lock",
        "desktop-assessment.lock", "lifetracker.sqlite-wal", "lifetracker.sqlite-shm", "lifetracker.sqlite-journal", ".DS_Store"];
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    public sealed record Manifest(int Format, int SchemaVersion, string Scope, Guid ProfileId,
        DateTimeOffset CreatedAt, Dictionary<string, string> Sha256);

    internal static async Task<Guid> InspectProfileAsync(string source, CancellationToken ct)
    {
        KnownFiles(source, RuntimeFiles);
        await using var desktopLease = new FileStream(Path.Combine(source, "desktop-assessment.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await using var store = await SqliteStore.OpenExistingAsync(source, ct);
        await ProfileCredential.LoadOrCreateAsync(source, create: false, ct);
        return await ProfileIdAsync(store, ct);
    }

    public static async Task CreateAsync(string sourceRoot, string destination, CancellationToken ct = default)
    {
        var source = Root(sourceRoot);
        var target = NewDestination(destination);
        Separate(source, target);
        KnownFiles(source, RuntimeFiles);
        // Interoperates with the desktop journal's exclusive lock on Unix.
        await using var desktopLease = new FileStream(Path.Combine(source, "desktop-assessment.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await using var store = await SqliteStore.OpenExistingAsync(source, ct);
        await ProfileCredential.LoadOrCreateAsync(source, create: false, ct);
        var profile = await ProfileIdAsync(store, ct);
        await StageAsync(target, async stage =>
        {
            var snapshot = Path.Combine(stage, Database);
            await using (var empty = PrivateFile(snapshot)) { }
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = snapshot,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString()))
            {
                await connection.OpenAsync(ct);
                ct.ThrowIfCancellationRequested();
                store.Connection.BackupDatabase(connection);
            }
            await CopyAsync(Path.Combine(source, Credential), Path.Combine(stage, Credential), ct);
            if (File.Exists(Path.Combine(source, Pending)))
            {
                await CopyAsync(Path.Combine(source, Pending), Path.Combine(stage, Pending), ct);
            }
            await ValidateDatabaseAsync(stage, profile, ct);
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in Artifacts.Where(name => File.Exists(Path.Combine(stage, name))))
            {
                hashes.Add(name, await HashAsync(Path.Combine(stage, name), ct));
            }
            var manifest = new Manifest(1, 1, "state-profile-offline-v1", profile, DateTimeOffset.UtcNow, hashes);
            await using var file = PrivateFile(Path.Combine(stage, ManifestName));
            await JsonSerializer.SerializeAsync(file, manifest, Json, ct);
            await file.FlushAsync(ct);
            file.Flush(flushToDisk: true);
        }, ct);
    }

    public static async Task<Manifest> VerifyAsync(string backupRoot, CancellationToken ct = default)
    {
        var backup = Root(backupRoot);
        KnownFiles(backup, [Database, Credential, Pending, ManifestName, ".DS_Store"]);
        var manifestPath = Path.Combine(backup, ManifestName);
        if (new FileInfo(manifestPath).Length > 16384)
        {
            throw new InvalidDataException("Backup manifest exceeds the supported size.");
        }
        await using var input = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<Manifest>(input, Json, ct)
            ?? throw new InvalidDataException("Missing backup manifest.");
        if (manifest.Format != 1 || manifest.SchemaVersion != 1 || manifest.Scope != "state-profile-offline-v1"
            || manifest.ProfileId == Guid.Empty || manifest.Sha256 is null
            || !manifest.Sha256.ContainsKey(Database) || !manifest.Sha256.ContainsKey(Credential)
            || manifest.Sha256.Keys.Any(name => !Artifacts.Contains(name)))
        {
            throw new InvalidDataException("Unsupported backup manifest.");
        }
        var actual = Directory.GetFiles(backup).Select(Path.GetFileName).Where(name => name != ManifestName && name != ".DS_Store").ToHashSet();
        if (!actual.SetEquals(manifest.Sha256.Keys))
        {
            throw new InvalidDataException("Backup artifact inventory mismatch.");
        }
        foreach (var (name, hash) in manifest.Sha256)
        {
            if (hash is null || hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c))
                || !string.Equals(await HashAsync(Path.Combine(backup, name), ct), hash, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Backup artifact checksum mismatch.");
            }
        }
        await ProfileCredential.LoadOrCreateAsync(backup, create: false, ct);
        // Validation may create WAL/lock files: perform it only in a disposable copy, never in the backup.
        var temporary = Path.Combine(Path.GetTempPath(), "lifetracker-backup-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            PrivateDirectory(temporary);
            await CopyAsync(Path.Combine(backup, Database), Path.Combine(temporary, Database), ct);
            if (!string.Equals(await HashAsync(Path.Combine(temporary, Database), ct), manifest.Sha256[Database], StringComparison.Ordinal))
            {
                throw new InvalidDataException("Backup changed during verification.");
            }
            await ValidateDatabaseAsync(temporary, manifest.ProfileId, ct);
        }
        finally
        {
            if (Directory.Exists(temporary)) { Directory.Delete(temporary, recursive: true); }
        }
        return manifest;
    }

    public static async Task RestoreAsync(string backupRoot, string destination, CancellationToken ct = default)
    {
        var backup = Root(backupRoot);
        var target = NewDestination(destination);
        Separate(backup, target);
        var manifest = await VerifyAsync(backup, ct);
        await StageAsync(target, async stage =>
        {
            foreach (var (name, hash) in manifest.Sha256)
            {
                await CopyAsync(Path.Combine(backup, name), Path.Combine(stage, name), ct);
                if (!string.Equals(await HashAsync(Path.Combine(stage, name), ct), hash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Backup changed during restore.");
                }
            }
            await ValidateDatabaseAsync(stage, manifest.ProfileId, ct);
            await ProfileCredential.LoadOrCreateAsync(stage, create: false, ct);
        }, ct);
    }

    private static async Task ValidateDatabaseAsync(string root, Guid expectedProfile, CancellationToken ct)
    {
        await using (var store = await SqliteStore.OpenExistingAsync(root, ct))
        {
            if (await ProfileIdAsync(store, ct) != expectedProfile)
            {
                throw new InvalidDataException("Backup profile identity mismatch.");
            }
        }
        File.Delete(Path.Combine(root, ".writer.lock"));
        // The last connection closes/checkpoints WAL. A leftover sidecar is not silently discarded.
        if (File.Exists(Path.Combine(root, Database + "-wal")) || File.Exists(Path.Combine(root, Database + "-shm")))
        {
            throw new InvalidDataException("Snapshot validation left unexpected sidecars.");
        }
    }

    private static async Task<Guid> ProfileIdAsync(SqliteStore store, CancellationToken ct)
    {
        await using var query = store.Connection.CreateCommand();
        query.CommandText = "SELECT id, active FROM profiles;";
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || !Guid.TryParse(reader.GetString(0), out var id) || id == Guid.Empty
            || reader.GetInt64(1) != 1 || await reader.ReadAsync(ct))
        {
            throw new InvalidDataException("A single initialized profile is required.");
        }
        return id;
    }

    private static async Task StageAsync(string target, Func<string, Task> populate, CancellationToken ct)
    {
        var parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, ".lifetracker-stage-" + Guid.NewGuid().ToString("N"));
        try
        {
            PrivateDirectory(stage);
            await populate(stage);
            ct.ThrowIfCancellationRequested();
            Directory.Move(stage, target); // Fails if any destination already exists; never replace.
        }
        finally
        {
            if (Directory.Exists(stage)) { Directory.Delete(stage, recursive: true); }
        }
    }

    internal static string Root(string path, int depth = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (depth > 32) { throw new IOException("Directory link depth exceeded."); }
        if (!Path.IsPathFullyQualified(path)) { throw new ArgumentException("Absolute path required.", nameof(path)); }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (new DirectoryInfo(root).LinkTarget is not null) { throw new IOException("Directory links are unsupported."); }
        // Resolve ancestor links (including macOS /var and /tmp aliases) before overlap checks.
        var volume = Path.GetPathRoot(root)!;
        if (string.Equals(root, volume, StringComparison.Ordinal)) { throw new IOException("Filesystem root is not a profile."); }
        var current = volume;
        foreach (var part in root[volume.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = new DirectoryInfo(Path.Combine(current, part));
            current = directory.LinkTarget is null ? directory.FullName
                : Root(directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new IOException("Unresolved directory link."), depth + 1);
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    private static string NewDestination(string path)
    {
        var root = Root(path);
        if (Directory.Exists(root) || File.Exists(root)) { throw new IOException("Destination must not exist."); }
        return root;
    }

    internal static void Separate(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(first, second, comparison)
            || second.StartsWith(first + Path.DirectorySeparatorChar, comparison)
            || first.StartsWith(second + Path.DirectorySeparatorChar, comparison))
        {
            throw new IOException("Source and destination must be separate trees.");
        }
    }

    private static void KnownFiles(string root, HashSet<string> allowed)
    {
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
        {
            if (entry.LinkTarget is not null || entry is DirectoryInfo || !allowed.Contains(entry.Name))
            {
                throw new IOException("Unknown profile contents or links require a separate backup scope.");
            }
        }
    }

    private static void PrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) { Directory.CreateDirectory(path); }
        else { Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    private static FileStream PrivateFile(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) { options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
        return new FileStream(path, options);
    }

    private static async Task CopyAsync(string source, string target, CancellationToken ct)
    {
        if (new FileInfo(source).LinkTarget is not null) { throw new IOException("File links are unsupported."); }
        await using var input = File.OpenRead(source);
        await using var output = PrivateFile(target);
        await input.CopyToAsync(output, ct);
        await output.FlushAsync(ct);
        output.Flush(flushToDisk: true);
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var input = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
    }
}
