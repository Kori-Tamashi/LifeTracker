using LifeTracker.Application.Abstractions;
using LifeTracker.State.Application;
using LifeTracker.State.Contracts;
using LifeTracker.State.Domain;
using Xunit;

namespace LifeTracker.Tests.Contracts;

public abstract class StateRepositoryContract
{
    protected sealed record Scope(IStateRepository Repository, Guid ProfileId, Func<ValueTask> Cleanup) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Cleanup();
    }
    protected abstract ValueTask<Scope> CreateAsync();
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;
    protected static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-15T12:00:00.1234567Z");
    private sealed class Clock : IUtcClock { public DateTimeOffset UtcNow => Now; }
    private static RecordAssessmentsCommand Command(Guid profileId) => new(profileId, Guid.NewGuid(), Guid.NewGuid().ToString("D"),
        Now, [new("energy", 8), new("mood", 7.5m)]);

    [Fact]
    public async Task PartialBatchAndExactTimestampRoundTrip()
    {
        await using var scope = await CreateAsync();
        var command = Command(scope.ProfileId);
        var receipt = await new RecordAssessmentsHandler(scope.Repository, new Clock()).ExecuteAsync(command, Ct);
        Assert.Equal(2, receipt.Assessments.Count);
        var current = await scope.Repository.GetCurrentAsync(scope.ProfileId, Ct);
        Assert.Equal(2, current.Count);
        Assert.All(current, r => Assert.Equal(Now, r.ObservedAt));
        Assert.Null((await new CurrentStateQuery(scope.Repository, new Clock()).ExecuteAsync(scope.ProfileId, Ct)).Balance);
    }

    [Fact]
    public async Task EquivalentRetryReturnsOriginalReceiptWithoutDuplicating()
    {
        await using var scope = await CreateAsync();
        var handler = new RecordAssessmentsHandler(scope.Repository, new Clock());
        var command = Command(scope.ProfileId);
        var first = await handler.ExecuteAsync(command, Ct);
        var retry = await handler.ExecuteAsync(command with { CommandId = Guid.NewGuid(), Values = command.Values.Reverse().ToArray() }, Ct);
        Assert.Equal(first.CommandId, retry.CommandId);
        Assert.Equal(first.Assessments.ToArray(), retry.Assessments.ToArray());
        Assert.Equal(2, (await scope.Repository.GetCurrentAsync(scope.ProfileId, Ct)).Count);
    }

    [Fact]
    public async Task DifferentPayloadCannotReuseIdempotencyKey()
    {
        await using var scope = await CreateAsync();
        var handler = new RecordAssessmentsHandler(scope.Repository, new Clock());
        var command = Command(scope.ProfileId);
        await handler.ExecuteAsync(command, Ct);
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => handler.ExecuteAsync(command with { Comment = "Different synthetic payload" }, Ct));
        Assert.Equal(2, (await scope.Repository.GetCurrentAsync(scope.ProfileId, Ct)).Count);
    }

    [Fact]
    public async Task CorrectionAppendsHistoryAndRetryDoesNotAppendAgain()
    {
        await using var scope = await CreateAsync();
        var created = await new RecordAssessmentsHandler(scope.Repository, new Clock()).ExecuteAsync(Command(scope.ProfileId), Ct);
        var id = created.Assessments[0].Id;
        var command = new ReviseAssessmentCommand(scope.ProfileId, Guid.NewGuid(), "synthetic-correction", id, 1, Now.AddHours(-1), 6, "Synthetic correction");
        var handler = new ReviseAssessmentHandler(scope.Repository, new Clock());
        var corrected = await handler.ExecuteAsync(command, Ct);
        var retried = await handler.ExecuteAsync(command, Ct);
        Assert.Equal(corrected.CommandId, retried.CommandId);
        var history = await scope.Repository.GetHistoryAsync(scope.ProfileId, id, Ct);
        Assert.Equal(new[] { 1, 2 }, history.Select(r => r.Revision).ToArray());
        Assert.Equal(8m, history[0].Score.Value);
        Assert.Equal(6m, history[1].Score.Value);
        Assert.Equal(2, (await scope.Repository.GetAsync(scope.ProfileId, id, Ct))!.Revision);
    }

    [Fact]
    public async Task StaleCorrectionCannotOverwriteNewRevision()
    {
        await using var scope = await CreateAsync();
        var created = await new RecordAssessmentsHandler(scope.Repository, new Clock()).ExecuteAsync(Command(scope.ProfileId), Ct);
        var command = new ReviseAssessmentCommand(scope.ProfileId, Guid.NewGuid(), "synthetic-correction", created.Assessments[0].Id, 1, Now, 6, "Synthetic correction");
        var handler = new ReviseAssessmentHandler(scope.Repository, new Clock());
        await handler.ExecuteAsync(command, Ct);
        await Assert.ThrowsAsync<RevisionConflictException>(() => handler.ExecuteAsync(command with { CommandId = Guid.NewGuid(), IdempotencyKey = "another-synthetic-correction" }, Ct));
        Assert.Equal(2, (await scope.Repository.GetHistoryAsync(scope.ProfileId, command.AssessmentId, Ct)).Count);
    }

    [Fact]
    public async Task ProfileScopeAppliesToCurrentHistoryIdentityAndReceipt()
    {
        await using var scope = await CreateAsync();
        var command = Command(scope.ProfileId);
        var receipt = await new RecordAssessmentsHandler(scope.Repository, new Clock()).ExecuteAsync(command, Ct);
        var other = Guid.NewGuid();
        Assert.Empty(await scope.Repository.GetCurrentAsync(other, Ct));
        Assert.Empty(await scope.Repository.GetHistoryAsync(other, receipt.Assessments[0].Id, Ct));
        Assert.Null(await scope.Repository.GetAsync(other, receipt.Assessments[0].Id, Ct));
        Assert.Null(await scope.Repository.FindReceiptAsync(other, command.IdempotencyKey, "synthetic-unused-hash", Ct));
    }

    [Fact]
    public async Task PreCancelledCommandCannotCommit()
    {
        await using var scope = await CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RecordAssessmentsHandler(scope.Repository, new Clock())
            .ExecuteAsync(Command(scope.ProfileId), cancellation.Token));
        Assert.Empty(await scope.Repository.GetCurrentAsync(scope.ProfileId, Ct));
    }
}
