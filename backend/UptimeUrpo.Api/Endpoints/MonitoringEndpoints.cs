using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using UptimeUrpo.Api.Contracts;
using UptimeUrpo.Api.Monitoring;
using UptimeUrpo.Api.Persistence;

namespace UptimeUrpo.Api.Endpoints;

public static class MonitoringEndpoints
{
    public static void MapMonitoringQueries(this WebApplication app)
    {
        var group = app.MapGroup("/api").WithTags("Monitoring history").RequireRateLimiting("database-reads");
        group.MapGet("/services/{id}/statistics", (string id, MonitoringState state, MonitoringQueries queries,
            PersistenceHealth health, ILogger<MonitoringQueries> logger, CancellationToken token) =>
                ExecuteAsync(state.Get(id) is not null, health, logger, async () => Results.Ok(await queries.StatisticsAsync(id, token))))
            .WithName("ServiceStatistics").WithSummary("Observed availability and nominal-slot coverage over 24h, 7d, and 30d.")
            .WithDescription("No observations means null availability. Missing samples never count as confirmed downtime. Current incidents include gap flags.")
            .Produces<ServiceStatisticsResponse>().ProducesProblem(404).ProducesProblem(429).ProducesProblem(503);

        group.MapGet("/services/{id}/history", (string id, [AsParameters] HistoryParameters parameters,
            MonitoringState state, MonitoringQueries queries, IOptions<DatabaseOptions> options, TimeProvider time,
            PersistenceHealth health, ILogger<MonitoringQueries> logger, CancellationToken token) =>
        {
            if (state.Get(id) is null) return Task.FromResult<IResult>(Results.Problem(statusCode: 404, title: "Service not found"));
            if (!parameters.TryResolve(time.GetUtcNow(), options.Value.RetentionDays, out var window, out var errors))
                return Task.FromResult<IResult>(Results.ValidationProblem(errors));
            return ExecuteAsync(true, health, logger, async () => Results.Ok(await queries.HistoryAsync(id,
                window.FromUtc, window.ToUtc, window.BucketMinutes, token)));
        })
            .WithName("ServiceHistory").WithSummary("Bounded response-header timing buckets, including empty gaps.")
            .WithDescription("from/to require ISO-8601 timestamps with offsets; [from,to), at most 30 days inside retention. bucketMinutes: 1,5,15,60,360,1440; auto-selected if omitted. At most 1000 points. Timing aggregates include HTTP responses only.")
            .Produces<HistoryResponse>().ProducesValidationProblem().ProducesProblem(404).ProducesProblem(429).ProducesProblem(503);

        group.MapGet("/services/{id}/incidents", (string id, [AsParameters] IncidentParameters parameters,
            MonitoringState state, MonitoringQueries queries, PersistenceHealth health,
            ILogger<MonitoringQueries> logger, CancellationToken token) =>
        {
            if (state.Get(id) is null) return Task.FromResult<IResult>(Results.Problem(statusCode: 404, title: "Service not found"));
            if (!parameters.TryResolve(out var before, out var limit, out var errors))
                return Task.FromResult<IResult>(Results.ValidationProblem(errors));
            return ExecuteAsync(true, health, logger, async () => Results.Ok(await queries.IncidentsAsync(id, before, limit, token)));
        })
            .WithName("ServiceIncidents").WithSummary("Incidents in descending ID order with a stable cursor.")
            .WithDescription("limit: 1-100, default 20. beforeId: positive previous nextBeforeId. Open-incident duration includes explicitly flagged gaps and is not continuous confirmed downtime.")
            .Produces<IncidentPage>().ProducesValidationProblem().ProducesProblem(404).ProducesProblem(429).ProducesProblem(503);

        group.MapGet("/overview", (MonitoringState state, MonitoringQueries queries, PersistenceHealth health,
            ILogger<MonitoringQueries> logger, CancellationToken token) =>
                ExecuteAsync(true, health, logger, async () => Results.Ok(await queries.OverviewAsync(state.GetAll(), token))))
            .WithName("MonitoringOverview").WithSummary("Current health counts, availability/coverage windows, and open incidents.")
            .WithDescription("Only configured services are visible. Overall enabled-service precedence: DOWN, DEGRADED, UNKNOWN, UP; no enabled services means UNKNOWN. Cached health is current; history windows share one repeatable-read snapshot.")
            .Produces<OverviewResponse>().ProducesProblem(429).ProducesProblem(503);
    }

    private static async Task<IResult> ExecuteAsync(bool exists, PersistenceHealth health,
        ILogger logger, Func<Task<IResult>> read)
    {
        if (!exists) return Results.Problem(statusCode: 404, title: "Service not found");
        if (!health.IsReady) return Results.Problem(statusCode: 503, title: "Monitoring storage is not ready");
        try { return await read(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            logger.LogWarning("Monitoring history read failed ({ErrorType}).", exception.GetType().Name);
            return Results.Problem(statusCode: 503, title: "Monitoring storage is unavailable");
        }
    }

    private static bool IsStorageFailure(Exception exception)
    {
        // EF's non-retrying execution strategy can wrap a transient provider error.
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is NpgsqlException or DbUpdateException) return true;
        return false;
    }
}
