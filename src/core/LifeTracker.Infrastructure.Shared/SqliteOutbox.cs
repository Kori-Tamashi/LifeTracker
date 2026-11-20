using Microsoft.Data.Sqlite;

namespace LifeTracker.Infrastructure.Shared;

/// <summary>Infrastructure plumbing; callers own the transaction and domain payload.</summary>
public static class SqliteOutbox
{
    public static async Task AppendAsync(SqliteConnection connection, SqliteTransaction transaction, Guid profileId,
        string topic, string reasonKey, string payloadJson, DateTimeOffset now, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO outbox(profile_id,id,reason_key,topic,payload_json,created_ms)
            VALUES ($profile,$id,$reason,$topic,$payload,$now);
            """;
        command.Parameters.AddWithValue("$profile", profileId.ToString("D"));
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$reason", reasonKey);
        command.Parameters.AddWithValue("$topic", topic);
        command.Parameters.AddWithValue("$payload", payloadJson);
        command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
        await command.ExecuteNonQueryAsync(ct);
    }
}
