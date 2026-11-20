using System.Security.Cryptography;
using System.Text;

namespace LifeTracker.Infrastructure.Shared;

public static class ProfileCredential
{
    public static async Task<string> LoadOrCreateAsync(string root, bool create, CancellationToken ct)
    {
        var path = Path.Combine(root, "owner.credential");
        if (!File.Exists(path))
        {
            if (!create)
            {
                throw new IOException("Owner credential recovery is required.");
            }

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using var file = new FileStream(path, options);
            await file.WriteAsync(Encoding.UTF8.GetBytes(token), ct);
            file.Flush(flushToDisk: true);
            return token;
        }
        var existing = await File.ReadAllTextAsync(path, ct);
        if (existing.Length != 43 || existing.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
        {
            throw new IOException("Invalid owner credential.");
        }

        return existing;
    }
}
