using System.Security.Cryptography;
using System.Text.Json;
using LifeTracker.Application.Abstractions;
using LifeTracker.Journal.Domain;

namespace LifeTracker.Journal.Application;

public sealed record ReviewExtractionCommand(Guid ProfileId, Guid CommandId, string IdempotencyKey,
    Guid ExtractionId, int ExpectedRevision, ExtractionDecision Decision,
    IReadOnlyList<ExtractionCandidate>? EditedCandidates = null);
public sealed record ExtractionReviewReceipt(Guid CommandId, Guid ExtractionId, int Revision, ExtractionAcceptance Acceptance);
public sealed record ExtractionReviewCommit(Guid ProfileId, string IdempotencyKey, string Fingerprint,
    int ExpectedRevision, ExtractionRevision Updated, ExtractionReviewReceipt Receipt);

/// <summary>Commit must atomically compare revision, append history and persist the receipt.
/// A concurrent identical retry returns the stored receipt; a mismatched fingerprint or revision conflicts.</summary>
public interface IExtractionReviewRepository
{
    Task<ExtractionReviewReceipt?> FindReceiptAsync(Guid profileId, Guid extractionId, string idempotencyKey, string fingerprint, CancellationToken ct);
    Task<ExtractionRevision?> FindAsync(Guid profileId, Guid extractionId, CancellationToken ct);
    Task<ExtractionReviewReceipt> CommitAsync(ExtractionReviewCommit commit, CancellationToken ct);
}

public sealed class ReviewExtractionHandler(IExtractionReviewRepository repository, IUtcClock clock)
{
    public async Task<ExtractionReviewReceipt> ExecuteAsync(ReviewExtractionCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ct.ThrowIfCancellationRequested();
        if (command.ProfileId == Guid.Empty || command.CommandId == Guid.Empty || command.ExtractionId == Guid.Empty
            || command.ExpectedRevision < 1 || !Enum.IsDefined(command.Decision))
        {
            throw new ArgumentException("Invalid review command identity/revision/decision.", nameof(command));
        }
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Length is < 8 or > 128
            || command.IdempotencyKey.Any(char.IsControl))
        {
            throw new ArgumentException("Invalid idempotency key.", nameof(command));
        }
        // Snapshot caller-owned collections before the first await.
        var edited = command.EditedCandidates?.ToArray();
        if ((command.Decision == ExtractionDecision.Edit) != (edited is not null)
            || (edited is not null && (edited.Length == 0 || edited.Any(c => c is null))))
        {
            throw new ArgumentException("Only edit supplies a nonempty candidate set.", nameof(command));
        }
        var fingerprint = Fingerprint(command, edited);
        var previous = await repository.FindReceiptAsync(command.ProfileId, command.ExtractionId, command.IdempotencyKey, fingerprint, ct);
        if (previous is not null) { return previous; }
        var current = await repository.FindAsync(command.ProfileId, command.ExtractionId, ct)
            ?? throw new KeyNotFoundException("Extraction not found in this profile.");
        if (current.ProfileId != command.ProfileId || current.Id != command.ExtractionId)
        {
            throw new InvalidOperationException("Repository returned an extraction outside the requested scope.");
        }
        var updated = current.Decide(command.ExpectedRevision, command.Decision, clock.UtcNow, edited);
        var receipt = new ExtractionReviewReceipt(command.CommandId, updated.Id, updated.Revision, updated.Acceptance);
        return await repository.CommitAsync(new(command.ProfileId, command.IdempotencyKey, fingerprint,
            command.ExpectedRevision, updated, receipt), ct);
    }

    private static string Fingerprint(ReviewExtractionCommand command, ExtractionCandidate[]? edited)
    {
        using var bytes = new MemoryStream();
        using (var json = new Utf8JsonWriter(bytes))
        {
            json.WriteStartArray();
            json.WriteStringValue("journal-review-v1");
            json.WriteStringValue(command.ProfileId);
            json.WriteStringValue(command.ExtractionId);
            json.WriteNumberValue(command.ExpectedRevision);
            json.WriteNumberValue((int)command.Decision);
            if (edited is not null)
            {
                foreach (var candidate in edited.OrderBy(c => c.Id))
                {
                    json.WriteStartArray();
                    json.WriteStringValue(candidate.Id);
                    json.WriteNumberValue((int)candidate.Kind);
                    json.WriteStringValue(candidate.Content);
                    json.WriteNumberValue((int)candidate.Subject);
                    json.WriteNumberValue((int)candidate.Polarity);
                    var evidence = candidate.Evidence;
                    json.WriteStringValue(evidence.ProfileId);
                    json.WriteStringValue(evidence.ConversationId);
                    json.WriteStringValue(evidence.MessageId);
                    json.WriteNumberValue(evidence.MessageRevision);
                    json.WriteNumberValue(evidence.Start);
                    json.WriteNumberValue(evidence.Length);
                    json.WriteStringValue(evidence.Quote);
                    json.WriteEndArray();
                }
            }
            json.WriteEndArray();
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }
}
