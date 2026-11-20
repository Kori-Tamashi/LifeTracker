using LifeTracker.Application.Abstractions;
using LifeTracker.Infrastructure.Shared;
using LifeTracker.Kernel;
using LifeTracker.Operations.Application;
using LifeTracker.Operations.Domain;
using Microsoft.Data.Sqlite;

namespace LifeTracker.Operations.Infrastructure;

public sealed class SqliteOutboxStore(SqliteStore store, IUtcClock clock) : IOutbox
{
    private DateTimeOffset Now => clock.UtcNow;
    public Task<IReadOnlyList<OutboxEvent>> ReadPendingAsync(Guid profileId, int limit, CancellationToken ct)
    {
        Identifiers.RequireNonEmpty(profileId, nameof(profileId));
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        return store.WithConnectionAsync<IReadOnlyList<OutboxEvent>>(async () =>
        {
            await using var command = Command("SELECT id,topic,reason_key,payload_json,created_ms FROM outbox WHERE profile_id=$profile AND dispatched_ms IS NULL ORDER BY created_ms,id LIMIT $limit;",
                null, ("$profile", Id(profileId)), ("$limit", limit));
            await using var reader = await command.ExecuteReaderAsync(ct);
            var events = new List<OutboxEvent>();
            while (await reader.ReadAsync(ct))
            {
                events.Add(new(profileId, Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4))));
            }

            return events.AsReadOnly();
        }, ct);
    }

    public Task<bool> MarkDispatchedAsync(Guid profileId, Guid eventId, CancellationToken ct)
    {
        Identity(profileId, eventId);
        return store.WithConnectionAsync(async () =>
        {
            await using var command = Command("UPDATE outbox SET dispatched_ms=$now WHERE profile_id=$profile AND id=$id AND dispatched_ms IS NULL;",
                null, ("$now", Now.ToUnixTimeMilliseconds()), ("$profile", Id(profileId)), ("$id", Id(eventId)));
            return await command.ExecuteNonQueryAsync(ct) == 1;
        }, ct);
    }

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
    private static string Id(Guid id) => id.ToString("D");
    private static void Identity(Guid profileId, Guid id)
    {
        Identifiers.RequireNonEmpty(profileId, nameof(profileId));
        Identifiers.RequireNonEmpty(id, nameof(id));
    }
}
