using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LifeTracker.Application.Abstractions;
using LifeTracker.Kernel;
using LifeTracker.State.Contracts;
using LifeTracker.State.Domain;

namespace LifeTracker.State.Application;

public sealed record HistorySnapshot(long Watermark, IReadOnlyList<AssessmentRevision> Records);
public sealed record HistoryCursor(long Watermark, long LastTicks, Guid LastId, long ExpiresUnixSeconds, string QueryHash);
public interface IStateHistoryReader
{
    Task<HistorySnapshot> ReadSnapshotAsync(Guid profileId, long? watermark, CancellationToken ct);
}
public interface IHistoryCursorCodec
{
    string Encode(HistoryCursor cursor);
    HistoryCursor Decode(string cursor);
}
public sealed class InvalidHistoryCursorException() : Exception("The history cursor is invalid or expired.");

public sealed class AssessmentHistoryQuery(IStateHistoryReader reader, IHistoryCursorCodec codec, IUtcClock clock) : IGetAssessmentHistory
{
    public async Task<AssessmentHistoryPage> ExecuteAsync(Guid profileId, DateTimeOffset? from, DateTimeOffset? to,
        int limit = 50, string? cursor = null, CancellationToken cancellationToken = default)
    {
        Identifiers.RequireNonEmpty(profileId, nameof(profileId));
        if (limit is < 1 or > 100 || from is not null && to is not null && from >= to)
        {
            throw new ArgumentException("Invalid history query.");
        }

        var canonical = string.Join("|", profileId.ToString("D"), from?.ToUniversalTime().ToString("O") ?? "",
            to?.ToUniversalTime().ToString("O") ?? "", limit.ToString(CultureInfo.InvariantCulture));
        var queryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var continuation = cursor is null ? null : codec.Decode(cursor);
        if (continuation is not null && (continuation.QueryHash != queryHash || continuation.ExpiresUnixSeconds <= clock.UtcNow.ToUnixTimeSeconds()))
        {
            throw new InvalidHistoryCursorException();
        }

        var snapshot = await reader.ReadSnapshotAsync(profileId, continuation?.Watermark, cancellationToken);
        var selected = snapshot.Records.Where(r => (from is null || r.ObservedAt >= from) && (to is null || r.ObservedAt < to));
        if (continuation is not null)
        {
            selected = selected.Where(r => r.ObservedAt.UtcTicks < continuation.LastTicks || r.ObservedAt.UtcTicks == continuation.LastTicks
                && StringComparer.Ordinal.Compare(r.AssessmentId.ToString("D"), continuation.LastId.ToString("D")) > 0);
        }

        var rows = selected.OrderByDescending(r => r.ObservedAt).ThenBy(r => r.AssessmentId.ToString("D"), StringComparer.Ordinal).Take(limit + 1).ToArray();
        var page = rows.Take(limit).ToArray();
        string? next = null;
        if (rows.Length > limit)
        {
            var last = page[^1];
            next = codec.Encode(new(snapshot.Watermark, last.ObservedAt.UtcTicks, last.AssessmentId,
                continuation?.ExpiresUnixSeconds ?? clock.UtcNow.AddHours(24).ToUnixTimeSeconds(), queryHash));
        }
        return new(Array.AsReadOnly(page.Select(r => new AssessmentRecord(r.AssessmentId, r.Revision,
            StateValidation.MetricName(r.Metric), r.Score.Value, r.ObservedAt,
            r.Source == AssessmentSource.SelfReport ? "self_report" : "ai_estimate", r.Status.ToString().ToLowerInvariant())).ToArray()), next);
    }
}
