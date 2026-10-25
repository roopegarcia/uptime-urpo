using Microsoft.Extensions.Options;

namespace UptimeUrpo.Api.Monitoring;

public sealed class StartupCheckState
{
    public CheckResult? LastCheck { get; private set; }

    public void Save(CheckResult result) => LastCheck = result;
}

// A one-shot startup hook, not a scheduler or BackgroundService.
public sealed class StartupCheckService : IHostedService
{
    private readonly TargetOptions _target;
    private readonly HttpChecker _checker;
    private readonly StartupCheckState _state;
    private readonly ILogger<StartupCheckService> _logger;

    public StartupCheckService(IOptions<TargetOptions> options, HttpChecker checker,
        StartupCheckState state, ILogger<StartupCheckService> logger)
    {
        _target = options.Value;
        _checker = checker;
        _state = state;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_target.Enabled)
        {
            _logger.LogInformation("Startup check skipped: monitoring is disabled.");
            return;
        }

        var result = await _checker.CheckAsync(cancellationToken);
        _state.Save(result);
        _logger.LogInformation("Startup check completed: success={Success}; status={StatusCode}; category={Category}.",
            result.Success, result.StatusCode, result.FailureCategory);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
