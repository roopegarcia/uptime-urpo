using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using UptimeUrpo.Api.Monitoring;

namespace UptimeUrpo.UnitTests;

public sealed class MonitoringWorkerTests
{
    [Fact]
    public async Task GlobalLimitBoundsRequestsAndQueuedTargetsMakeProgress()
    {
        var targets = Enumerable.Range(0, 5).Select(index => Target($"service-{index}")).ToList();
        var disabled = Target("disabled");
        disabled.Enabled = false;
        targets.Add(disabled);
        await using var harness = new Harness(targets, concurrency: 2);
        await harness.StartAsync();
        var first = await harness.NextAsync();
        var second = await harness.NextAsync();
        Assert.Equal(2, harness.StartedCount);

        first.Respond();
        var third = await harness.NextAsync();
        second.Respond();
        var fourth = await harness.NextAsync();
        third.Respond();
        var fifth = await harness.NextAsync();
        fourth.Respond();
        fifth.Respond();
        await harness.WaitForDelaysAsync(5, 60);

        Assert.Equal(5, harness.StartedCount);
        Assert.Equal(2, harness.MaxActive);
        Assert.Equal(5, harness.StartedIds.Distinct().Count());
        Assert.DoesNotContain("disabled", harness.StartedIds);
    }

    [Fact]
    public async Task SlowRequestDoesNotBlockOtherTargetOrOverlapItself()
    {
        await using var harness = new Harness([Target("slow", 60, 30), Target("fast", 5, 1)], 2);
        await harness.StartAsync();
        var requests = new[] { await harness.NextAsync(), await harness.NextAsync() };
        var slow = requests.Single(request => request.Id == "slow");
        requests.Single(request => request.Id == "fast").Respond();
        await harness.WaitForDelaysAsync(1, 5);

        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        var fastAgain = await harness.NextAsync();
        Assert.Equal("fast", fastAgain.Id);
        Assert.Equal(1, harness.StartedIds.Count(id => id == "slow"));
        Assert.False(slow.Completed);
        fastAgain.Respond();
        slow.Respond();
        await harness.WaitForDelaysAsync(2, 5);
        await harness.WaitForDelaysAsync(1, 60);
        Assert.Equal(2, harness.MaxActive);
    }

    [Fact]
    public async Task InFlightChecksAndLongPausesNeverCreateOverlapOrCatchUpBursts()
    {
        await using var harness = new Harness([Target("one", 60, 30)]);
        await harness.StartAsync();
        var first = await harness.NextAsync();
        harness.Clock.Advance(TimeSpan.FromSeconds(15));
        Assert.Equal(1, harness.StartedCount);
        Assert.False(first.Completed);
        first.Respond();
        await harness.WaitForDelaysAsync(1, 60);

        harness.Clock.Advance(TimeSpan.FromHours(1));
        var second = await harness.NextAsync();
        Assert.Equal(2, harness.StartedCount);
        second.Respond();
        await harness.WaitForDelaysAsync(2, 60);
        Assert.Equal(2, harness.StartedCount);
        harness.Clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(2, harness.StartedCount);
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        await harness.NextAsync();
        Assert.Equal(3, harness.StartedCount);
        Assert.Equal(1, harness.MaxActive);
    }

    [Fact]
    public async Task TargetIntervalsAreIndependent()
    {
        await using var harness = new Harness([Target("fast", 5, 1), Target("slow", 10, 1)]);
        await harness.StartAsync();
        (await harness.NextAsync()).Respond();
        (await harness.NextAsync()).Respond();
        await harness.WaitForDelaysAsync(1, 5);
        await harness.WaitForDelaysAsync(1, 10);
        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        var fast = await harness.NextAsync();
        Assert.Equal("fast", fast.Id);
        fast.Respond();
        await harness.WaitForDelaysAsync(2, 5);
        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        var next = new[] { await harness.NextAsync(), await harness.NextAsync() };
        Assert.Contains(next, request => request.Id == "fast");
        Assert.Contains(next, request => request.Id == "slow");
    }

    [Fact]
    public async Task ShutdownCancelsActiveAndQueuedChecksWithoutRecordingFailures()
    {
        await using var harness = new Harness([Target("first"), Target("second"), Target("third")], 1);
        await harness.StartAsync();
        await harness.NextAsync();
        await harness.StopAsync();
        Assert.Equal(1, harness.StartedCount);
        Assert.Equal(0, harness.Active);
        Assert.All(harness.State.GetAll(), service => Assert.Null(service.LastCheck));
        harness.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, harness.StartedCount);
    }

    [Fact]
    public async Task ShutdownAlsoCancelsIntervalDelays()
    {
        await using var harness = new Harness([Target("one")]);
        await harness.StartAsync();
        (await harness.NextAsync()).Respond();
        await harness.WaitForDelaysAsync(1, 60);
        await harness.StopAsync();
        harness.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, harness.StartedCount);
        Assert.True(harness.State.Get("one")!.LastCheck!.Success);
        Assert.Equal(HealthStatus.Unknown, harness.State.Get("one")!.Health);
    }

    [Fact]
    public async Task UnexpectedCheckFailureLeavesAGapAndNextIntervalCanRecover()
    {
        await using var harness = new Harness([Target("one")]);
        await harness.StartAsync();
        (await harness.NextAsync()).Fail(new InvalidOperationException("private detail"));
        await harness.WaitForDelaysAsync(1, 60);
        Assert.Null(harness.State.Get("one")!.LastCheck);
        Assert.Equal(HealthStatus.Unknown, harness.State.Get("one")!.Health);

        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        (await harness.NextAsync()).Respond();
        await harness.WaitForDelaysAsync(2, 60);
        Assert.Equal(HealthStatus.Up, harness.State.Get("one")!.Health);
    }

    [Fact]
    public async Task PerTargetTimeoutDoesNotCancelOtherChecks()
    {
        await using var harness = new Harness([Target("timeout", timeout: 5), Target("healthy", timeout: 30)], 2);
        await harness.StartAsync();
        var pending = new[] { await harness.NextAsync(), await harness.NextAsync() };
        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        await harness.WaitForDelaysAsync(1, 60);
        Assert.Equal(FailureCategory.Timeout, harness.State.Get("timeout")!.LastCheck!.FailureCategory);
        Assert.Equal(HealthStatus.Degraded, harness.State.Get("timeout")!.Health);
        pending.Single(request => request.Id == "healthy").Respond();
        await harness.WaitForDelaysAsync(2, 60);
        Assert.True(harness.State.Get("healthy")!.LastCheck!.Success);
        Assert.Equal(HealthStatus.Degraded, harness.State.Get("healthy")!.Health); // Successful, but took five seconds.
    }

    private static TargetOptions Target(string id, int interval = 60, int timeout = 10) => new()
    {
        Id = id,
        Url = $"https://example.com/{id}",
        Enabled = true,
        IntervalSeconds = interval,
        TimeoutSeconds = timeout
    };

    private sealed class Harness : IAsyncDisposable
    {
        private readonly Channel<Pending> _started = Channel.CreateUnbounded<Pending>();
        private readonly MonitoringWorker _worker;
        private int _active;
        private int _maxActive;
        private int _startedCount;
        private readonly object _statsGate = new();
        public TestClock Clock { get; } = new();
        public MonitoringState State { get; }
        public ConcurrentQueue<string> StartedIds { get; } = new();
        public int StartedCount => Volatile.Read(ref _startedCount);
        public int Active => Volatile.Read(ref _active);
        public int MaxActive => Volatile.Read(ref _maxActive);

        public Harness(List<TargetOptions> targets, int concurrency = 3)
        {
            var options = Options.Create(new MonitoringOptions { Targets = targets, MaxConcurrency = concurrency });
            State = new MonitoringState(options, Clock);
            var checker = new HttpChecker(new ClientFactory(new Handler(SendAsync)), Clock);
            _worker = new MonitoringWorker(options, checker, State, Clock, NullLogger<MonitoringWorker>.Instance);
        }

        public Task StartAsync() => _worker.StartAsync(CancellationToken.None);
        public async Task StopAsync() => await _worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        public async Task<Pending> NextAsync() => await _started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));

        public async Task WaitForDelaysAsync(int count, int seconds)
        {
            var deadline = Stopwatch.StartNew();
            while (Clock.CountTimers(seconds) < count)
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("Worker did not reach its interval delay.");
                await Task.Delay(1); // Coordinate task continuations; scenario time uses the fake clock.
            }
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var active = Interlocked.Increment(ref _active);
            lock (_statsGate) _maxActive = Math.Max(active, _maxActive);
            Interlocked.Increment(ref _startedCount);
            var pending = new Pending(request.RequestUri!.AbsolutePath.Trim('/'));
            StartedIds.Enqueue(pending.Id);
            _started.Writer.TryWrite(pending);
            try { return await pending.Response.Task.WaitAsync(token); }
            finally { Interlocked.Decrement(ref _active); }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _worker.Dispose();
        }
    }

    private sealed class TestClock : FakeTimeProvider
    {
        private readonly ConcurrentDictionary<TimeSpan, int> _timers = new();
        public int CountTimers(int seconds) => _timers.GetValueOrDefault(TimeSpan.FromSeconds(seconds));
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            _timers.AddOrUpdate(dueTime, 1, (_, count) => count + 1);
            return timer;
        }
    }

    private sealed class Pending(string id)
    {
        public string Id { get; } = id;
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Completed => Response.Task.IsCompleted;
        public void Respond() => Response.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
        public void Fail(Exception exception) => Response.SetException(exception);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
