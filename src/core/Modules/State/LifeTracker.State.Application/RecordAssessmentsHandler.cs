using System.Globalization;
using LifeTracker.Application.Abstractions;
using LifeTracker.State.Contracts;
using LifeTracker.State.Domain;

namespace LifeTracker.State.Application;

public sealed class RecordAssessmentsHandler(IStateRepository repository, IUtcClock clock) : IRecordAssessments
{
    public async Task<AssessmentReceipt> ExecuteAsync(RecordAssessmentsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        StateValidation.Envelope(command.ProfileId, command.CommandId, command.IdempotencyKey);
        ArgumentNullException.ThrowIfNull(command.Values);
        if (command.Values.Any(v => v is null))
        {
            throw new ArgumentException("Assessment values cannot contain null.", nameof(command));
        }

        var values = command.Values.Select(v => (Metric: StateValidation.ParseMetric(v.Metric), Score: new Score(v.Score))).ToArray();
        if (values.Length is < 1 or > 4 || values.Select(v => v.Metric).Distinct().Count() != values.Length)
        {
            throw new ArgumentException("Supply one to four distinct state metrics.", nameof(command));
        }

        if (command.Comment?.Length > 2000)
        {
            throw new ArgumentException("Comment exceeds 2000 characters.", nameof(command));
        }

        var fields = new[] { "record", command.ObservedAt.ToUniversalTime().ToString("O"), command.Comment ?? "" }
            .Concat(values.OrderBy(v => v.Metric).Select(v => StateValidation.MetricName(v.Metric) + ":" + v.Score.HalfUnits.ToString(CultureInfo.InvariantCulture))).ToArray();
        var fingerprint = StateValidation.Fingerprint(fields);
        var previous = await repository.FindReceiptAsync(command.ProfileId, command.IdempotencyKey, fingerprint, cancellationToken);
        if (previous is not null)
        {
            return previous;
        }

        var now = clock.UtcNow.ToUniversalTime();
        var observedAt = StateValidation.NormalizeTime(command.ObservedAt, now);
        var revisions = values.Select(v => new AssessmentRevision(command.ProfileId, Guid.NewGuid(), 1,
            v.Metric, v.Score, observedAt, now, originalObservedAt: command.ObservedAt, comment: command.Comment)).ToArray();
        return await repository.CommitAsync(new(command.ProfileId, command.CommandId, command.IdempotencyKey,
            fingerprint, Array.AsReadOnly(revisions)), cancellationToken);
    }
}
