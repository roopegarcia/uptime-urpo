using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace UptimeUrpo.IntegrationTests;

[Collection("PostgreSQL")]
[Trait("Category", "Database")]
public sealed class AnalyticsApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ReadEndpointsExposeSafeDtosAndNeverInsertCheckResults()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        await scenario.FailThreeAsync();
        scenario.Clock.Advance(TimeSpan.FromSeconds(1));
        using var factory = new AnalyticsApiFactory(scenario);
        using var client = factory.CreateClient();
        factory.Restore(scenario);
        foreach (var path in new[] { "/api/services/service/statistics", "/api/services/service/history", "/api/services/service/incidents", "/api/overview" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("example.com", body);
            Assert.DoesNotContain("urpo_runtime", body);
        }
        using var stats = JsonDocument.Parse(await client.GetStringAsync("/api/services/service/statistics"));
        Assert.Equal(3, stats.RootElement.GetProperty("windows").GetArrayLength());
        Assert.Equal(0, stats.RootElement.GetProperty("windows")[0].GetProperty("observedAvailabilityPercent").GetDouble());
        Assert.Equal(JsonValueKind.Object, stats.RootElement.GetProperty("currentIncident").ValueKind);
        using var overview = JsonDocument.Parse(await client.GetStringAsync("/api/overview"));
        Assert.Equal("DOWN", overview.RootElement.GetProperty("health").GetString());
        Assert.Equal(1, overview.RootElement.GetProperty("counts").GetProperty("down").GetInt32());
        await using var db = scenario.Database.Context();
        Assert.Equal(3, await db.Checks.CountAsync());
        Assert.Equal(1, await db.Incidents.CountAsync());
    }

    [Fact]
    public async Task BadQueriesAndUnknownIdsReturnSanitizedProblemDetails()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        using var factory = new AnalyticsApiFactory(scenario);
        using var client = factory.CreateClient();
        foreach (var path in new[]
        {
            "/api/services/service/history?from=private-invalid-value",
            "/api/services/service/history?to=0001-01-01T00%3A00%3A00Z",
            "/api/services/service/history?bucketMinutes=0",
            "/api/services/service/incidents?limit=101",
            "/api/services/service/incidents?beforeId=-1"
        })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain("private-invalid-value", await response.Content.ReadAsStringAsync());
        }
        foreach (var endpoint in new[] { "history", "incidents", "statistics" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/services/missing/{endpoint}")).StatusCode);
    }

    [Fact]
    public async Task UnreadyOrUnreachableStorageReturns503WithoutDatabaseDetails()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        using var factory = new AnalyticsApiFactory(scenario,
            "Host=127.0.0.1;Port=1;Database=private_database;Username=private_role;Timeout=1;Pooling=false");
        using var client = factory.CreateClient();
        foreach (var ready in new[] { false, true })
        {
            factory.Health.SetReady(ready);
            using var response = await client.GetAsync("/api/services/service/statistics");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("private_database", body);
            Assert.DoesNotContain("private_role", body);
            Assert.DoesNotContain("127.0.0.1", body);
        }
    }

    [Fact]
    public async Task OpenApiDescribesRoutesLimitsErrorsAndSafeResponseTypes()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        using var factory = new AnalyticsApiFactory(scenario);
        using var client = factory.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        Assert.StartsWith("3.", json.RootElement.GetProperty("openapi").GetString());
        var paths = json.RootElement.GetProperty("paths");
        var history = paths.GetProperty("/api/services/{id}/history").GetProperty("get");
        Assert.True(history.GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(history.GetProperty("responses").TryGetProperty("503", out _));
        Assert.True(history.GetProperty("responses").TryGetProperty("429", out _));
        Assert.Contains(history.GetProperty("parameters").EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "bucketMinutes");
        Assert.True(paths.TryGetProperty("/api/overview", out _));
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("AvailabilityWindow", out _));
        Assert.False(schemas.TryGetProperty("TargetOptions", out _));
    }

    [Fact]
    public async Task SharedRateLimitRejectsExcessReadsButLeavesLivenessAvailable()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        using var factory = new AnalyticsApiFactory(scenario);
        using var client = factory.CreateClient();
        for (var index = 0; index < 60; index++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/services/service/incidents?limit=0")).StatusCode);
        using var rejected = await client.GetAsync("/api/services/service/incidents?limit=0");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task FourBlockedDatabaseReadsBoundConcurrencyAndTheFifthIsRejected()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        using var factory = new AnalyticsApiFactory(scenario);
        using var client = factory.CreateClient();
        await using var blocker = new NpgsqlConnection(scenario.Database.Admin);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("LOCK TABLE check_results IN ACCESS EXCLUSIVE MODE", blocker, transaction))
            await command.ExecuteNonQueryAsync();
        var reads = Enumerable.Range(0, 4).Select(_ => client.GetAsync("/api/services/service/statistics")).ToArray();
        try
        {
            await WaitForBlockedReadersAsync(scenario.Database, 4);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/services/service/statistics")).StatusCode);
        }
        finally { await transaction.RollbackAsync(); }
        foreach (var response in await Task.WhenAll(reads))
        {
            using (response) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task RequestCancellationCancelsRealSqlAndReturnsConcurrencyPermits()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        using var factory = new AnalyticsApiFactory(scenario);
        using var client = factory.CreateClient();
        await using var blocker = new NpgsqlConnection(scenario.Database.Admin);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("LOCK TABLE check_results IN ACCESS EXCLUSIVE MODE", blocker, transaction))
            await command.ExecuteNonQueryAsync();
        // More requests than the concurrency limit prove permits are returned after cancellation.
        for (var index = 0; index < 5; index++)
        {
            using var cancelled = new CancellationTokenSource();
            var read = client.GetAsync("/api/services/service/statistics", cancelled.Token);
            await WaitForBlockedReadersAsync(scenario.Database, 1);
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            await WaitForBlockedReadersAsync(scenario.Database, 0);
        }
        await transaction.RollbackAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/services/service/statistics")).StatusCode);
    }

    private static async Task WaitForBlockedReadersAsync(TestDatabase database, int expected)
    {
        await using var connection = new NpgsqlConnection(database.Admin);
        await connection.OpenAsync();
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            await using var command = new NpgsqlCommand("""
                SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database() AND usename = 'urpo_runtime' AND wait_event_type = 'Lock'
                """, connection);
            if (Convert.ToInt64(await command.ExecuteScalarAsync()) == expected) return;
            if (deadline.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("SQL readers did not reach the expected lock state.");
            await Task.Delay(5);
        }
    }
}
