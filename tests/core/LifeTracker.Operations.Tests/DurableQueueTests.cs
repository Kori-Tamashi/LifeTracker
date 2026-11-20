using System.Text.RegularExpressions;
using LifeTracker.Application.Abstractions;
using LifeTracker.Infrastructure.Shared;
using LifeTracker.Operations.Application;
using LifeTracker.Operations.Domain;
using LifeTracker.Operations.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LifeTracker.Operations.Tests;

public sealed class DurableQueueTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lifetracker-synthetic-" + Guid.NewGuid().ToString("N"));
    private readonly Guid profile = Guid.NewGuid();
    private SqliteStore store = null!;
    private readonly Clock clock = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(10);
    private sealed class Clock : IUtcClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-01-15T12:00:00Z"); }
    private sealed class Jitter : IRetryJitter { public double NextUnit() => 0.5; }
    private SqliteJobQueue Queue => new(store, clock, new Jitter());
    private SqliteOutboxStore Outbox => new(store, clock);
    private JobSubmission Input(string? external = null) => new(profile, Guid.NewGuid(), Guid.NewGuid(), "synthetic.channel",
        external ?? Guid.NewGuid().ToString("D"), "synthetic.process", "{\"text\":\"synthetic input\"}");
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
    private async Task<long> CountAsync(string table)
    {
        await using var command = store.Connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct));
    }
    public async ValueTask InitializeAsync()
    {
        store = await SqliteStore.CreateAsync(root, Ct);
        await ExecuteAsync("INSERT INTO profiles(id) VALUES ($profile);", ("$profile", profile.ToString("D")));
    }
    public async ValueTask DisposeAsync()
    {
        await store.DisposeAsync();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AcceptanceIsAtomicDurableAndIdempotent()
    {
        var input = Input();
        var receipt = await Queue.EnqueueAsync(input, Ct);
        var retry = await Queue.EnqueueAsync(input with { CommandId = Guid.NewGuid(), JobId = Guid.NewGuid(), InputJson = "{ \"text\" : \"synthetic input\" }" }, Ct);
        Assert.Equal(receipt, retry);
        Assert.Equal(1, await CountAsync("source_events"));
        Assert.Equal(1, await CountAsync("jobs"));
        Assert.Equal(1, await CountAsync("outbox"));
        await Assert.ThrowsAsync<JobConflictException>(() => Queue.EnqueueAsync(input with { InputJson = "{}" }, Ct));
        await store.DisposeAsync();
        store = await SqliteStore.OpenExistingAsync(root, Ct);
        Assert.Equal(receipt, await Queue.EnqueueAsync(input, Ct));
        var claimed = await Queue.ClaimAsync(profile, "worker-a", Lease, Ct);
        Assert.NotNull(claimed);
        Assert.Contains("synthetic input", claimed.InputJson);
    }

    [Fact]
    public async Task OutboxFailureRollsBackSourceJobAndReceipt()
    {
        await ExecuteAsync("CREATE TRIGGER synthetic_fail BEFORE INSERT ON outbox BEGIN SELECT RAISE(ABORT,'synthetic failure'); END;");
        var input = Input();
        await Assert.ThrowsAsync<SqliteException>(() => Queue.EnqueueAsync(input, Ct));
        Assert.Equal(0, await CountAsync("jobs"));
        Assert.Equal(0, await CountAsync("source_events"));
        await ExecuteAsync("DROP TRIGGER synthetic_fail;");
        Assert.Equal(input.JobId, (await Queue.EnqueueAsync(input, Ct)).JobId);
    }

    [Fact]
    public async Task ConcurrentAcceptAndClaimHaveSingleWinner()
    {
        var input = Input();
        var receipts = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => Queue.EnqueueAsync(input, Ct), Ct)));
        Assert.All(receipts, r => Assert.Equal(input.JobId, r.JobId));
        var claims = await Task.WhenAll(Enumerable.Range(0, 10).Select(n => Task.Run(() => Queue.ClaimAsync(profile, "worker-" + n, Lease, Ct), Ct)));
        Assert.Single(claims, x => x is not null);
        Assert.Equal(1, (await Queue.GetAsync(profile, input.JobId, Ct))!.Attempts);
    }

    [Fact]
    public async Task ExpiredLeaseCannotCompleteEvenBeforeReclaim()
    {
        var input = Input();
        await Queue.EnqueueAsync(input, Ct);
        var first = (await Queue.ClaimAsync(profile, "worker-a", Lease, Ct))!;
        clock.UtcNow = first.LeaseUntil;
        Assert.False(await Queue.CompleteAsync(first.Token, "{}", Ct));
        Assert.False(await Queue.HeartbeatAsync(first.Token, Lease, Ct));
        Assert.False(await Queue.FailAsync(first.Token, JobFailureKind.Timeout, Ct));
        Assert.Equal(JobState.Running, (await Queue.GetAsync(profile, input.JobId, Ct))!.State);
        var next = (await Queue.ClaimAsync(profile, "worker-b", Lease, Ct))!;
        Assert.True(next.Token.Fence > first.Token.Fence);
        Assert.Equal(2, next.Attempt);
        Assert.False(await Queue.CompleteAsync(first.Token, "{}", Ct));
        Assert.True(await Queue.CompleteAsync(next.Token, "{\"done\":true}", Ct));
        Assert.False(await Queue.CompleteAsync(next.Token, "{}", Ct));
    }

    [Fact]
    public async Task HeartbeatExtendsWithoutChangingAttemptOrShorteningLease()
    {
        var input = Input();
        await Queue.EnqueueAsync(input, Ct);
        var claim = (await Queue.ClaimAsync(profile, "worker-a", Lease, Ct))!;
        Assert.True(await Queue.HeartbeatAsync(claim.Token, TimeSpan.FromSeconds(1), Ct));
        Assert.Equal(claim.LeaseUntil, (await Queue.GetAsync(profile, input.JobId, Ct))!.LeaseUntil);
        clock.UtcNow += TimeSpan.FromSeconds(5);
        Assert.True(await Queue.HeartbeatAsync(claim.Token, Lease, Ct));
        clock.UtcNow = claim.LeaseUntil;
        Assert.Null(await Queue.ClaimAsync(profile, "worker-b", Lease, Ct));
        Assert.True(await Queue.CompleteAsync(claim.Token, "{}", Ct));
    }

    [Fact]
    public async Task HeartbeatAndCompletionRequireExactProfileOwnerAndFence()
    {
        await Queue.EnqueueAsync(Input(), Ct);
        var claim = (await Queue.ClaimAsync(profile, "worker-a", Lease, Ct))!;
        foreach (var wrong in new[] { claim.Token with { Owner = "worker-b" }, claim.Token with { Fence = claim.Token.Fence + 1 },
            claim.Token with { ProfileId = Guid.NewGuid() }, claim.Token with { JobId = Guid.NewGuid() } })
        {
            Assert.False(await Queue.HeartbeatAsync(wrong, Lease, Ct));
            Assert.False(await Queue.CompleteAsync(wrong, "{}", Ct));
        }
        Assert.True(await Queue.CompleteAsync(claim.Token, "{}", Ct));
    }

    [Fact]
    public async Task RetryWaitHonorsDueTimeAndFiveAttemptLimit()
    {
        var input = Input();
        await Queue.EnqueueAsync(input, Ct);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var claim = (await Queue.ClaimAsync(profile, "worker-a", Lease, Ct))!;
            Assert.Equal(attempt, claim.Attempt);
            Assert.True(await Queue.FailAsync(claim.Token, JobFailureKind.Timeout, Ct));
            var current = (await Queue.GetAsync(profile, input.JobId, Ct))!;
            if (attempt < 5)
            {
                Assert.Equal(JobState.RetryWait, current.State);
                Assert.Null(await Queue.ClaimAsync(profile, "worker-a", Lease, Ct));
                clock.UtcNow = current.ReadyAt;
            }
            else
            {
                Assert.Equal(JobState.Failed, current.State);
            }
        }
        clock.UtcNow += TimeSpan.FromDays(1);
        Assert.Null(await Queue.ClaimAsync(profile, "worker-a", Lease, Ct));
        Assert.Equal(5, (await Queue.GetAsync(profile, input.JobId, Ct))!.Attempts);
    }

    [Fact]
    public async Task AbandonedLeasesExhaustAttemptsAndReopenPreservesFences()
    {
        var input = Input();
        await Queue.EnqueueAsync(input, Ct);
        ClaimedJob? previous = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var current = (await Queue.ClaimAsync(profile, "worker-a", Lease, Ct))!;
            Assert.Equal(attempt, current.Attempt);
            if (previous is not null)
            {
                Assert.True(current.Token.Fence > previous.Token.Fence);
                Assert.False(await Queue.CompleteAsync(previous.Token, "{}", Ct));
            }
            await store.DisposeAsync();
            store = await SqliteStore.OpenExistingAsync(root, Ct);
            clock.UtcNow = current.LeaseUntil;
            previous = current;
        }
        Assert.Null(await Queue.ClaimAsync(profile, "worker-b", Lease, Ct));
        var failed = (await Queue.GetAsync(profile, input.JobId, Ct))!;
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Equal(5, failed.Attempts);
        Assert.True(failed.Fence > previous!.Token.Fence);
    }

    [Theory]
    [InlineData(JobFailureKind.Validation)]
    [InlineData(JobFailureKind.Authorization)]
    [InlineData(JobFailureKind.Conflict)]
    public async Task NonTransientFailuresStopImmediately(JobFailureKind failure)
    {
        var input = Input();
        await Queue.EnqueueAsync(input, Ct);
        var claim = (await Queue.ClaimAsync(profile, "worker", Lease, Ct))!;
        Assert.True(await Queue.FailAsync(claim.Token, failure, Ct));
        Assert.Equal(JobState.Failed, (await Queue.GetAsync(profile, input.JobId, Ct))!.State);
        Assert.Null(await Queue.ClaimAsync(profile, "worker", Lease, Ct));
    }

    [Fact]
    public async Task CancelInvalidatesRunningLeaseAndIsIdempotent()
    {
        var input = Input();
        await Queue.EnqueueAsync(input, Ct);
        var claim = (await Queue.ClaimAsync(profile, "worker", Lease, Ct))!;
        Assert.True(await Queue.CancelAsync(profile, input.JobId, Ct));
        Assert.False(await Queue.CancelAsync(profile, input.JobId, Ct));
        Assert.False(await Queue.CompleteAsync(claim.Token, "{}", Ct));
        Assert.Equal(JobState.Cancelled, (await Queue.GetAsync(profile, input.JobId, Ct))!.State);
        Assert.Equal(2, await CountAsync("outbox"));
    }

    [Fact]
    public async Task FailedCompletionTransactionCanBeRetriedWithSameLease()
    {
        var input = Input();
        await Queue.EnqueueAsync(input, Ct);
        var claim = (await Queue.ClaimAsync(profile, "worker", Lease, Ct))!;
        await ExecuteAsync("CREATE TRIGGER synthetic_fail BEFORE INSERT ON outbox WHEN NEW.topic='job.completed' BEGIN SELECT RAISE(ABORT,'synthetic failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Queue.CompleteAsync(claim.Token, "{}", Ct));
        Assert.Equal(JobState.Running, (await Queue.GetAsync(profile, input.JobId, Ct))!.State);
        await ExecuteAsync("DROP TRIGGER synthetic_fail;");
        Assert.True(await Queue.CompleteAsync(claim.Token, "{}", Ct));
    }

    [Fact]
    public async Task OutboxIsAtLeastOnceWithStableIdsUntilAcknowledged()
    {
        await Queue.EnqueueAsync(Input(), Ct);
        var first = Assert.Single(await Outbox.ReadPendingAsync(profile, 10, Ct));
        await store.DisposeAsync();
        store = await SqliteStore.OpenExistingAsync(root, Ct);
        Assert.Equal(first, Assert.Single(await Outbox.ReadPendingAsync(profile, 10, Ct)));
        Assert.Empty(await Outbox.ReadPendingAsync(Guid.NewGuid(), 10, Ct));
        Assert.False(await Outbox.MarkDispatchedAsync(Guid.NewGuid(), first.Id, Ct));
        Assert.True(await Outbox.MarkDispatchedAsync(profile, first.Id, Ct));
        Assert.False(await Outbox.MarkDispatchedAsync(profile, first.Id, Ct));
        Assert.Empty(await Outbox.ReadPendingAsync(profile, 10, Ct));
    }

    [Fact]
    public async Task PreCancelledInputCannotBeAccepted()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Queue.EnqueueAsync(Input(), cancellation.Token));
        Assert.Equal(0, await CountAsync("jobs"));
    }

    [Fact]
    public async Task JobAndOutboxDdlMatchesNormativeSchema()
    {
        var baseline = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures/schema-v1.sql"), Ct);
        foreach (var name in new[] { "jobs", "jobs_due", "outbox" })
        {
            var expected = Regex.Match(baseline, @"CREATE (?:TABLE|INDEX) " + name + @"\b.*?;", RegexOptions.Singleline).Value;
            Assert.NotEmpty(expected);
            await using var command = store.Connection.CreateCommand();
            command.CommandText = "SELECT sql FROM sqlite_master WHERE name=$name;";
            command.Parameters.AddWithValue("$name", name);
            var actual = Convert.ToString(await command.ExecuteScalarAsync(Ct))!;
            static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim().TrimEnd(';');
            Assert.Equal(Normalize(expected), Normalize(actual));
        }
    }
    [Fact]
    public async Task LegacyMigrationSetIsRejectedWithoutApplyingPendingDdl()
    {
        await ExecuteAsync("DROP TABLE jobs; DROP TABLE outbox; DELETE FROM __EFMigrationsHistory WHERE MigrationId='202611200002_DurableJobs';");
        await store.DisposeAsync();
        await Assert.ThrowsAsync<NotSupportedException>(() => SqliteStore.OpenExistingAsync(root, Ct));
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path.Combine(root, "lifetracker.sqlite"), Pooling = false }.ToString());
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='jobs';";
        Assert.Equal(0L, await command.ExecuteScalarAsync(Ct));
        command.CommandText = "SELECT COUNT(*) FROM profiles;";
        Assert.Equal(1L, await command.ExecuteScalarAsync(Ct));
    }

}
