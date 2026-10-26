using Microsoft.Extensions.Options;
using UptimeUrpo.Api.Persistence;

namespace UptimeUrpo.Api.Monitoring;

public sealed class MonitoringWorker : BackgroundService
{
    private readonly MonitoringOptions _options;
    private readonly HttpChecker _checker;
    private readonly MonitoringState _state;
    private readonly TimeProvider _time;
    private readonly ILogger<MonitoringWorker> _logger;
    private readonly IServiceScopeFactory _scopes;
    private readonly PersistenceHealth _health;

    public MonitoringWorker(IOptions<MonitoringOptions> options, HttpChecker checker,
        MonitoringState state, TimeProvider time, ILogger<MonitoringWorker> logger,
        IServiceScopeFactory scopes, PersistenceHealth health)
    {
        _options = options.Value;
        _checker = checker;
        _state = state;
        _time = time;
        _logger = logger;
        _scopes = scopes;
        _health = health;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Target is not null)
            _logger.LogWarning("Legacy Monitoring:Target configuration is supported; use Monitoring:Targets for multiple services.");

        using var slots = new SemaphoreSlim(_options.MaxConcurrency);
        try
        {
            await InitializeAsync(stoppingToken);
            await Task.WhenAll(_options.GetTargets().Where(target => target.Enabled)
                .Select(target => MonitorAsync(target, slots, stoppingToken)));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // StopAsync waits for these loops, their requests, and delays to finish.
        }
    }

    private async Task InitializeAsync(CancellationToken token)
    {
        while (true)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IMonitoringStore>();
                if (!await store.IsSchemaReadyAsync(token))
                    throw new InvalidOperationException("Monitoring database migrations must be applied first.");
                _state.Restore(await store.SynchronizeAsync(token));
                _health.SetReady(true);
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                _health.SetReady(false);
                _logger.LogError("Monitoring storage initialization failed ({ErrorType}); retrying in five seconds.", exception.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), _time, token);
        }
    }

    private async Task MonitorAsync(TargetOptions target, SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await slots.WaitAsync(stoppingToken);
            try
            {
                var result = await _checker.CheckAsync(target, stoppingToken);
                stoppingToken.ThrowIfCancellationRequested();
                var observation = _state.Prepare(target.Id, result);
                try
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IMonitoringStore>().SaveAsync(target, observation, stoppingToken);
                    _state.Apply(observation);
                    _health.SetReady(true);
                }
                catch
                {
                    _health.SetReady(false);
                    throw;
                }
                _logger.LogInformation("Check completed for {ServiceId}: success={Success}; status={StatusCode}; category={Category}.",
                    target.Id, result.Success, result.StatusCode, result.FailureCategory);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // An internal check failure is not evidence of target downtime. Keep a gap,
                // report the safe exception type, and allow the next interval to recover.
                _logger.LogError("Check could not complete for {ServiceId} ({ErrorType}); no observation recorded.",
                    target.Id, exception.GetType().Name);
            }
            finally
            {
                slots.Release();
            }

            // Fixed delay after completion: no overlap, missed-run queue, or catch-up burst.
            await Task.Delay(TimeSpan.FromSeconds(target.IntervalSeconds), _time, stoppingToken);
        }
    }
}
