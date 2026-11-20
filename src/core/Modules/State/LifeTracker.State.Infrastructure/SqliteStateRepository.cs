using System.Text.Json;
using LifeTracker.Infrastructure.Shared;
using LifeTracker.Kernel;
using LifeTracker.State.Application;
using LifeTracker.State.Contracts;
using LifeTracker.State.Domain;
using Microsoft.Data.Sqlite;

namespace LifeTracker.State.Infrastructure;

/// <summary>State persistence only. The store owns the connection and writer lease.</summary>
public sealed class SqliteStateRepository(SqliteStore store) : IStateRepository
{
    private const string Source = "state.command";
    private sealed record Snapshot(int SchemaVersion, DateTimeOffset ObservedAt, DateTimeOffset OriginalObservedAt,
        DateTimeOffset RecordedAt, string? Comment, string? CorrectionReason);

    public Task<AssessmentReceipt?> FindReceiptAsync(Guid profileId, string idempotencyKey, string fingerprint, CancellationToken ct) =>
        LockedAsync(() => FindReceiptCoreAsync(profileId, idempotencyKey, fingerprint, null, ct), ct);

    public Task<IReadOnlyList<AssessmentRevision>> GetCurrentAsync(Guid profileId, CancellationToken ct) =>
        LockedAsync(() => ReadAsync(profileId, null, currentOnly: true, ct), ct);

    public Task<IReadOnlyList<AssessmentRevision>> GetHistoryAsync(Guid profileId, Guid assessmentId, CancellationToken ct) =>
        LockedAsync(() => ReadAsync(profileId, assessmentId, currentOnly: false, ct), ct);

    public async Task<AssessmentRevision?> GetAsync(Guid profileId, Guid assessmentId, CancellationToken ct) =>
        (await LockedAsync(() => ReadAsync(profileId, assessmentId, currentOnly: true, ct), ct)).SingleOrDefault();

    public Task<AssessmentReceipt> CommitAsync(AssessmentBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ValidateEnvelope(batch.ProfileId, batch.CommandId, batch.IdempotencyKey, batch.Fingerprint);
        if (batch.Revisions.Count is < 1 or > 4 || batch.Revisions.Any(r => r.ProfileId != batch.ProfileId || r.Revision != 1)
            || batch.Revisions.Select(r => r.AssessmentId).Distinct().Count() != batch.Revisions.Count
            || batch.Revisions.Select(r => r.Metric).Distinct().Count() != batch.Revisions.Count)
        {
            throw new ArgumentException("Invalid initial assessment batch.", nameof(batch));
        }

        return LockedAsync(() => WriteAsync(batch.ProfileId, batch.CommandId, batch.IdempotencyKey, batch.Fingerprint,
            batch.Revisions, null, ct), ct);
    }

    public Task<AssessmentReceipt> CorrectAsync(AssessmentCorrection correction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(correction);
        ValidateEnvelope(correction.ProfileId, correction.CommandId, correction.IdempotencyKey, correction.Fingerprint);
        if (correction.NewRevision.ProfileId != correction.ProfileId || correction.ExpectedRevision < 1
            || correction.NewRevision.Revision != checked(correction.ExpectedRevision + 1))
        {
            throw new ArgumentException("Invalid correction revision.", nameof(correction));
        }

        return LockedAsync(() => WriteAsync(correction.ProfileId, correction.CommandId, correction.IdempotencyKey,
            correction.Fingerprint, [correction.NewRevision], correction.ExpectedRevision, ct), ct);
    }

    private Task<T> LockedAsync<T>(Func<Task<T>> action, CancellationToken ct) =>
        store.WithConnectionAsync(action, ct);

    private async Task<AssessmentReceipt?> FindReceiptCoreAsync(Guid profileId, string key, string fingerprint,
        SqliteTransaction? transaction, CancellationToken ct)
    {
        Identifiers.RequireNonEmpty(profileId, nameof(profileId));
        await using var command = Command("""
            SELECT payload_hash, receipt_json FROM source_events
            WHERE profile_id=$profile AND source=$source AND external_id=$key;
            """, transaction, ("$profile", profileId.ToString("D")), ("$source", Source), ("$key", key));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        if (!string.Equals(reader.GetString(0), fingerprint, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException();
        }

        return JsonSerializer.Deserialize<AssessmentReceipt>(reader.GetString(1))
            ?? throw new InvalidDataException("Stored command receipt is invalid.");
    }

    private async Task<AssessmentReceipt> WriteAsync(Guid profileId, Guid commandId, string key, string fingerprint,
        IReadOnlyList<AssessmentRevision> revisions, int? expectedRevision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await using var transaction = store.Connection.BeginTransaction(deferred: false);
        var previous = await FindReceiptCoreAsync(profileId, key, fingerprint, transaction, ct);
        if (previous is not null)
        {
            return previous;
        }

        await using (var collision = Command("SELECT COUNT(*) FROM source_events WHERE profile_id=$profile AND id=$id;",
            transaction, ("$profile", profileId.ToString("D")), ("$id", commandId.ToString("D"))))
        {
            if (Convert.ToInt64(await collision.ExecuteScalarAsync(ct)) != 0)
            {
                throw new IdempotencyConflictException();
            }
        }

        foreach (var revision in revisions)
        {
            if (expectedRevision is null)
            {
                await using var entity = Command("""
                    INSERT INTO entities(profile_id,id,kind,current_revision) VALUES ($profile,$id,'observation',1);
                    """, transaction, ("$profile", profileId.ToString("D")), ("$id", revision.AssessmentId.ToString("D")));
                await entity.ExecuteNonQueryAsync(ct);
            }
            else
            {
                await using var existing = Command("""
                    SELECT e.current_revision, o.metric, o.source_kind, r.status FROM entities e
                    JOIN observations o ON o.profile_id=e.profile_id AND o.entity_id=e.id AND o.revision=e.current_revision
                    JOIN revisions r ON r.profile_id=e.profile_id AND r.entity_id=e.id AND r.revision=e.current_revision
                    WHERE e.profile_id=$profile AND e.id=$id AND e.kind='observation';
                    """, transaction, ("$profile", profileId.ToString("D")), ("$id", revision.AssessmentId.ToString("D")));
                await using (var reader = await existing.ExecuteReaderAsync(ct))
                {
                    if (!await reader.ReadAsync(ct))
                    {
                        throw new AssessmentNotFoundException();
                    }

                    if (reader.GetInt32(0) != expectedRevision.Value)
                    {
                        throw new RevisionConflictException();
                    }

                    if (reader.GetString(1) != MetricName(revision.Metric) || reader.GetString(2) != SourceName(revision.Source)
                        || reader.GetString(3) != StatusName(revision.Status))
                    {
                        throw new ArgumentException("A correction cannot change metric, source or acceptance status.");
                    }
                }
                await using var pointer = Command("""
                    UPDATE entities SET current_revision=$next
                    WHERE profile_id=$profile AND id=$id AND current_revision=$expected;
                    """, transaction, ("$next", revision.Revision), ("$profile", profileId.ToString("D")),
                    ("$id", revision.AssessmentId.ToString("D")), ("$expected", expectedRevision.Value));
                if (await pointer.ExecuteNonQueryAsync(ct) != 1)
                {
                    throw new RevisionConflictException();
                }
            }
            var snapshot = JsonSerializer.Serialize(new Snapshot(1, revision.ObservedAt, revision.OriginalObservedAt,
                revision.RecordedAt, revision.Comment, revision.CorrectionReason));
            await using var insert = Command("""
                INSERT INTO revisions(profile_id,entity_id,revision,recorded_ms,actor,status,reason,body_json)
                VALUES ($profile,$id,$revision,$recorded,$actor,$status,$reason,$body);
                INSERT INTO observations(profile_id,entity_id,revision,metric,score_half,observed_ms,source_kind)
                VALUES ($profile,$id,$revision,$metric,$score,$observed,$source);
                """, transaction, ("$profile", profileId.ToString("D")), ("$id", revision.AssessmentId.ToString("D")),
                ("$revision", revision.Revision), ("$recorded", revision.RecordedAt.ToUnixTimeMilliseconds()),
                ("$actor", revision.Source == AssessmentSource.SelfReport ? "owner" : "ai"),
                ("$status", StatusName(revision.Status)), ("$reason", revision.CorrectionReason), ("$body", snapshot),
                ("$metric", MetricName(revision.Metric)), ("$score", revision.Score.HalfUnits),
                ("$observed", revision.ObservedAt.ToUnixTimeMilliseconds()), ("$source", SourceName(revision.Source)));
            await insert.ExecuteNonQueryAsync(ct);
        }
        var receipt = new AssessmentReceipt(commandId, Array.AsReadOnly(revisions.Select(r => new SavedAssessment(r.AssessmentId, r.Revision)).ToArray()));
        await using (var command = Command("""
            INSERT INTO source_events(profile_id,id,source,external_id,payload_hash,received_ms,state,receipt_json)
            VALUES ($profile,$id,$source,$key,$hash,$received,'completed',$receipt);
            """, transaction, ("$profile", profileId.ToString("D")), ("$id", commandId.ToString("D")), ("$source", Source),
            ("$key", key), ("$hash", fingerprint), ("$received", revisions.Max(r => r.RecordedAt.ToUnixTimeMilliseconds())),
            ("$receipt", JsonSerializer.Serialize(receipt))))
        {
            await command.ExecuteNonQueryAsync(ct);
        }

        await SqliteOutbox.AppendAsync(store.Connection, transaction, profileId, "state.changed", commandId.ToString("D"),
            JsonSerializer.Serialize(new { command_id = commandId, assessments = receipt.Assessments }),
            revisions.Max(r => r.RecordedAt), ct);
        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        return receipt;
    }

    private async Task<IReadOnlyList<AssessmentRevision>> ReadAsync(Guid profileId, Guid? assessmentId, bool currentOnly, CancellationToken ct)
    {
        Identifiers.RequireNonEmpty(profileId, nameof(profileId));
        if (assessmentId is not null)
        {
            Identifiers.RequireNonEmpty(assessmentId.Value, nameof(assessmentId));
        }

        await using var command = Command("""
            SELECT o.entity_id,o.revision,o.metric,o.score_half,o.observed_ms,o.source_kind,
                r.recorded_ms,r.status,r.reason,r.body_json FROM observations o
            JOIN entities e ON e.profile_id=o.profile_id AND e.id=o.entity_id AND e.kind='observation'
            JOIN revisions r ON r.profile_id=o.profile_id AND r.entity_id=o.entity_id AND r.revision=o.revision
            WHERE o.profile_id=$profile AND ($id IS NULL OR o.entity_id=$id)
              AND ($current=0 OR o.revision=e.current_revision)
            ORDER BY o.entity_id,o.revision;
            """, null, ("$profile", profileId.ToString("D")), ("$id", assessmentId?.ToString("D")), ("$current", currentOnly ? 1 : 0));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var records = new List<AssessmentRevision>();
        while (await reader.ReadAsync(ct))
        {
            var body = JsonSerializer.Deserialize<Snapshot>(reader.GetString(9)) ?? throw new InvalidDataException("Missing revision body.");
            if (body.SchemaVersion != 1 || body.ObservedAt.ToUnixTimeMilliseconds() != reader.GetInt64(4)
                || body.RecordedAt.ToUnixTimeMilliseconds() != reader.GetInt64(6)
                || body.CorrectionReason != (reader.IsDBNull(8) ? null : reader.GetString(8)))
            {
                throw new InvalidDataException("Revision snapshot does not match its typed fields.");
            }

            var metric = reader.GetString(2) switch
            {
                "energy" => StateMetric.Energy,
                "mood" => StateMetric.Mood,
                "anxiety" => StateMetric.Anxiety,
                "physical_wellbeing" => StateMetric.PhysicalWellbeing,
                _ => throw new InvalidDataException("Unknown metric.")
            };
            var source = reader.GetString(5) switch
            {
                "self_report" => AssessmentSource.SelfReport,
                "ai_estimate" => AssessmentSource.AiEstimate,
                _ => throw new InvalidDataException("Unknown assessment source.")
            };
            var status = reader.GetString(7) switch
            {
                "accepted" => AssessmentStatus.Accepted,
                "rejected" => AssessmentStatus.Rejected,
                "deleted" => AssessmentStatus.Deleted,
                _ => throw new InvalidDataException("Unsupported assessment status.")
            };
            records.Add(new(profileId, Guid.Parse(reader.GetString(0)), reader.GetInt32(1), metric,
                new Score(reader.GetInt32(3) / 2m), body.ObservedAt, body.RecordedAt, source, status,
                body.OriginalObservedAt, body.Comment, body.CorrectionReason));
        }
        return records.AsReadOnly();
    }

    private SqliteCommand Command(string sql, SqliteTransaction? transaction, params (string Name, object? Value)[] parameters)
    {
        var command = store.Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    private static void ValidateEnvelope(Guid profileId, Guid commandId, string key, string fingerprint)
    {
        Identifiers.RequireNonEmpty(profileId, nameof(profileId));
        Identifiers.RequireNonEmpty(commandId, nameof(commandId));
        if (string.IsNullOrWhiteSpace(key) || key.Length is < 8 or > 128 || string.IsNullOrWhiteSpace(fingerprint))
        {
            throw new ArgumentException("Invalid command envelope.");
        }
    }

    private static string MetricName(StateMetric metric) => metric switch
    {
        StateMetric.Energy => "energy",
        StateMetric.Mood => "mood",
        StateMetric.Anxiety => "anxiety",
        StateMetric.PhysicalWellbeing => "physical_wellbeing",
        _ => throw new ArgumentOutOfRangeException(nameof(metric))
    };
    private static string SourceName(AssessmentSource source) => source switch
    {
        AssessmentSource.SelfReport => "self_report",
        AssessmentSource.AiEstimate => "ai_estimate",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
    private static string StatusName(AssessmentStatus status) => status switch
    {
        AssessmentStatus.Accepted => "accepted",
        AssessmentStatus.Rejected => "rejected",
        AssessmentStatus.Deleted => "deleted",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
