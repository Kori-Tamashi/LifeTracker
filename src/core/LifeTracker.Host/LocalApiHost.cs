using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LifeTracker.Application.Abstractions;
using LifeTracker.Infrastructure.Shared;
using LifeTracker.State.Application;
using LifeTracker.State.Contracts;
using LifeTracker.State.Infrastructure;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

namespace LifeTracker.Host;

public sealed record ApiHostSettings(string RuntimeRoot, int Port = 17841);
public sealed class SystemUtcClock : IUtcClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

public sealed class LocalApiHost : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly SqliteStore store;
    private LocalApiHost(WebApplication app, SqliteStore store) => (this.app, this.store) = (app, store);
    public Uri Address => new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
    public Task StartAsync(CancellationToken ct = default) => app.StartAsync(ct);
    public Task WaitForShutdownAsync(CancellationToken ct = default) => app.WaitForShutdownAsync(ct);
    private sealed record Route(string Path, string Method);

    public static async Task<LocalApiHost> CreateAsync(ApiHostSettings settings, IUtcClock? clock = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(settings.RuntimeRoot) || settings.Port is < 0 or > 65535)
        {
            throw new ArgumentException("Invalid local API configuration.");
        }

        Directory.CreateDirectory(settings.RuntimeRoot);
        var existing = File.Exists(Path.Combine(settings.RuntimeRoot, "lifetracker.sqlite"));
        var token = await OwnerCredential.LoadOrCreateAsync(settings.RuntimeRoot, !existing, ct);
        var store = existing ? await SqliteStore.OpenExistingAsync(settings.RuntimeRoot, ct) : await SqliteStore.CreateAsync(settings.RuntimeRoot, ct);
        try
        {
            var profileId = await InitializeOwnerAsync(store, ct);
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, settings.Port);
                options.Limits.MaxRequestBodySize = 65536;
            });
            clock ??= new SystemUtcClock();
            var repository = new SqliteStateRepository(store);
            builder.Services.AddSingleton<IUtcClock>(clock);
            builder.Services.AddSingleton<IRecordAssessments>(new RecordAssessmentsHandler(repository, clock));
            builder.Services.AddSingleton<IReviseAssessment>(new ReviseAssessmentHandler(repository, clock));
            builder.Services.AddSingleton<IGetCurrentState>(new CurrentStateQuery(repository, clock));
            builder.Services.AddSingleton<IGetAssessmentHistory>(new AssessmentHistoryQuery(repository,
                new HmacHistoryCursorCodec(SHA256.HashData(Encoding.UTF8.GetBytes(token))), clock));
            var app = builder.Build();
            var credentialHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            app.Use(async (context, next) =>
            {
                context.Items["correlation"] = Guid.NewGuid();
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["X-Correlation-ID"] = ((Guid)context.Items["correlation"]!).ToString("D");
                try
                {
                    var authorization = context.Request.Headers.Authorization;
                    if (authorization.Count != 1 || authorization[0] is not { } header || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                        || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(header[7..])), credentialHash))
                    {
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        await ApiTransport.ErrorAsync(context, 401, "NOT_AUTHORIZED", "Требуется доступ владельца.");
                        return;
                    }
                    if (context.Request.Host.Host is not ("127.0.0.1" or "localhost")
                        || context.Connection.RemoteIpAddress is { } remote && !IPAddress.IsLoopback(remote))
                    {
                        await ApiTransport.ErrorAsync(context, 403, "NOT_AUTHORIZED", "Недопустимый адрес запроса.");
                        return;
                    }
                    await next(context);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Abort(); }
                catch (Exception error)
                {
                    if (context.Response.HasStarted) { context.Abort(); return; }
                    var (status, code, message) = error switch
                    {
                        AssessmentNotFoundException => (404, "NOT_FOUND", "Оценка не найдена."),
                        RevisionConflictException => (409, "REVISION_CONFLICT", "Оценка уже изменена."),
                        IdempotencyConflictException => (409, "IDEMPOTENCY_CONFLICT", "Команда конфликтует с ранее принятой."),
                        InvalidHistoryCursorException => (400, "VALIDATION_ERROR", "Курсор недействителен; обновите историю."),
                        JsonException => (400, "VALIDATION_ERROR", "Некорректный JSON запроса."),
                        BadHttpRequestException bad => (bad.StatusCode == 413 ? 413 : 400, "VALIDATION_ERROR", "Некорректный HTTP-запрос."),
                        ArgumentException => (422, "VALIDATION_ERROR", "Некорректные значения запроса."),
                        _ => (503, "PROCESSING_UNAVAILABLE", "Обработка временно недоступна.")
                    };
                    await ApiTransport.ErrorAsync(context, status, code, message);
                }
            });
            app.MapGet("/api/v1/state/current", async (HttpContext context, IGetCurrentState query) =>
                await ApiTransport.WriteAsync(context, await query.ExecuteAsync(profileId, context.RequestAborted)));
            app.MapPost("/api/v1/assessments", async (HttpContext context, IRecordAssessments handler) =>
            {
                var body = await ApiTransport.BodyAsync<AssessmentCreateRequest>(context.Request, context.RequestAborted);
                var receipt = await handler.ExecuteAsync(new(profileId, ApiTransport.CommandId(context.Request), ScopedKey(context.Request, "record"),
                    body.ObservedAt, body.Values, body.Comment), context.RequestAborted);
                await ApiTransport.WriteAsync(context, ApiTransport.Receipt(receipt));
            });
            app.MapPatch("/api/v1/assessments/{id}", async (HttpContext context, string id, IReviseAssessment handler) =>
            {
                if (!Guid.TryParseExact(id, "D", out var identity) || identity == Guid.Empty)
                {
                    throw new BadHttpRequestException("Invalid ID.");
                }

                var body = await ApiTransport.BodyAsync<AssessmentReviseRequest>(context.Request, context.RequestAborted);
                var receipt = await handler.ExecuteAsync(new(profileId, ApiTransport.CommandId(context.Request), ScopedKey(context.Request, "revise:" + identity.ToString("D")),
                    identity, body.ExpectedRevision, body.ObservedAt, body.Score, body.Reason), context.RequestAborted);
                await ApiTransport.WriteAsync(context, ApiTransport.Receipt(receipt));
            });
            app.MapGet("/api/v1/assessments", async (HttpContext context, IGetAssessmentHistory query) =>
            {
                var parameters = context.Request.Query;
                if (parameters.Keys.Except(new[] { "from", "to", "limit", "cursor" }, StringComparer.Ordinal).Any()
                    || parameters.Any(p => p.Value.Count != 1))
                {
                    throw new BadHttpRequestException("Invalid query.");
                }

                var limit = 50;
                if (parameters.TryGetValue("limit", out var value) && !int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out limit))
                {
                    throw new BadHttpRequestException("Invalid limit.");
                }

                DateTimeOffset? Date(string name) => parameters.TryGetValue(name, out var text) ? UtcDateTimeConverter.Parse(text.ToString()) : null;
                var page = await query.ExecuteAsync(profileId, Date("from"), Date("to"), limit,
                    parameters.TryGetValue("cursor", out var cursor) ? cursor.ToString() : null, context.RequestAborted);
                await ApiTransport.WriteAsync(context, page);
            });
            app.MapGet("/api/v1/capabilities", (HttpContext context) => ApiTransport.WriteAsync(context, new
            {
                items = new object[] { new { name = "state", status = "enabled" }, new { name = "activity.apple", status = "unqualified" },
                    new { name = "journal", status = "disabled" }, new { name = "finance", status = "disabled" }, new { name = "quests", status = "disabled" } }
            }));
            var enabled = new HashSet<string>(StringComparer.Ordinal) { "GET /state/current", "GET /assessments", "POST /assessments", "PATCH /assessments/{id}", "GET /capabilities" };
            using var stream = typeof(LocalApiHost).Assembly.GetManifestResourceStream("LifeTracker.Host.Routes.json")!;
            var routes = JsonSerializer.Deserialize<Route[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            foreach (var route in routes.Where(r => !enabled.Contains(r.Method + " " + r.Path)))
            {
                app.MapMethods("/api/v1" + route.Path, [route.Method], (HttpContext context) =>
                    ApiTransport.ErrorAsync(context, 503, "CAPABILITY_UNAVAILABLE", "Возможность пока недоступна."));
            }

            app.MapFallback("/{**path}", (HttpContext context) => ApiTransport.ErrorAsync(context, 404, "NOT_FOUND", "Маршрут не найден."));
            return new(app, store);
        }
        catch { await store.DisposeAsync(); throw; }
    }

    private static string ScopedKey(HttpRequest request, string operation)
    {
        var keys = request.Headers["Idempotency-Key"];
        if (keys.Count != 1 || string.IsNullOrWhiteSpace(keys[0]) || keys[0]!.Length is < 8 or > 128)
        {
            throw new BadHttpRequestException("Missing or invalid idempotency key.");
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operation + ":" + keys[0])));
    }
    private static async Task<Guid> InitializeOwnerAsync(SqliteStore store, CancellationToken ct) => await store.WithConnectionAsync(async () =>
    {
        await using var command = store.Connection.CreateCommand();
        command.CommandText = "SELECT id FROM profiles WHERE active=1;";
        if (await command.ExecuteScalarAsync(ct) is string existing)
        {
            return Guid.Parse(existing);
        }

        command.CommandText = "SELECT COUNT(*) FROM profiles;";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) != 0)
        {
            throw new InvalidDataException("Owner profile recovery is required.");
        }

        var profile = Guid.NewGuid();
        command.CommandText = "INSERT INTO profiles(id) VALUES ($id);";
        command.Parameters.AddWithValue("$id", profile.ToString("D"));
        await command.ExecuteNonQueryAsync(ct);
        return profile;
    }, ct);
    public async ValueTask DisposeAsync()
    {
        try { await app.StopAsync(); await app.DisposeAsync(); }
        finally { await store.DisposeAsync(); }
    }
}
