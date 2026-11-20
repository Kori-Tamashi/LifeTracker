using LifeTracker.Kernel;

namespace LifeTracker.State.Domain;

public enum BalanceLevel { Undefined, Attention, Satisfactory, Good, Excellent }

public sealed record SelectedMetric(StateMetric Metric, AssessmentRevision? Assessment, bool Fresh);
public sealed record BalanceSnapshot(IReadOnlyList<SelectedMetric> Metrics, Score? Balance,
    BalanceLevel Level, DateTimeOffset ComputedAt, DateTimeOffset? ValidUntil)
{
    public const string RuleVersion = "state-v1";
}

public static class BalancePolicy
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    public static BalanceSnapshot Calculate(Guid profileId, IEnumerable<AssessmentRevision> history, DateTimeOffset now)
    {
        Identifiers.RequireNonEmpty(profileId, nameof(profileId));
        ArgumentNullException.ThrowIfNull(history);
        now = now.ToUniversalTime();
        var records = history.ToArray();
        if (records.Any(r => r.ProfileId != profileId))
        {
            throw new InvalidOperationException("Repository returned data outside the profile scope.");
        }

        foreach (var revision in records.GroupBy(r => (r.AssessmentId, r.Revision)))
        {
            if (revision.Distinct().Skip(1).Any())
            {
                throw new InvalidOperationException("Conflicting copies of the same assessment revision.");
            }
        }

        var current = records.GroupBy(r => r.AssessmentId)
            .Select(g => g.MaxBy(r => r.Revision)!)
            .Where(r => r.Status == AssessmentStatus.Accepted && r.ObservedAt <= now).ToArray();
        var selected = Enum.GetValues<StateMetric>().Select(metric =>
        {
            var candidates = NewestFirst(current.Where(r => r.Metric == metric)).ToArray();
            var fresh = candidates.Where(r => now - r.ObservedAt < Lifetime).ToArray();
            var chosen = fresh.FirstOrDefault(r => r.Source == AssessmentSource.SelfReport)
                ?? fresh.FirstOrDefault(r => r.Source == AssessmentSource.AiEstimate)
                ?? candidates.FirstOrDefault();
            return new SelectedMetric(metric, chosen, chosen is not null && now - chosen.ObservedAt < Lifetime);
        }).ToArray();

        if (selected.Any(m => !m.Fresh))
        {
            return new(Array.AsReadOnly(selected), null, BalanceLevel.Undefined, now, null);
        }

        var normalized = selected.Select(m => m.Metric == StateMetric.Anxiety
            ? m.Assessment!.Score.Invert().Value : m.Assessment!.Score.Value);
        var balance = new Score(normalized.Min());
        var level = balance.Value switch
        {
            >= 9m => BalanceLevel.Excellent,
            >= 7.5m => BalanceLevel.Good,
            >= 6m => BalanceLevel.Satisfactory,
            _ => BalanceLevel.Attention
        };
        return new(Array.AsReadOnly(selected), balance, level, now,
            selected.Min(m => m.Assessment!.ObservedAt + Lifetime));
    }

    private static IOrderedEnumerable<AssessmentRevision> NewestFirst(IEnumerable<AssessmentRevision> records) =>
        records.OrderByDescending(r => r.ObservedAt).ThenByDescending(r => r.RecordedAt)
            .ThenByDescending(r => r.AssessmentId.ToString("D"), StringComparer.Ordinal);
}
