using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LifeTracker.Application.Abstractions;
using LifeTracker.Host;
using Xunit;

namespace LifeTracker.Host.Tests;

public sealed class ApiFlowTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lifetracker-api-synthetic-" + Guid.NewGuid().ToString("N"));
    private LocalApiHost host = null!;
    private HttpClient client = null!;
    private readonly Clock clock = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private sealed class Clock : IUtcClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-01-15T12:00:00Z"); }
    public async ValueTask InitializeAsync() => await StartAsync();
    private async Task StartAsync()
    {
        host = await LocalApiHost.CreateAsync(new(root, 0), clock, Ct);
        await host.StartAsync(Ct);
        client = new HttpClient { BaseAddress = new Uri(host.Address, "/api/v1/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await File.ReadAllTextAsync(Path.Combine(root, "owner.credential"), Ct));
    }
    private async Task RestartAsync()
    {
        client.Dispose();
        await host.DisposeAsync();
        await StartAsync();
    }
    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await host.DisposeAsync();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.Clone();
    }
    private Task<HttpResponseMessage> RequestAsync(HttpMethod method, string path, string? json = null, string key = "synthetic-command-key")
    {
        var request = new HttpRequestMessage(method, path);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        request.Headers.Add("Idempotency-Key", key);
        return SendAsync(request);
    }
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using (request)
        {
            return await client.SendAsync(request, Ct);
        }
    }
    private string CreateBody(decimal score = 8) => JsonSerializer.Serialize(new
    {
        observed_at = clock.UtcNow,
        values = new[] { new { metric = "energy", score } }
    });

    [Fact]
    public async Task MissingOrWrongCredentialCannotReadState()
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var missing = await client.GetAsync("state/current", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal("NOT_AUTHORIZED", (await JsonAsync(missing)).GetProperty("code").GetString());
        client.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic-invalid-token");
        using var wrong = await client.GetAsync("state/current", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("no-store", wrong.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task InitialStateIsUnknownAndUnavailableRoutesAreExplicit()
    {
        using var initial = await client.GetAsync("state/current", Ct);
        var state = await JsonAsync(initial);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("balance").ValueKind);
        Assert.Equal(4, state.GetProperty("metrics").GetArrayLength());
        using var unavailable = await client.GetAsync("portfolio", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("CAPABILITY_UNAVAILABLE", (await JsonAsync(unavailable)).GetProperty("code").GetString());
        using var unknown = await client.GetAsync("synthetic-unknown-route", Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task RecordRestartCorrectAndRetryPreserveDurableIdentity()
    {
        using var create = await RequestAsync(HttpMethod.Post, "assessments", CreateBody());
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var receipt = await JsonAsync(create);
        var id = receipt.GetProperty("resource_id").GetString();
        var commandId = receipt.GetProperty("command_id").GetString();
        await RestartAsync();
        using var retried = await RequestAsync(HttpMethod.Post, "assessments", CreateBody());
        Assert.Equal(commandId, (await JsonAsync(retried)).GetProperty("command_id").GetString());
        using var current = await client.GetAsync("state/current", Ct);
        var energy = (await JsonAsync(current)).GetProperty("metrics").EnumerateArray().Single(m => m.GetProperty("metric").GetString() == "energy");
        Assert.Equal(8m, energy.GetProperty("score").GetDecimal());
        var body = JsonSerializer.Serialize(new { expected_revision = 1, observed_at = clock.UtcNow, score = 6m, reason = "Synthetic correction" });
        using var patch = await RequestAsync(HttpMethod.Patch, "assessments/" + id, body);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(2, (await JsonAsync(patch)).GetProperty("revision").GetInt32());
        using var repeated = await RequestAsync(HttpMethod.Patch, "assessments/" + id, body);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        using var stale = await RequestAsync(HttpMethod.Patch, "assessments/" + id, body, "different-synthetic-key");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("REVISION_CONFLICT", (await JsonAsync(stale)).GetProperty("code").GetString());
        using var page = await client.GetAsync("assessments", Ct);
        Assert.Equal(2, (await JsonAsync(page)).GetProperty("items")[0].GetProperty("revision").GetInt32());
    }

    [Fact]
    public async Task BatchBalanceUsesDomainAndHistoryContainsAllFourMetrics()
    {
        var body = JsonSerializer.Serialize(new
        {
            observed_at = clock.UtcNow,
            values = new[] {
            new { metric = "energy", score = 8m }, new { metric = "mood", score = 7.5m },
            new { metric = "anxiety", score = 2m }, new { metric = "physical_wellbeing", score = 9m } }
        });
        using var create = await RequestAsync(HttpMethod.Post, "assessments", body);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        using var current = await client.GetAsync("state/current", Ct);
        Assert.Equal(7.5m, (await JsonAsync(current)).GetProperty("balance").GetDecimal());
        using var history = await client.GetAsync("assessments", Ct);
        Assert.Equal(4, (await JsonAsync(history)).GetProperty("items").GetArrayLength());
        clock.UtcNow += TimeSpan.FromHours(6);
        using var stale = await client.GetAsync("state/current", Ct);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(stale)).GetProperty("balance").ValueKind);
    }

    [Theory]
    [InlineData("{\"observed_at\":\"2026-01-15T12:00:00Z\",\"values\":[{\"metric\":\"energy\",\"score\":7.25}]}", 422)]
    [InlineData("{\"observed_at\":\"2026-01-15T12:00:00Z\",\"values\":[{\"metric\":\"energy\"}]}", 400)]
    [InlineData("{\"observed_at\":123,\"values\":[]}", 400)]
    [InlineData("{\"values\":[{\"metric\":\"energy\",\"score\":8}]}", 400)]
    [InlineData("{\"observed_at\":\"2026-01-15T12:00:00\",\"values\":[]}", 400)]
    [InlineData("{\"observed_at\":\"2026-01-15T12:00:00Z\",\"values\":[],\"profile_id\":\"synthetic-other\"}", 400)]
    [InlineData("{\"observed_at\":\"2026-01-15T12:00:00Z\",\"values\":[],\"comment\":null}", 400)]
    [InlineData("{\"observed_at\":\"2026-01-15T12:00:00Z\",\"values\":[{\"metric\":\"energy\",\"score\":8,\"score\":9}]}", 400)]
    [InlineData("{\"observed_at\":\"2026-01-15T15:00:00Z\",\"values\":[{\"metric\":\"energy\",\"score\":8}]}", 422)]
    public async Task InvalidRequestsAreTypedAndAtomic(string body, int expected)
    {
        using var result = await RequestAsync(HttpMethod.Post, "assessments", body);
        Assert.Equal(expected, (int)result.StatusCode);
        var error = await JsonAsync(result);
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.True(Guid.TryParse(error.GetProperty("correlation_id").GetString(), out _));
        Assert.DoesNotContain(root, await result.Content.ReadAsStringAsync(Ct));
        using var page = await client.GetAsync("assessments", Ct);
        Assert.Empty((await JsonAsync(page)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ConflictingKeyOversizedBodyAndMissingKeyAreRejected()
    {
        using var created = await RequestAsync(HttpMethod.Post, "assessments", CreateBody());
        using var conflict = await RequestAsync(HttpMethod.Post, "assessments", CreateBody(6));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var huge = await RequestAsync(HttpMethod.Post, "assessments", "{\"comment\":\"" + new string('x', 70000) + "\"}");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);
        using var missing = await RequestAsync(HttpMethod.Post, "assessments", CreateBody(), "");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task PaginationSnapshotSurvivesCorrectionNewWritesAndRestart()
    {
        for (var n = 0; n < 3; n++)
        {
            using var accepted = await RequestAsync(HttpMethod.Post, "assessments", CreateBody(), "synthetic-page-key-" + n);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
        using var initial = await client.GetAsync("assessments?limit=1", Ct);
        var first = await JsonAsync(initial);
        var ids = new List<string> { first.GetProperty("items")[0].GetProperty("id").GetString()! };
        var cursor = first.GetProperty("next_cursor").GetString()!;
        using var all = await client.GetAsync("assessments", Ct);
        var last = (await JsonAsync(all)).GetProperty("items")[2].GetProperty("id").GetString();
        using var corrected = await RequestAsync(HttpMethod.Patch, "assessments/" + last, JsonSerializer.Serialize(new
        {
            expected_revision = 1,
            observed_at = clock.UtcNow.AddHours(-2),
            score = 6m,
            reason = "Synthetic backdated correction"
        }), "synthetic-patch-key");
        using var extra = await RequestAsync(HttpMethod.Post, "assessments", CreateBody(), "synthetic-new-key");
        await RestartAsync();
        while (cursor is not null)
        {
            using var response = await client.GetAsync("assessments?limit=1&cursor=" + Uri.EscapeDataString(cursor), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await JsonAsync(response);
            foreach (var row in page.GetProperty("items").EnumerateArray())
            {
                ids.Add(row.GetProperty("id").GetString()!);
                Assert.Equal(1, row.GetProperty("revision").GetInt32());
                Assert.Equal(8m, row.GetProperty("score").GetDecimal());
            }
            cursor = page.GetProperty("next_cursor").GetString();
        }
        Assert.Equal(3, ids.Distinct().Count());
    }

    [Fact]
    public async Task CursorRejectsTamperingFilterChangeAndExpiry()
    {
        for (var n = 0; n < 2; n++)
        {
            using var accepted = await RequestAsync(HttpMethod.Post, "assessments", CreateBody(), "synthetic-cursor-key-" + n);
        }
        using var first = await client.GetAsync("assessments?limit=1", Ct);
        var cursor = (await JsonAsync(first)).GetProperty("next_cursor").GetString()!;
        foreach (var query in new[] { "assessments?limit=2&cursor=" + cursor, "assessments?limit=1&cursor=" + cursor + "0", "assessments?limit=abc" })
        {
            using var invalid = await client.GetAsync(query, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        clock.UtcNow += TimeSpan.FromHours(24);
        using var expired = await client.GetAsync("assessments?limit=1&cursor=" + cursor, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
    }

    [Fact]
    public async Task ForeignHostCannotUseOwnerCredential()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "state/current");
        request.Headers.Host = "synthetic.invalid";
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ActualResponsesProduceOpenApiContractEvidence()
    {
        var evidence = new List<object>();
        async Task Capture(string schema, HttpResponseMessage response)
        {
            using (response)
            {
                evidence.Add(new { schema, body = await JsonAsync(response) });
            }
        }
        await Capture("CurrentState", await client.GetAsync("state/current", Ct));
        await Capture("Capabilities", await client.GetAsync("capabilities", Ct));
        await Capture("Receipt", await RequestAsync(HttpMethod.Post, "assessments", CreateBody()));
        await Capture("AssessmentRecordPage", await client.GetAsync("assessments", Ct));
        await Capture("Error", await RequestAsync(HttpMethod.Post, "assessments", CreateBody(6)));
        await Capture("Error", await client.GetAsync("portfolio", Ct));
        var path = Environment.GetEnvironmentVariable("LIFETRACKER_CONTRACT_OUTPUT");
        if (path is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence), Ct);
        }
        Assert.Equal(6, evidence.Count);
    }
    [Fact]
    public async Task EveryDeclaredUnimplementedRouteReportsCapabilityUnavailable()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures/routes.json"), Ct));
        var supported = new HashSet<string> { "GET /state/current", "GET /assessments", "POST /assessments", "PATCH /assessments/{id}", "GET /capabilities" };
        var count = 0;
        foreach (var row in manifest.RootElement.EnumerateArray())
        {
            var method = row.GetProperty("method").GetString()!;
            var path = row.GetProperty("path").GetString()!;
            if (supported.Contains(method + " " + path))
            {
                continue;
            }

            path = System.Text.RegularExpressions.Regex.Replace(path, @"\{[^}]+\}", "synthetic").TrimStart('/');
            using var response = await RequestAsync(new HttpMethod(method), path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("CAPABILITY_UNAVAILABLE", (await JsonAsync(response)).GetProperty("code").GetString());
            count++;
        }
        Assert.Equal(46, count);
    }

    [Fact]
    public async Task HistoryUsesInclusiveFromAndExclusiveTo()
    {
        foreach (var (time, key) in new[] { (clock.UtcNow.AddHours(-1), "synthetic-before-key"), (clock.UtcNow, "synthetic-boundary-key") })
        {
            using var accepted = await RequestAsync(HttpMethod.Post, "assessments", JsonSerializer.Serialize(new
            {
                observed_at = time,
                values = new[] { new { metric = "energy", score = 8 } }
            }), key);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
        using var filtered = await client.GetAsync("assessments?from=" + Uri.EscapeDataString(clock.UtcNow.AddHours(-1).ToString("O"))
            + "&to=" + Uri.EscapeDataString(clock.UtcNow.ToString("O")), Ct);
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        Assert.Equal(1, (await JsonAsync(filtered)).GetProperty("items").GetArrayLength());
    }

}
