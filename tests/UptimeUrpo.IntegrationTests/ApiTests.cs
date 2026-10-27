using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using UptimeUrpo.Api.Monitoring;
using UptimeUrpo.Api.Persistence;
using UptimeUrpo.TestSupport;

namespace UptimeUrpo.IntegrationTests;

public sealed class ApiTests
{
    [Fact]
    public async Task ReadsReturnTheSavedResultWithoutTriggeringMoreChecks()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await factory.WaitForIntervalsAsync(1);

        var first = await client.GetStringAsync("/api/services/test-service");
        var second = await client.GetStringAsync("/api/services/test-service");
        using var json = JsonDocument.Parse(first);
        var service = json.RootElement;
        Assert.Equal("test-service", service.GetProperty("id").GetString());
        Assert.Equal("Test service", service.GetProperty("name").GetString());
        Assert.True(service.GetProperty("enabled").GetBoolean());
        Assert.Equal("UP", service.GetProperty("health").GetString());
        var check = service.GetProperty("lastCheck");
        Assert.Equal(200, check.GetProperty("statusCode").GetInt32());
        Assert.True(check.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, check.GetProperty("failureCategory").ValueKind);
        Assert.True(check.GetProperty("durationMs").GetDouble() >= 0);
        Assert.Equal(TimeSpan.Zero, check.GetProperty("checkedAtUtc").GetDateTimeOffset().Offset);
        Assert.Equal(TimeSpan.Zero, service.GetProperty("lastCompletedAtUtc").GetDateTimeOffset().Offset);
        Assert.Equal(first, second);
        Assert.Equal(1, factory.Handler.Calls);
        Assert.False(service.TryGetProperty("url", out _));
        Assert.DoesNotContain("example.com", first);
    }

    [Fact]
    public async Task ListIncludesConfiguredServicesAndDisabledUnknownState()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await factory.WaitForIntervalsAsync(1);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/services"));
        Assert.Equal(2, json.RootElement.GetArrayLength());
        var disabled = json.RootElement.EnumerateArray().Single(service => service.GetProperty("id").GetString() == "disabled-service");
        Assert.False(disabled.GetProperty("enabled").GetBoolean());
        Assert.Equal("UNKNOWN", disabled.GetProperty("health").GetString());
        Assert.Equal(JsonValueKind.Null, disabled.GetProperty("lastCheck").ValueKind);
        Assert.Equal(1, factory.Handler.Calls);
    }

    [Fact]
    public async Task ScheduledFailuresReachDownAndASuccessRecovers()
    {
        using var factory = new ApiFactory(status: 503);
        using var client = factory.CreateClient();
        await factory.WaitForIntervalsAsync(1);
        Assert.Equal("DEGRADED", await HealthAsync(client));
        for (var count = 2; count <= 3; count++)
        {
            factory.Clock.Advance(TimeSpan.FromSeconds(60));
            await factory.WaitForIntervalsAsync(count);
        }
        Assert.Equal("DOWN", await HealthAsync(client));
        factory.Handler.Status = 200;
        factory.Clock.Advance(TimeSpan.FromSeconds(60));
        await factory.WaitForIntervalsAsync(4);
        Assert.Equal("UP", await HealthAsync(client));
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/services/test-service"));
        Assert.Equal(0, json.RootElement.GetProperty("consecutiveFailures").GetInt32());
        Assert.Equal(4, factory.Handler.Calls);
    }

    [Fact]
    public async Task TargetFailureDoesNotFailApplicationLiveness()
    {
        using var factory = new ApiFactory(status: 503);
        using var client = factory.CreateClient();
        await factory.WaitForIntervalsAsync(1);
        Assert.Equal("{\"status\":\"ok\"}", await client.GetStringAsync("/health"));
        using var response = await client.GetAsync("/api/services/test-service");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var check = json.RootElement.GetProperty("lastCheck");
        Assert.False(check.GetProperty("success").GetBoolean());
        Assert.Equal("unexpected_status", check.GetProperty("failureCategory").GetString());
        Assert.Equal(503, check.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task PendingFirstCheckDoesNotBlockLivenessAndRemainsUnknown()
    {
        using var factory = new ApiFactory(blockRequest: true);
        using var client = factory.CreateClient();
        await factory.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("{\"status\":\"ok\"}", await client.GetStringAsync("/health"));
        Assert.Equal("UNKNOWN", await HealthAsync(client));
    }

    [Fact]
    public async Task StaleResultsRemainVisibleButBecomeUnknown()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await factory.WaitForIntervalsAsync(1);
        var worker = factory.Services.GetServices<IHostedService>().OfType<MonitoringWorker>().Single();
        await worker.StopAsync(CancellationToken.None);
        factory.Clock.Advance(TimeSpan.FromSeconds(121));
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/services/test-service"));
        Assert.Equal("UNKNOWN", json.RootElement.GetProperty("health").GetString());
        Assert.True(json.RootElement.GetProperty("lastCheck").GetProperty("success").GetBoolean());
        Assert.Equal(1, factory.Handler.Calls);
    }

    [Fact]
    public async Task DisabledMonitoringMakesNoOutboundRequest()
    {
        using var factory = new ApiFactory(enabled: false);
        using var client = factory.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/services/test-service"));
        Assert.False(json.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal("UNKNOWN", json.RootElement.GetProperty("health").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("lastCheck").ValueKind);
        Assert.Equal(0, factory.Handler.Calls);
    }

    [Fact]
    public async Task UnknownIdReturnsProblemDetails()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/services/missing");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(404, json.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task NetworkDetailsAreNotExposedInResponses()
    {
        using var factory = new ApiFactory(networkFailure: true);
        using var client = factory.CreateClient();
        await factory.WaitForIntervalsAsync(1);
        var body = await client.GetStringAsync("/api/services/test-service");
        using var json = JsonDocument.Parse(body);
        var check = json.RootElement.GetProperty("lastCheck");
        Assert.Equal("dns", check.GetProperty("failureCategory").GetString());
        Assert.Equal(JsonValueKind.Null, check.GetProperty("statusCode").ValueKind);
        Assert.DoesNotContain("private-network-detail", body);
        Assert.DoesNotContain("example.com", body);
    }

    [Fact]
    public async Task LegacySingleTargetConfigurationStillWorks()
    {
        using var factory = new ApiFactory(legacy: true);
        using var client = factory.CreateClient();
        await factory.WaitForIntervalsAsync(1);
        Assert.Equal("UP", await HealthAsync(client));
    }

    [Theory]
    [InlineData("url")]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("timeout")]
    [InlineData("status")]
    [InlineData("interval")]
    [InlineData("threshold")]
    [InlineData("slow")]
    [InlineData("duplicate")]
    [InlineData("mixed")]
    [InlineData("concurrency")]
    public void InvalidConfigurationFailsBeforeChecking(string field)
    {
        using var factory = new ApiFactory(invalidField: field);
        // The deferred test host can dispose its provider before surfacing the startup error.
        // Assert the actual host validation failure as well as failed startup and no checks.
        var startupError = Record.Exception(() => factory.CreateClient());
        Assert.True(startupError is OptionsValidationException or ObjectDisposedException,
            $"Unexpected startup error: {startupError}");
        var validation = Assert.IsType<OptionsValidationException>(factory.StartupValidationFailure);
        Assert.Equal(typeof(MonitoringOptions), validation.OptionsType);
        Assert.Equal(0, factory.Handler.Calls);
    }

    [Fact]
    public async Task DashboardFilesShareTheOriginWithoutSwallowingApiErrors()
    {
        var directory = Path.Combine(Path.GetTempPath(), "urpo-dashboard-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "wwwroot", "assets"));
        await File.WriteAllTextAsync(Path.Combine(directory, "wwwroot", "index.html"), "<!doctype html><title>Dashboard fixture</title>");
        await File.WriteAllTextAsync(Path.Combine(directory, "wwwroot", "assets", "app.js"), "console.log('fixture');");
        try
        {
            using var factory = new ApiFactory(enabled: false);
            using var configured = factory.WithWebHostBuilder(builder => builder.UseContentRoot(directory));
            using var client = configured.CreateClient();
            using var index = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, index.StatusCode);
            Assert.Equal("text/html", index.Content.Headers.ContentType?.MediaType);
            Assert.Contains("Dashboard fixture", await index.Content.ReadAsStringAsync());
            Assert.Equal("no-cache", index.Headers.CacheControl?.ToString());
            Assert.Equal("nosniff", index.Headers.GetValues("X-Content-Type-Options").Single());
            using var asset = await client.GetAsync("/assets/app.js");
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
            Assert.Equal("text/javascript", asset.Content.Headers.ContentType?.MediaType);
            Assert.Equal("{\"status\":\"ok\"}", await client.GetStringAsync("/health"));
            using var missing = await client.GetAsync("/api/not-a-route");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
            using var missingAsset = await client.GetAsync("/assets/missing.js");
            Assert.Equal(HttpStatusCode.NotFound, missingAsset.StatusCode);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<string?> HealthAsync(HttpClient client)
    {
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/services/test-service"));
        return json.RootElement.GetProperty("health").GetString();
    }

    private sealed class ApiFactory(bool enabled = true, int status = 200, bool networkFailure = false,
        string? invalidField = null, bool blockRequest = false, bool legacy = false) : WebApplicationFactory<Program>
    {
        public RecordingHandler Handler { get; } = new(status, networkFailure, blockRequest);
        public ObservingClock Clock { get; } = new();
        public FakeMonitoringStore Store { get; } = new();
        public OptionsValidationException? StartupValidationFailure { get; private set; }

        public async Task WaitForIntervalsAsync(int count)
        {
            var deadline = Stopwatch.StartNew();
            while (Clock.IntervalTimers < count)
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("Worker did not reach its interval delay.");
                await Task.Delay(1);
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                if (invalidField is not null)
                {
                    logging.AddProvider(new StartupValidationLogger(error => StartupValidationFailure = error));
                }
            });
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(service => service.ServiceType == typeof(IHostedService)
                    && service.ImplementationType == typeof(RetentionWorker)).ToArray()) services.Remove(descriptor);
                services.RemoveAll<IMonitoringStore>();
                services.AddSingleton<IMonitoringStore>(Store);
                services.PostConfigure<DatabaseOptions>(options =>
                    options.ConnectionString = "Host=127.0.0.1;Database=test;Username=test");
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                // Clear both shapes so ignored operator configuration can never reach the network.
                services.PostConfigure<MonitoringOptions>(options =>
                {
                    var target = new TargetOptions
                    {
                        Id = "test-service",
                        Name = "Test service",
                        Url = "https://example.com/",
                        Enabled = enabled,
                        TimeoutSeconds = 1
                    };
                    options.Target = legacy ? target : null;
                    options.Targets = legacy ? [] : [target, new TargetOptions { Id = "disabled-service", Name = "Disabled service" }];
                    options.MaxConcurrency = 3;
                    switch (invalidField)
                    {
                        case "url": target.Url = "https://127.0.0.1/"; break;
                        case "id": target.Id = "bad id"; break;
                        case "name": target.Name = "bad\nname"; break;
                        case "timeout": target.TimeoutSeconds = 0; break;
                        case "status": target.ExpectedStatusCode = 999; break;
                        case "interval": target.IntervalSeconds = 0; break;
                        case "threshold": target.FailureThreshold = 0; break;
                        case "slow": target.SlowResponseThresholdMs = 0; break;
                        case "duplicate": options.Targets.Add(target); break;
                        case "mixed": options.Target = target; break;
                        case "concurrency": options.MaxConcurrency = 9; break;
                    }
                });
                services.AddHttpClient(HttpChecker.ClientName).ConfigurePrimaryHttpMessageHandler(() => Handler);
            });
        }
    }

    // Observe the original startup error before app.Run disposes a failed host.
    private sealed class StartupValidationLogger(Action<OptionsValidationException> capture) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Dispose() { }
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is OptionsValidationException validation) capture(validation);
        }
    }

    private sealed class ObservingClock : FakeTimeProvider
    {
        private int _intervalTimers;
        public int IntervalTimers => Volatile.Read(ref _intervalTimers);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            if (dueTime == TimeSpan.FromSeconds(60)) Interlocked.Increment(ref _intervalTimers);
            return timer;
        }
    }

    private sealed class RecordingHandler(int status, bool networkFailure, bool blockRequest) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public int Status = status;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            Started.TrySetResult();
            if (blockRequest) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            if (networkFailure) throw new HttpRequestException(HttpRequestError.NameResolutionError, "private-network-detail");
            return new HttpResponseMessage((HttpStatusCode)Volatile.Read(ref Status));
        }
    }
}
