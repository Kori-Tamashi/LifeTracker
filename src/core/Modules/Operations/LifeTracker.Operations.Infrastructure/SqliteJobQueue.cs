using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LifeTracker.Application.Abstractions;
using LifeTracker.Infrastructure.Shared;
using LifeTracker.Kernel;
using LifeTracker.Operations.Application;
using LifeTracker.Operations.Domain;
using Microsoft.Data.Sqlite;

namespace LifeTracker.Operations.Infrastructure;

/// <summary>Durable storage primitives. Job execution and delivery happen outside these transactions.</summary>
public sealed class SqliteJobQueue(SqliteStore store, IUtcClock clock, IRetryJitter jitter) : IJobQueue
{
    private sealed record Envelope(Guid SourceEventId, JsonElement Payload);
    private sealed record Running(int Attempts, Envelope Envelope);
    private DateTimeOffset Now => DateTimeOffset.FromUnixTimeMilliseconds(clock.UtcNow.ToUnixTimeMilliseconds());

    public Task<JobReceipt> EnqueueAsync(JobSubmission submission, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ct.ThrowIfCancellationRequested();
        Identity(submission.ProfileId, submission.JobId);
        Identifiers.RequireNonEmpty(submission.CommandId, nameof(submission.CommandId));
        ArgumentException.ThrowIfNullOrWhiteSpace(submission.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(submission.ExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(submission.Kind);
        var input = NormalizeJson(submission.InputJson);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { submission.Kind, input }))));
        return store.WithConnectionAsync(async () =>
        {
            var now = Now;
            await using var transaction = store.Connection.BeginTransaction(deferred: false);
            await using (var duplicate = Command("""
                SELECT payload_hash,receipt_json FROM source_events
                WHERE profile_id=$profile AND source=$source AND external_id=$external;
                """, transaction, ("$profile", Id(submission.ProfileId)), ("$source", submission.Source), ("$external", submission.ExternalId)))
            await using (var reader = await duplicate.ExecuteReaderAsync(ct))
            {
                if (await reader.ReadAsync(ct))
                {
                    if (reader.GetString(0) != hash)
                    {
                        throw new JobConflictException();
                    }

                    return JsonSerializer.Deserialize<JobReceipt>(reader.GetString(1)) ?? throw new InvalidDataException("Invalid job receipt.");
                }
            }
            await using (var collision = Command("""
                SELECT (SELECT COUNT(*) FROM source_events WHERE profile_id=$profile AND id=$command)
                     + (SELECT COUNT(*) FROM jobs WHERE profile_id=$profile AND id=$job);
                """, transaction, ("$profile", Id(submission.ProfileId)), ("$command", Id(submission.CommandId)), ("$job", Id(submission.JobId))))
            {
                if (Convert.ToInt64(await collision.ExecuteScalarAsync(ct)) != 0)
                {
                    throw new JobConflictException();
                }
            }

            using var document = JsonDocument.Parse(input);
            var envelope = JsonSerializer.Serialize(new Envelope(submission.CommandId, document.RootElement));
            var receipt = new JobReceipt(submission.CommandId, submission.JobId);
            await using (var insert = Command("""
                INSERT INTO source_events(profile_id,id,source,external_id,payload_hash,received_ms,state,receipt_json)
                VALUES ($profile,$command,$source,$external,$hash,$now,'accepted',$receipt);
                INSERT INTO jobs(profile_id,id,kind,idempotency_key,state,ready_ms,input_json)
                VALUES ($profile,$job,$kind,$key,'queued',$now,$input);
                """, transaction, ("$profile", Id(submission.ProfileId)), ("$command", Id(submission.CommandId)), ("$source", submission.Source),
                ("$external", submission.ExternalId), ("$hash", hash), ("$now", now.ToUnixTimeMilliseconds()),
                ("$receipt", JsonSerializer.Serialize(receipt)), ("$job", Id(submission.JobId)), ("$kind", submission.Kind),
                ("$key", JsonSerializer.Serialize(new[] { submission.Source, submission.ExternalId })), ("$input", envelope)))
            {
                await insert.ExecuteNonQueryAsync(ct);
            }

            await EventAsync(transaction, submission.ProfileId, submission.JobId, "accepted", 0, now, ct);
            ct.ThrowIfCancellationRequested();
            await transaction.CommitAsync(ct);
            return receipt;
        }, ct);
    }

    public Task<ClaimedJob?> ClaimAsync(Guid profileId, string owner, TimeSpan leaseDuration, CancellationToken ct)
    {
        Identifiers.RequireNonEmpty(profileId, nameof(profileId));
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ValidateDuration(leaseDuration);
        return store.WithConnectionAsync<ClaimedJob?>(async () =>
        {
            var now = Now;
            var until = now.Add(leaseDuration);
            await using var transaction = store.Connection.BeginTransaction(deferred: false);
            await ExhaustExpiredAsync(profileId, transaction, now, ct);
            Guid jobId;
            string kind;
            string body;
            int attempt;
            long fence;
            await using (var select = Command("""
                SELECT id,kind,input_json,attempts,fence FROM jobs WHERE profile_id=$profile AND attempts<$maximum
                 AND ((state IN ('queued','retry_wait') AND ready_ms<=$now)
                   OR (state='running' AND lease_until_ms<=$now))
                ORDER BY ready_ms,id LIMIT 1;
                """, transaction, ("$profile", Id(profileId)), ("$maximum", RetryPolicy.MaximumAttempts), ("$now", now.ToUnixTimeMilliseconds())))
            await using (var reader = await select.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                {
                    await transaction.CommitAsync(ct);
                    return null;
                }
                jobId = Guid.Parse(reader.GetString(0));
                kind = reader.GetString(1);
                body = reader.GetString(2);
                attempt = checked(reader.GetInt32(3) + 1);
                fence = checked(reader.GetInt64(4) + 1);
            }
            var envelope = ReadEnvelope(body);
            await using (var update = Command("""
                UPDATE jobs SET state='running',attempts=$attempt,fence=$fence,lease_owner=$owner,lease_until_ms=$until,result_json=NULL
                WHERE profile_id=$profile AND id=$job;
                """, transaction, ("$attempt", attempt), ("$fence", fence), ("$owner", owner), ("$until", until.ToUnixTimeMilliseconds()),
                ("$profile", Id(profileId)), ("$job", Id(jobId))))
            {
                await update.ExecuteNonQueryAsync(ct);
            }

            await SourceStateAsync(transaction, profileId, envelope.SourceEventId, "processing", ct);
            ct.ThrowIfCancellationRequested();
            await transaction.CommitAsync(ct);
            return new ClaimedJob(new(profileId, jobId, owner, fence), kind, envelope.Payload.GetRawText(), attempt, until);
        }, ct);
    }

    public Task<bool> HeartbeatAsync(LeaseToken token, TimeSpan leaseDuration, CancellationToken ct)
    {
        ValidateToken(token);
        ValidateDuration(leaseDuration);
        return store.WithConnectionAsync(async () =>
        {
            var now = Now;
            await using var command = Command("""
                UPDATE jobs SET lease_until_ms=MAX(lease_until_ms,$until)
                WHERE profile_id=$profile AND id=$job AND state='running' AND lease_owner=$owner AND fence=$fence AND lease_until_ms>$now;
                """, null, TokenParameters(token, now).Append(("$until", (object?)now.Add(leaseDuration).ToUnixTimeMilliseconds())).ToArray());
            return await command.ExecuteNonQueryAsync(ct) == 1;
        }, ct);
    }

    public Task<bool> CompleteAsync(LeaseToken token, string resultJson, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return FinishAsync(token, NormalizeJson(resultJson), null, ct);
    }

    public Task<bool> FailAsync(LeaseToken token, JobFailureKind failure, CancellationToken ct)
    {
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        return FinishAsync(token, JsonSerializer.Serialize(new { failure = failure.ToString().ToLowerInvariant() }), failure, ct);
    }

    private Task<bool> FinishAsync(LeaseToken token, string resultJson, JobFailureKind? failure, CancellationToken ct)
    {
        ValidateToken(token);
        return store.WithConnectionAsync(async () =>
        {
            var now = Now;
            await using var transaction = store.Connection.BeginTransaction(deferred: false);
            var running = await RunningAsync(token, transaction, now, ct);
            if (running is null)
            {
                return false;
            }

            var delay = failure is null ? null : RetryPolicy.Delay(running.Attempts, failure.Value, jitter.NextUnit());
            var state = failure is null ? "completed" : delay is null ? "failed" : "retry_wait";
            await using (var update = Command("""
                UPDATE jobs SET state=$state,ready_ms=$ready,result_json=$result,lease_owner=NULL,lease_until_ms=NULL
                WHERE profile_id=$profile AND id=$job AND state='running' AND lease_owner=$owner AND fence=$fence AND lease_until_ms>$now;
                """, transaction, TokenParameters(token, now).Concat(new (string, object?)[] {
                    ("$state",state), ("$ready", now.Add(delay ?? TimeSpan.Zero).ToUnixTimeMilliseconds()), ("$result",resultJson) }).ToArray()))
            {
                if (await update.ExecuteNonQueryAsync(ct) != 1)
                {
                    return false;
                }
            }

            await SourceStateAsync(transaction, token.ProfileId, running.Envelope.SourceEventId,
                state == "completed" ? "completed" : state == "failed" ? "failed" : "processing", ct);
            await EventAsync(transaction, token.ProfileId, token.JobId, state, token.Fence, now, ct);
            ct.ThrowIfCancellationRequested();
            await transaction.CommitAsync(ct);
            return true;
        }, ct);
    }

    public Task<bool> CancelAsync(Guid profileId, Guid jobId, CancellationToken ct)
    {
        Identity(profileId, jobId);
        return store.WithConnectionAsync(async () =>
        {
            var now = Now;
            await using var transaction = store.Connection.BeginTransaction(deferred: false);
            Envelope envelope;
            long fence;
            await using (var select = Command("""
                SELECT input_json,fence FROM jobs WHERE profile_id=$profile AND id=$job AND state IN ('queued','running','retry_wait');
                """, transaction, ("$profile", Id(profileId)), ("$job", Id(jobId))))
            await using (var reader = await select.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                {
                    return false;
                }

                envelope = ReadEnvelope(reader.GetString(0));
                fence = checked(reader.GetInt64(1) + 1);
            }
            await using (var update = Command("""
                UPDATE jobs SET state='cancelled',fence=$fence,lease_owner=NULL,lease_until_ms=NULL
                WHERE profile_id=$profile AND id=$job;
                """, transaction, ("$fence", fence), ("$profile", Id(profileId)), ("$job", Id(jobId))))
            {
                await update.ExecuteNonQueryAsync(ct);
            }

            await SourceStateAsync(transaction, profileId, envelope.SourceEventId, "failed", ct);
            await EventAsync(transaction, profileId, jobId, "cancelled", fence, now, ct);
            ct.ThrowIfCancellationRequested();
            await transaction.CommitAsync(ct);
            return true;
        }, ct);
    }

    public Task<JobSnapshot?> GetAsync(Guid profileId, Guid jobId, CancellationToken ct)
    {
        Identity(profileId, jobId);
        return store.WithConnectionAsync<JobSnapshot?>(async () =>
        {
            await using var command = Command("SELECT kind,state,attempts,fence,ready_ms,lease_until_ms,result_json FROM jobs WHERE profile_id=$profile AND id=$job;",
                null, ("$profile", Id(profileId)), ("$job", Id(jobId)));
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            var state = reader.GetString(1) switch
            {
                "queued" => JobState.Queued,
                "running" => JobState.Running,
                "retry_wait" => JobState.RetryWait,
                "completed" => JobState.Completed,
                "failed" => JobState.Failed,
                "cancelled" => JobState.Cancelled,
                _ => throw new InvalidDataException("Unknown job state.")
            };
            return new JobSnapshot(jobId, reader.GetString(0), state, reader.GetInt32(2), reader.GetInt64(3),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)), reader.IsDBNull(5) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)),
                reader.IsDBNull(6) ? null : reader.GetString(6));
        }, ct);
    }

    private async Task<Running?> RunningAsync(LeaseToken token, SqliteTransaction transaction, DateTimeOffset now, CancellationToken ct)
    {
        await using var command = Command("""
            SELECT attempts,input_json FROM jobs WHERE profile_id=$profile AND id=$job AND state='running'
            AND lease_owner=$owner AND fence=$fence AND lease_until_ms>$now;
            """, transaction, TokenParameters(token, now));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new Running(reader.GetInt32(0), ReadEnvelope(reader.GetString(1))) : null;
    }

    private async Task ExhaustExpiredAsync(Guid profileId, SqliteTransaction transaction, DateTimeOffset now, CancellationToken ct)
    {
        var expired = new List<(Guid Id, Envelope Envelope, long Fence)>();
        await using (var select = Command("""
            SELECT id,input_json,fence FROM jobs WHERE profile_id=$profile AND state='running' AND attempts>=$maximum AND lease_until_ms<=$now;
            """, transaction, ("$profile", Id(profileId)), ("$maximum", RetryPolicy.MaximumAttempts), ("$now", now.ToUnixTimeMilliseconds())))
        await using (var reader = await select.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                expired.Add((Guid.Parse(reader.GetString(0)), ReadEnvelope(reader.GetString(1)), checked(reader.GetInt64(2) + 1)));
            }
        }

        foreach (var expiredJob in expired)
        {
            await using var update = Command("""
                UPDATE jobs SET state='failed',fence=$fence,lease_owner=NULL,lease_until_ms=NULL,result_json='{"failure":"lease_expired"}'
                WHERE profile_id=$profile AND id=$job;
                """, transaction, ("$fence", expiredJob.Fence), ("$profile", Id(profileId)), ("$job", Id(expiredJob.Id)));
            await update.ExecuteNonQueryAsync(ct);
            await SourceStateAsync(transaction, profileId, expiredJob.Envelope.SourceEventId, "failed", ct);
            await EventAsync(transaction, profileId, expiredJob.Id, "failed", expiredJob.Fence, now, ct);
        }
    }

    private async Task SourceStateAsync(SqliteTransaction transaction, Guid profileId, Guid sourceId, string state, CancellationToken ct)
    {
        await using var command = Command("UPDATE source_events SET state=$state WHERE profile_id=$profile AND id=$source;", transaction,
            ("$state", state), ("$profile", Id(profileId)), ("$source", Id(sourceId)));
        if (await command.ExecuteNonQueryAsync(ct) != 1)
        {
            throw new InvalidDataException("Job source event is missing.");
        }
    }

    private Task EventAsync(SqliteTransaction transaction, Guid profileId, Guid jobId, string state, long fence, DateTimeOffset now, CancellationToken ct) =>
        SqliteOutbox.AppendAsync(store.Connection, transaction, profileId, "job." + state, $"{jobId:D}:{state}:{fence}",
            JsonSerializer.Serialize(new { job_id = jobId, state, fence }), now, ct);

    private SqliteCommand Command(string sql, SqliteTransaction? transaction, params (string Name, object? Value)[] parameters)
    {
        var command = store.Connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
    private static (string, object?)[] TokenParameters(LeaseToken token, DateTimeOffset now) =>
        [("$profile", Id(token.ProfileId)), ("$job", Id(token.JobId)), ("$owner", token.Owner), ("$fence", token.Fence), ("$now", now.ToUnixTimeMilliseconds())];
    private static string Id(Guid id) => id.ToString("D");
    private static void Identity(Guid profileId, Guid id)
    {
        Identifiers.RequireNonEmpty(profileId, nameof(profileId));
        Identifiers.RequireNonEmpty(id, nameof(id));
    }
    private static void ValidateToken(LeaseToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        Identity(token.ProfileId, token.JobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(token.Owner);
        if (token.Fence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(token));
        }
    }
    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMilliseconds(1) || duration.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
    }
    private static Envelope ReadEnvelope(string json)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(json) ?? throw new InvalidDataException("Invalid job envelope.");
        if (envelope.SourceEventId == Guid.Empty || envelope.Payload.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidDataException("Invalid job envelope.");
        }

        return envelope;
    }
    private static string NormalizeJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }
}
