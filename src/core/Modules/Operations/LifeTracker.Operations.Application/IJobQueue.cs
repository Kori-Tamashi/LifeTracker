using LifeTracker.Operations.Domain;

namespace LifeTracker.Operations.Application;

public interface IJobQueue
{
    Task<JobReceipt> EnqueueAsync(JobSubmission submission, CancellationToken ct);
    Task<ClaimedJob?> ClaimAsync(Guid profileId, string owner, TimeSpan leaseDuration, CancellationToken ct);
    Task<bool> HeartbeatAsync(LeaseToken token, TimeSpan leaseDuration, CancellationToken ct);
    Task<bool> CompleteAsync(LeaseToken token, string resultJson, CancellationToken ct);
    Task<bool> FailAsync(LeaseToken token, JobFailureKind failure, CancellationToken ct);
    Task<bool> CancelAsync(Guid profileId, Guid jobId, CancellationToken ct);
    Task<JobSnapshot?> GetAsync(Guid profileId, Guid jobId, CancellationToken ct);
}
public interface IOutbox
{
    Task<IReadOnlyList<OutboxEvent>> ReadPendingAsync(Guid profileId, int limit, CancellationToken ct);
    Task<bool> MarkDispatchedAsync(Guid profileId, Guid eventId, CancellationToken ct);
}
public interface IRetryJitter { double NextUnit(); }
public sealed class JobConflictException() : Exception("Source key or command identity has a conflicting job payload.");
