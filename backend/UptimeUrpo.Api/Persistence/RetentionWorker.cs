using Microsoft.Extensions.Options;

namespace UptimeUrpo.Api.Persistence;

public sealed class RetentionWorker(IServiceScopeFactory scopes, IOptions<DatabaseOptions> options,
    PersistenceHealth health, TimeProvider time, ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (health.IsReady)
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        var store = scope.ServiceProvider.GetRequiredService<IMonitoringStore>();
                        var deleted = await store.DeleteOldChecksAsync(time.GetUtcNow().AddDays(-options.Value.RetentionDays),
                            options.Value.RetentionBatchSize, stoppingToken);
                        logger.LogInformation("History retention removed {Count} rows.", deleted);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        logger.LogError("History retention failed ({ErrorType}).", exception.GetType().Name);
                    }
                }
                // One bounded batch per minute; no request path or unbounded deletion loop.
                await Task.Delay(TimeSpan.FromMinutes(1), time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
