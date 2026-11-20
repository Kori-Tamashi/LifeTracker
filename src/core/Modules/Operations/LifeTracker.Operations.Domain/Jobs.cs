namespace LifeTracker.Operations.Domain;

public enum JobState { Queued, Running, RetryWait, Completed, Failed, Cancelled }
public enum JobFailureKind { Timeout, Unavailable, Validation, Authorization, Conflict, Terminal }
public sealed record JobSubmission(Guid ProfileId, Guid CommandId, Guid JobId, string Source, string ExternalId, string Kind, string InputJson);
public sealed record JobReceipt(Guid CommandId, Guid JobId);
public sealed record LeaseToken(Guid ProfileId, Guid JobId, string Owner, long Fence);
public sealed record ClaimedJob(LeaseToken Token, string Kind, string InputJson, int Attempt, DateTimeOffset LeaseUntil);
public sealed record JobSnapshot(Guid JobId, string Kind, JobState State, int Attempts, long Fence,
    DateTimeOffset ReadyAt, DateTimeOffset? LeaseUntil, string? ResultJson);
public sealed record OutboxEvent(Guid ProfileId, Guid Id, string Topic, string ReasonKey, string PayloadJson, DateTimeOffset CreatedAt);

public static class RetryPolicy
{
    public const int MaximumAttempts = 5;
    public static TimeSpan? Delay(int attempt, JobFailureKind failure, double unitJitter)
    {
        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt));
        }

        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        if (!double.IsFinite(unitJitter) || unitJitter is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(unitJitter));
        }

        if (attempt >= MaximumAttempts || failure is not (JobFailureKind.Timeout or JobFailureKind.Unavailable))
        {
            return null;
        }

        var capMs = Math.Min(900_000d, 5_000d * Math.Pow(2, attempt - 1));
        return TimeSpan.FromMilliseconds(Math.Floor(unitJitter * capMs));
    }
}
