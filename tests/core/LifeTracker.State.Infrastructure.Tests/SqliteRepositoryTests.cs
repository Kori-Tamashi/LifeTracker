using LifeTracker.Application.Abstractions;
using LifeTracker.Infrastructure.Shared;
using LifeTracker.State.Application;
using LifeTracker.State.Contracts;
using LifeTracker.State.Domain;
using LifeTracker.State.Infrastructure;
using LifeTracker.Tests.Contracts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LifeTracker.State.Infrastructure.Tests;

public sealed class SqliteRepositoryContract : StateRepositoryContract
{
    protected override async ValueTask<Scope> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "lifetracker-synthetic-" + Guid.NewGuid().ToString("N"));
        var store = await SqliteStore.CreateAsync(root, Ct);
        var profile = Guid.NewGuid();
        await using var command = store.Connection.CreateCommand();
        command.CommandText = "INSERT INTO profiles(id) VALUES ($id);";
        command.Parameters.AddWithValue("$id", profile.ToString("D"));
        await command.ExecuteNonQueryAsync(Ct);
        return new Scope(new SqliteStateRepository(store), profile, async () =>
        {
            await store.DisposeAsync();
            Directory.Delete(root, true);
        });
    }
}

public sealed class SqliteRepositoryTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lifetracker-synthetic-" + Guid.NewGuid().ToString("N"));
    private readonly Guid profile = Guid.NewGuid();
    private SqliteStore store = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-15T12:00:00.1234567Z");
    private sealed class Clock : IUtcClock { public DateTimeOffset UtcNow => Now; }
    private RecordAssessmentsCommand CreateCommand() => new(profile, Guid.NewGuid(), Guid.NewGuid().ToString("D"), Now,
        [new("energy", 8), new("mood", 7.5m)]);
    private Task<AssessmentReceipt> RecordAsync(RecordAssessmentsCommand command) =>
        new RecordAssessmentsHandler(new SqliteStateRepository(store), new Clock()).ExecuteAsync(command, Ct);

    public async ValueTask InitializeAsync()
    {
        store = await SqliteStore.CreateAsync(root, Ct);
        await ExecuteAsync("INSERT INTO profiles(id) VALUES ($id);", ("$id", profile.ToString("D")));
    }
    public async ValueTask DisposeAsync()
    {
        await store.DisposeAsync();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = store.Connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    [Fact]
    public async Task RestartPreservesExactSnapshotsAndDedupReceipt()
    {
        var command = CreateCommand();
        var first = await RecordAsync(command);
        await store.DisposeAsync();
        store = await SqliteStore.OpenExistingAsync(root, Ct);
        var retry = await RecordAsync(command);
        Assert.Equal(first.CommandId, retry.CommandId);
        Assert.Equal(first.Assessments.ToArray(), retry.Assessments.ToArray());
        var records = await new SqliteStateRepository(store).GetCurrentAsync(profile, Ct);
        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal(Now, r.ObservedAt));
    }

    [Fact]
    public async Task MidBatchSqlFailureRollsBackEntitiesRevisionsAndReceipt()
    {
        await ExecuteAsync("""
            CREATE TRIGGER synthetic_failure BEFORE INSERT ON observations WHEN NEW.metric='mood'
            BEGIN SELECT RAISE(ABORT,'synthetic failure'); END;
            """);
        var command = CreateCommand();
        await Assert.ThrowsAsync<SqliteException>(() => RecordAsync(command));
        Assert.Empty(await new SqliteStateRepository(store).GetCurrentAsync(profile, Ct));
        foreach (var table in new[] { "entities", "revisions", "source_events" })
        {
            await using var count = store.Connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM {table};";
            Assert.Equal(0L, await count.ExecuteScalarAsync(Ct));
        }
        await ExecuteAsync("DROP TRIGGER synthetic_failure;");
        Assert.Equal(2, (await RecordAsync(command)).Assessments.Count);
    }

    [Fact]
    public async Task ParallelRetriesAcrossRepositoryInstancesHaveOneLogicalResult()
    {
        var command = CreateCommand();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => RecordAsync(command), Ct)));
        Assert.All(results, r => Assert.Equal(results[0].Assessments.ToArray(), r.Assessments.ToArray()));
        Assert.Equal(2, (await new SqliteStateRepository(store).GetCurrentAsync(profile, Ct)).Count);
    }

    [Fact]
    public async Task AtomicExpectedRevisionAllowsOnlyOneCompetingCorrection()
    {
        var receipt = await RecordAsync(CreateCommand());
        var id = receipt.Assessments[0].Id;
        var repository = new SqliteStateRepository(store);
        var current = (await repository.GetAsync(profile, id, Ct))!;
        var revised = new AssessmentRevision(profile, id, 2, current.Metric, new Score(6), Now, Now, comment: current.Comment, correctionReason: "Synthetic correction");
        async Task<bool> Attempt(int number)
        {
            try
            {
                await new SqliteStateRepository(store).CorrectAsync(new(profile, Guid.NewGuid(), "synthetic-correction-" + number,
                    "synthetic-fingerprint-" + number, 1, revised), Ct);
                return true;
            }
            catch (RevisionConflictException) { return false; }
        }
        var outcomes = await Task.WhenAll(Task.Run(() => Attempt(1), Ct), Task.Run(() => Attempt(2), Ct));
        Assert.Single(outcomes, x => x);
        Assert.Equal(2, (await repository.GetHistoryAsync(profile, id, Ct)).Count);
    }

    [Fact]
    public async Task FailedCorrectionDoesNotAdvanceCurrentPointer()
    {
        var receipt = await RecordAsync(CreateCommand());
        var id = receipt.Assessments[0].Id;
        await ExecuteAsync("CREATE TRIGGER synthetic_revision_failure BEFORE INSERT ON revisions WHEN NEW.revision=2 BEGIN SELECT RAISE(ABORT,'synthetic failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => new ReviseAssessmentHandler(new SqliteStateRepository(store), new Clock())
            .ExecuteAsync(new(profile, Guid.NewGuid(), "synthetic-correction", id, 1, Now, 6, "Synthetic correction"), Ct));
        Assert.Equal(1, (await new SqliteStateRepository(store).GetAsync(profile, id, Ct))!.Revision);
        Assert.Single(await new SqliteStateRepository(store).GetHistoryAsync(profile, id, Ct));
    }

    [Fact]
    public async Task SnapshotDriftFailsClosed()
    {
        await RecordAsync(CreateCommand());
        await ExecuteAsync("UPDATE observations SET observed_ms=observed_ms+1;");
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteStateRepository(store).GetCurrentAsync(profile, Ct));
    }

    [Fact]
    public async Task UnknownSchemaCannotBeOpenedOrSilentlyMigrated()
    {
        await ExecuteAsync("PRAGMA user_version=99;");
        await store.DisposeAsync();
        await Assert.ThrowsAsync<NotSupportedException>(() => SqliteStore.OpenExistingAsync(root, Ct));
    }
    [Fact]
    public async Task CommandIdentityCannotBeReusedUnderAnotherKey()
    {
        var command = CreateCommand();
        await RecordAsync(command);
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => RecordAsync(command with { IdempotencyKey = "different-synthetic-key" }));
        Assert.Equal(2, (await new SqliteStateRepository(store).GetCurrentAsync(profile, Ct)).Count);
    }

    [Fact]
    public async Task AiProvenanceAndOriginalTimestampSurvivePersistence()
    {
        var id = Guid.NewGuid();
        var original = Now.AddMinutes(1);
        var revision = new AssessmentRevision(profile, id, 1, StateMetric.Anxiety, new Score(3.5m), Now, Now,
            AssessmentSource.AiEstimate, originalObservedAt: original, comment: "Synthetic observation");
        var repository = new SqliteStateRepository(store);
        await repository.CommitAsync(new(profile, Guid.NewGuid(), "synthetic-ai-command", "synthetic-ai-fingerprint", [revision]), Ct);
        var actual = (await repository.GetAsync(profile, id, Ct))!;
        Assert.Equal(revision, actual);
    }

    [Fact]
    public async Task CorrectionCannotChangeMetricOrSource()
    {
        var receipt = await RecordAsync(CreateCommand());
        var id = receipt.Assessments[0].Id;
        var repository = new SqliteStateRepository(store);
        var invalid = new AssessmentRevision(profile, id, 2, StateMetric.Anxiety, new Score(6), Now, Now);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.CorrectAsync(
            new(profile, Guid.NewGuid(), "synthetic-invalid-correction", "synthetic-fingerprint", 1, invalid), Ct));
        Assert.Equal(1, (await repository.GetAsync(profile, id, Ct))!.Revision);
    }

    [Fact]
    public async Task StateChangeAndReceiptProduceOneOutboxEventOnRetry()
    {
        var command = CreateCommand();
        await RecordAsync(command);
        await RecordAsync(command);
        await using var query = store.Connection.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM outbox WHERE topic='state.changed';";
        Assert.Equal(1L, await query.ExecuteScalarAsync(Ct));
    }

    [Fact]
    public async Task OutboxFailureRollsBackStateRevisionAndReceipt()
    {
        await ExecuteAsync("CREATE TRIGGER synthetic_outbox_failure BEFORE INSERT ON outbox BEGIN SELECT RAISE(ABORT,'synthetic failure'); END;");
        var command = CreateCommand();
        await Assert.ThrowsAsync<SqliteException>(() => RecordAsync(command));
        Assert.Empty(await new SqliteStateRepository(store).GetCurrentAsync(profile, Ct));
        foreach (var table in new[] { "entities", "revisions", "source_events", "outbox" })
        {
            await using var count = store.Connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM {table};";
            Assert.Equal(0L, await count.ExecuteScalarAsync(Ct));
        }
        await ExecuteAsync("DROP TRIGGER synthetic_outbox_failure;");
        Assert.Equal(2, (await RecordAsync(command)).Assessments.Count);
    }

}
