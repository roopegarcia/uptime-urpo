using Microsoft.Extensions.Options;

namespace UptimeUrpo.Api.Monitoring;

public sealed class MonitoringWorker : BackgroundService
{
    private readonly MonitoringOptions _options;
    private readonly HttpChecker _checker;
    private readonly MonitoringState _state;
    private readonly TimeProvider _time;
    private readonly ILogger<MonitoringWorker> _logger;

    public MonitoringWorker(IOptions<MonitoringOptions> options, HttpChecker checker,
        MonitoringState state, TimeProvider time, ILogger<MonitoringWorker> logger)
    {
        _options = options.Value;
        _checker = checker;
        _state = state;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Target is not null)
            _logger.LogWarning("Legacy Monitoring:Target configuration is supported; use Monitoring:Targets for multiple services.");

        using var slots = new SemaphoreSlim(_options.MaxConcurrency);
        try
        {
            await Task.WhenAll(_options.GetTargets().Where(target => target.Enabled)
                .Select(target => MonitorAsync(target, slots, stoppingToken)));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // StopAsync waits for these loops, their requests, and delays to finish.
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
                _state.Record(target.Id, result);
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
