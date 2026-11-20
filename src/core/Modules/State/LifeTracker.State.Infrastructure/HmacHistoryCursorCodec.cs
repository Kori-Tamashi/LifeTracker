using System.Security.Cryptography;
using System.Text.Json;
using LifeTracker.State.Application;

namespace LifeTracker.State.Infrastructure;

public sealed class HmacHistoryCursorCodec : IHistoryCursorCodec
{
    private readonly byte[] key;
    private sealed record Payload(long W, long T, Guid I, long E, string F);
    public HmacHistoryCursorCodec(byte[] key)
    {
        if (key.Length < 32)
        {
            throw new ArgumentException("Cursor signing key is too short.", nameof(key));
        }

        this.key = (byte[])key.Clone();
    }
    public string Encode(HistoryCursor cursor)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new Payload(cursor.Watermark, cursor.LastTicks, cursor.LastId, cursor.ExpiresUnixSeconds, cursor.QueryHash));
        return Convert.ToBase64String(body).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "." + Convert.ToHexString(HMACSHA256.HashData(key, body));
    }
    public HistoryCursor Decode(string cursor)
    {
        try
        {
            if (cursor.Length > 512)
            {
                throw new InvalidHistoryCursorException();
            }

            var parts = cursor.Split('.');
            if (parts.Length != 2)
            {
                throw new InvalidHistoryCursorException();
            }

            var base64 = parts[0].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            var body = Convert.FromBase64String(base64);
            var signature = Convert.FromHexString(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(key, body)))
            {
                throw new InvalidHistoryCursorException();
            }

            var payload = JsonSerializer.Deserialize<Payload>(body) ?? throw new InvalidHistoryCursorException();
            if (payload.W < 0 || payload.T < 0 || payload.I == Guid.Empty || payload.F is null)
            {
                throw new InvalidHistoryCursorException();
            }

            return new(payload.W, payload.T, payload.I, payload.E, payload.F);
        }
        catch (Exception error) when (error is FormatException or JsonException or OverflowException)
        {
            throw new InvalidHistoryCursorException();
        }
    }
}
