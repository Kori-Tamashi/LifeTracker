using LifeTracker.Kernel;

namespace LifeTracker.State.Domain;

public enum StateMetric { Energy, Mood, Anxiety, PhysicalWellbeing }
public enum AssessmentSource { SelfReport, AiEstimate }
public enum AssessmentStatus { Accepted, Rejected, Deleted }

public sealed record AssessmentRevision
{
    public Guid ProfileId { get; }
    public Guid AssessmentId { get; }
    public int Revision { get; }
    public StateMetric Metric { get; }
    public Score Score { get; }
    public DateTimeOffset ObservedAt { get; }
    public DateTimeOffset OriginalObservedAt { get; }
    public DateTimeOffset RecordedAt { get; }
    public AssessmentSource Source { get; }
    public AssessmentStatus Status { get; }
    public string? Comment { get; }
    public string? CorrectionReason { get; }

    public AssessmentRevision(Guid profileId, Guid assessmentId, int revision, StateMetric metric,
        Score score, DateTimeOffset observedAt, DateTimeOffset recordedAt,
        AssessmentSource source = AssessmentSource.SelfReport,
        AssessmentStatus status = AssessmentStatus.Accepted,
        DateTimeOffset? originalObservedAt = null, string? comment = null, string? correctionReason = null)
    {
        ProfileId = Identifiers.RequireNonEmpty(profileId, nameof(profileId));
        AssessmentId = Identifiers.RequireNonEmpty(assessmentId, nameof(assessmentId));
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        if (!Enum.IsDefined(metric)) throw new ArgumentOutOfRangeException(nameof(metric));
        if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        Revision = revision;
        Metric = metric;
        Score = score;
        ObservedAt = observedAt.ToUniversalTime();
        OriginalObservedAt = (originalObservedAt ?? observedAt).ToUniversalTime();
        RecordedAt = recordedAt.ToUniversalTime();
        Source = source;
        Status = status;
        Comment = comment;
        CorrectionReason = correctionReason;
    }
}
