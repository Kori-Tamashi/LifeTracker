using LifeTracker.State.Contracts;
using LifeTracker.State.Domain;

namespace LifeTracker.State.Application;

public sealed record AssessmentBatch(Guid ProfileId, Guid CommandId, string IdempotencyKey,
    string Fingerprint, IReadOnlyList<AssessmentRevision> Revisions);
public sealed record AssessmentCorrection(Guid ProfileId, Guid CommandId, string IdempotencyKey,
    string Fingerprint, int ExpectedRevision, AssessmentRevision NewRevision);

/// <summary>Implementations atomically deduplicate, check versions and commit the whole batch.
/// Receipt and revisions share a transaction; cancellation never exposes a partial batch.</summary>
public interface IStateRepository
{
    Task<AssessmentReceipt?> FindReceiptAsync(Guid profileId, string idempotencyKey, string fingerprint, CancellationToken ct);
    Task<IReadOnlyList<AssessmentRevision>> GetCurrentAsync(Guid profileId, CancellationToken ct);
    Task<IReadOnlyList<AssessmentRevision>> GetHistoryAsync(Guid profileId, Guid assessmentId, CancellationToken ct);
    Task<AssessmentRevision?> GetAsync(Guid profileId, Guid assessmentId, CancellationToken ct);
    Task<AssessmentReceipt> CommitAsync(AssessmentBatch batch, CancellationToken ct);
    Task<AssessmentReceipt> CorrectAsync(AssessmentCorrection correction, CancellationToken ct);
}

public sealed class RevisionConflictException() : Exception("Assessment revision has changed.");
public sealed class IdempotencyConflictException() : Exception("Idempotency key already has a different payload.");
public sealed class AssessmentNotFoundException() : Exception("Assessment was not found in this profile.");
