using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LifeTracker.Kernel;
using LifeTracker.State.Domain;

namespace LifeTracker.State.Application;

internal static class StateValidation
{
    public static void Envelope(Guid profileId, Guid commandId, string key)
    {
        Identifiers.RequireNonEmpty(profileId, nameof(profileId));
        Identifiers.RequireNonEmpty(commandId, nameof(commandId));
        if (string.IsNullOrWhiteSpace(key) || key.Length is < 8 or > 128)
        {
            throw new ArgumentException("Idempotency key must contain 8–128 characters.", nameof(key));
        }
    }

    public static StateMetric ParseMetric(string value) => value switch
    {
        "energy" => StateMetric.Energy,
        "mood" => StateMetric.Mood,
        "anxiety" => StateMetric.Anxiety,
        "physical_wellbeing" => StateMetric.PhysicalWellbeing,
        _ => throw new ArgumentException("Unknown state metric.", nameof(value))
    };

    public static string MetricName(StateMetric metric) => metric switch
    {
        StateMetric.Energy => "energy",
        StateMetric.Mood => "mood",
        StateMetric.Anxiety => "anxiety",
        StateMetric.PhysicalWellbeing => "physical_wellbeing",
        _ => throw new ArgumentOutOfRangeException(nameof(metric))
    };

    public static DateTimeOffset NormalizeTime(DateTimeOffset observedAt, DateTimeOffset now)
    {
        observedAt = observedAt.ToUniversalTime();
        now = now.ToUniversalTime();
        if (observedAt - now > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentException("The assessment time is in the future.", nameof(observedAt));
        }

        return observedAt > now ? now : observedAt;
    }

    // Length-prefixed UTF-8 fields prevent concatenation collisions and canonicalize numeric input.
    public static string Fingerprint(params string[] fields)
    {
        var canonical = string.Concat(fields.Select(f => Encoding.UTF8.GetByteCount(f).ToString(CultureInfo.InvariantCulture) + ":" + f));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
