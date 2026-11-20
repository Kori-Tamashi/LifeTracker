using LifeTracker.Application.Abstractions;
using LifeTracker.State.Application;
using LifeTracker.State.Contracts;
using LifeTracker.State.Domain;
using Xunit;

namespace LifeTracker.State.Application.Tests;

public sealed class StateScenarios
{
    private static readonly Guid Profile = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-15T12:00:00Z");
    private readonly MemoryRepository repository = new();
    private readonly Clock clock = new() { UtcNow = Now };
    private static RecordAssessmentsCommand Command(params AssessmentValue[] values) =>
        new(Profile, Guid.NewGuid(), Guid.NewGuid().ToString(), Now, values.Length == 0 ? [new("energy", 8)] : values);

    [Fact]
    public async Task BatchRecordsPartialStateWithoutInventingOtherMetrics()
    {
        var receipt = await new RecordAssessmentsHandler(repository, clock).ExecuteAsync(Command(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(receipt.Assessments);
        var state = await new CurrentStateQuery(repository, clock).ExecuteAsync(Profile, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(state.Balance);
        Assert.Equal("undefined", state.Level);
        Assert.Equal("self_report", state.Metrics.Single(m => m.Metric == "energy").SourceKind);
        Assert.Equal(3, state.Metrics.Count(m => m.SourceKind == "unknown"));
    }

    [Fact]
    public async Task InvalidBatchDoesNotWriteAnyValue()
    {
        var handler = new RecordAssessmentsHandler(repository, clock);
        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(Command(new AssessmentValue("energy", 8), new("energy", 9)), cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.ExecuteAsync(Command(new AssessmentValue("energy", 8), new("mood", 7.25m)), cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(Command(new AssessmentValue("other", 8)), cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(Command() with { Values = [] }, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteAsync(Command() with { Values = [null!] }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(repository.History);
    }

    [Fact]
    public async Task RetryingCanonicalPayloadReturnsOriginalReceiptEvenAfterTimeChanges()
    {
        var command = Command(new AssessmentValue("energy", 8), new("mood", 7.5m));
        var handler = new RecordAssessmentsHandler(repository, clock);
        var first = await handler.ExecuteAsync(command, cancellationToken: TestContext.Current.CancellationToken);
        clock.UtcNow = Now.AddDays(1);
        var retry = await handler.ExecuteAsync(command with
        {
            CommandId = Guid.NewGuid(),
            ObservedAt = Now.ToOffset(TimeSpan.FromHours(3)),
            Values = [new("mood", 7.50m), new("energy", 8.0m)]
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(first, retry);
        Assert.Equal(2, repository.History.Count);
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => handler.ExecuteAsync(command with { Comment = "Different synthetic payload" }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoMinuteClockSkewIsClampedAndOriginalTimePreserved()
    {
        await new RecordAssessmentsHandler(repository, clock).ExecuteAsync(Command() with { ObservedAt = Now.AddMinutes(2) }, cancellationToken: TestContext.Current.CancellationToken);
        var saved = Assert.Single(repository.History);
        Assert.Equal(Now, saved.ObservedAt);
        Assert.Equal(Now.AddMinutes(2), saved.OriginalObservedAt);
        await Assert.ThrowsAsync<ArgumentException>(() => new RecordAssessmentsHandler(repository, clock)
            .ExecuteAsync(Command() with { ObservedAt = Now.AddMinutes(2).AddTicks(1) }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single(repository.History);
    }

    [Fact]
    public async Task CorrectionAppendsHistoryAndIsIdempotentBeforeCheckingExpectedRevision()
    {
        var receipt = await new RecordAssessmentsHandler(repository, clock).ExecuteAsync(Command(), cancellationToken: TestContext.Current.CancellationToken);
        var id = receipt.Assessments[0].Id;
        var correction = new ReviseAssessmentCommand(Profile, Guid.NewGuid(), "correction-001", id, 1, Now.AddHours(-1), 6, "Corrected synthetic entry");
        var handler = new ReviseAssessmentHandler(repository, clock);
        var revised = await handler.ExecuteAsync(correction, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, revised.Assessments[0].Revision);
        Assert.Equal(revised, await handler.ExecuteAsync(correction, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2, repository.History.Count);
        Assert.Equal(8m, repository.History[0].Score.Value);
        Assert.Equal(6m, repository.History[1].Score.Value);
        Assert.Equal(correction.Reason, repository.History[1].CorrectionReason);
        await Assert.ThrowsAsync<RevisionConflictException>(() => handler.ExecuteAsync(correction with { IdempotencyKey = "another-correction" }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingAndForeignIdentityCannotBeCorrected()
    {
        await new RecordAssessmentsHandler(repository, clock).ExecuteAsync(Command(), cancellationToken: TestContext.Current.CancellationToken);
        var command = new ReviseAssessmentCommand(Guid.NewGuid(), Guid.NewGuid(), "correction-002", repository.History[0].AssessmentId, 1, Now, 6, "Synthetic correction");
        await Assert.ThrowsAsync<AssessmentNotFoundException>(() => new ReviseAssessmentHandler(repository, clock).ExecuteAsync(command, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single(repository.History);
    }

    [Fact]
    public async Task QueryPreservesStaleValuesButDoesNotCalculateCurrentBalance()
    {
        await new RecordAssessmentsHandler(repository, clock).ExecuteAsync(Command(new AssessmentValue("energy", 8), new("mood", 8), new("anxiety", 2), new("physical_wellbeing", 8)), cancellationToken: TestContext.Current.CancellationToken);
        var query = new CurrentStateQuery(repository, clock);
        Assert.Equal(8m, (await query.ExecuteAsync(Profile, cancellationToken: TestContext.Current.CancellationToken)).Balance);
        clock.UtcNow = Now.AddHours(6);
        var stale = await query.ExecuteAsync(Profile, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(stale.Balance);
        Assert.All(stale.Metrics, m => { Assert.False(m.Fresh); Assert.NotNull(m.Score); });
        Assert.All((await query.ExecuteAsync(Guid.NewGuid(), cancellationToken: TestContext.Current.CancellationToken)).Metrics, m => Assert.Null(m.Score));
    }

    [Fact]
    public async Task CancelledCommandCannotWrite()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RecordAssessmentsHandler(repository, clock).ExecuteAsync(Command(), cancellation.Token));
        Assert.Empty(repository.History);
    }

    private sealed class Clock : IUtcClock { public DateTimeOffset UtcNow { get; set; } }

    // Test double only. Real adapter must pass transaction/concurrency contracts independently.
    private sealed class MemoryRepository : IStateRepository
    {
        public List<AssessmentRevision> History { get; } = [];
        private readonly Dictionary<(Guid, string), (string Fingerprint, AssessmentReceipt Receipt)> receipts = [];
        public Task<AssessmentReceipt?> FindReceiptAsync(Guid profileId, string key, string fingerprint, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!receipts.TryGetValue((profileId, key), out var stored))
            {
                return Task.FromResult<AssessmentReceipt?>(null);
            }

            if (stored.Fingerprint != fingerprint)
            {
                throw new IdempotencyConflictException();
            }

            return Task.FromResult<AssessmentReceipt?>(stored.Receipt);
        }
        public Task<IReadOnlyList<AssessmentRevision>> GetCurrentAsync(Guid profileId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<AssessmentRevision>>(History.Where(r => r.ProfileId == profileId).ToArray());
        }
        public Task<IReadOnlyList<AssessmentRevision>> GetHistoryAsync(Guid profileId, Guid assessmentId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<AssessmentRevision>>(History.Where(r => r.ProfileId == profileId && r.AssessmentId == assessmentId).OrderBy(r => r.Revision).ToArray());
        }
        public Task<AssessmentRevision?> GetAsync(Guid profileId, Guid id, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(History.Where(r => r.ProfileId == profileId && r.AssessmentId == id).MaxBy(r => r.Revision));
        }
        public async Task<AssessmentReceipt> CommitAsync(AssessmentBatch batch, CancellationToken ct)
        {
            var previous = await FindReceiptAsync(batch.ProfileId, batch.IdempotencyKey, batch.Fingerprint, ct);
            if (previous is not null)
            {
                return previous;
            }

            ct.ThrowIfCancellationRequested();
            History.AddRange(batch.Revisions);
            var receipt = new AssessmentReceipt(batch.CommandId, batch.Revisions.Select(r => new SavedAssessment(r.AssessmentId, r.Revision)).ToArray());
            receipts.Add((batch.ProfileId, batch.IdempotencyKey), (batch.Fingerprint, receipt));
            return receipt;
        }
        public async Task<AssessmentReceipt> CorrectAsync(AssessmentCorrection correction, CancellationToken ct)
        {
            var previous = await FindReceiptAsync(correction.ProfileId, correction.IdempotencyKey, correction.Fingerprint, ct);
            if (previous is not null)
            {
                return previous;
            }

            var current = await GetAsync(correction.ProfileId, correction.NewRevision.AssessmentId, ct) ?? throw new AssessmentNotFoundException();
            if (current.Revision != correction.ExpectedRevision)
            {
                throw new RevisionConflictException();
            }

            return await CommitAsync(new(correction.ProfileId, correction.CommandId, correction.IdempotencyKey, correction.Fingerprint, [correction.NewRevision]), ct);
        }
    }
    public sealed class MemoryContract : LifeTracker.Tests.Contracts.StateRepositoryContract
    {
        protected override ValueTask<Scope> CreateAsync() =>
            ValueTask.FromResult(new Scope(new MemoryRepository(), Guid.NewGuid(), () => ValueTask.CompletedTask));
    }

}
