namespace LifeTracker.State.Contracts;

public sealed record AssessmentValue(string Metric, decimal Score);
public sealed record RecordAssessmentsCommand(Guid ProfileId, Guid CommandId, string IdempotencyKey,
    DateTimeOffset ObservedAt, IReadOnlyList<AssessmentValue> Values, string? Comment = null);
public sealed record ReviseAssessmentCommand(Guid ProfileId, Guid CommandId, string IdempotencyKey,
    Guid AssessmentId, int ExpectedRevision, DateTimeOffset ObservedAt, decimal Score, string Reason);
public sealed record SavedAssessment(Guid Id, int Revision);
public sealed record AssessmentReceipt(Guid CommandId, IReadOnlyList<SavedAssessment> Assessments);
public sealed record MetricState(string Metric, decimal? Score, string SourceKind, DateTimeOffset? ObservedAt,
    string? RevisionId, bool Fresh);
public sealed record CurrentState(IReadOnlyList<MetricState> Metrics, decimal? Balance, string Level,
    DateTimeOffset ComputedAt, DateTimeOffset? ValidUntil, string RuleVersion);

public interface IRecordAssessments
{
    Task<AssessmentReceipt> ExecuteAsync(RecordAssessmentsCommand command, CancellationToken cancellationToken = default);
}
public interface IReviseAssessment
{
    Task<AssessmentReceipt> ExecuteAsync(ReviseAssessmentCommand command, CancellationToken cancellationToken = default);
}
public interface IGetCurrentState
{
    Task<CurrentState> ExecuteAsync(Guid profileId, CancellationToken cancellationToken = default);
}
