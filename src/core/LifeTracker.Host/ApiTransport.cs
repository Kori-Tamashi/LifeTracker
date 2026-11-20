using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LifeTracker.State.Contracts;

namespace LifeTracker.Host;

public sealed record AssessmentCreateRequest
{
    public required DateTimeOffset ObservedAt { get; init; }
    public required AssessmentValue[] Values { get; init; }
    public string? Comment { get; init; }
}
public sealed record AssessmentReviseRequest
{
    public required int ExpectedRevision { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public required decimal Score { get; init; }
    public required string Reason { get; init; }
}
public sealed record ApiReceipt(Guid CommandId, string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ResourceId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Revision = null);
public sealed record ApiError(string Code, string Message, Guid CorrelationId);

public sealed class UtcDateTimeConverter : JsonConverter<DateTimeOffset>
{
    public static DateTimeOffset Parse(string value)
    {
        if (!Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
        {
            throw new JsonException("Invalid timestamp.");
        }

        return result.ToUniversalTime();
    }
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected timestamp string.");
        }

        return Parse(reader.GetString() ?? "");
    }
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToUniversalTime().ToString("O"));
}

internal static class ApiTransport
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new UtcDateTimeConverter() }
    };
    public static async Task<T> BodyAsync<T>(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasJsonContentType())
        {
            throw new BadHttpRequestException("Expected JSON.");
        }

        using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
        ValidateObject(document.RootElement);
        if (document.RootElement.TryGetProperty("comment", out var comment) && comment.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Comment must be a string.");
        }

        return document.RootElement.Deserialize<T>(Json) ?? throw new JsonException("Missing body.");
    }
    private static void ValidateObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Expected object.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new JsonException("Duplicate property.");
            }

            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        ValidateObject(item);
                    }
                }
            }
        }
    }
    public static Task WriteAsync(HttpContext context, object value, int status = 200)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(value, Json, context.RequestAborted);
    }
    public static Task ErrorAsync(HttpContext context, int status, string code, string message) =>
        WriteAsync(context, new ApiError(code, message, (Guid)context.Items["correlation"]!), status);
    public static Guid CommandId(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("X-Command-ID", out var values))
        {
            return Guid.NewGuid();
        }

        if (values.Count != 1 || !Guid.TryParseExact(values[0], "D", out var id) || id == Guid.Empty)
        {
            throw new BadHttpRequestException("Invalid command ID.");
        }

        return id;
    }
    public static ApiReceipt Receipt(AssessmentReceipt receipt) => receipt.Assessments.Count == 1
        ? new(receipt.CommandId, "completed", receipt.Assessments[0].Id, receipt.Assessments[0].Revision)
        : new(receipt.CommandId, "completed");
}
