using System.Globalization;
using LifeTracker.Application.Abstractions;
using LifeTracker.Kernel;
using LifeTracker.State.Contracts;
using LifeTracker.State.Domain;

namespace LifeTracker.State.Application;

public sealed class ReviseAssessmentHandler(IStateRepository repository, IUtcClock clock) : IReviseAssessment
{
    public async Task<AssessmentReceipt> ExecuteAsync(ReviseAssessmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        StateValidation.Envelope(command.ProfileId, command.CommandId, command.IdempotencyKey);
        Identifiers.RequireNonEmpty(command.AssessmentId, nameof(command.AssessmentId));
        if (command.ExpectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(command.ExpectedRevision));
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 1000)
            throw new ArgumentException("A correction reason is required (max 1000 characters).", nameof(command));
        var score = new Score(command.Score);
        var fingerprint = StateValidation.Fingerprint("revise", command.AssessmentId.ToString("D"),
            command.ExpectedRevision.ToString(CultureInfo.InvariantCulture), command.ObservedAt.ToUniversalTime().ToString("O"),
            score.HalfUnits.ToString(CultureInfo.InvariantCulture), command.Reason);
        var previous = await repository.FindReceiptAsync(command.ProfileId, command.IdempotencyKey, fingerprint, cancellationToken);
        if (previous is not null) return previous;
        var current = await repository.GetAsync(command.ProfileId, command.AssessmentId, cancellationToken)
            ?? throw new AssessmentNotFoundException();
        if (current.ProfileId != command.ProfileId || current.AssessmentId != command.AssessmentId)
            throw new InvalidOperationException("Repository returned data outside the requested identity.");
        if (current.Revision != command.ExpectedRevision) throw new RevisionConflictException();
        var now = clock.UtcNow.ToUniversalTime();
        var revised = new AssessmentRevision(command.ProfileId, current.AssessmentId, checked(current.Revision + 1),
            current.Metric, score, StateValidation.NormalizeTime(command.ObservedAt, now), now, current.Source,
            current.Status, command.ObservedAt, current.Comment, command.Reason);
        return await repository.CorrectAsync(new(command.ProfileId, command.CommandId, command.IdempotencyKey,
            fingerprint, command.ExpectedRevision, revised), cancellationToken);
    }
}
