using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using UptimeUrpo.Api.Monitoring;

namespace UptimeUrpo.UnitTests;

public sealed class MonitoringStateTests
{
    [Fact]
    public void NoResultAndDisabledTargetsAreUnknown()
    {
        var (state, _) = CreateState();
        Assert.Equal(HealthStatus.Unknown, state.Get("enabled")!.Health);
        Assert.Null(state.Get("enabled")!.LastCheck);
        state.Record("disabled", Result(success: true));
        Assert.Equal(HealthStatus.Unknown, state.Get("disabled")!.Health);
        Assert.Null(state.Get("disabled")!.LastCheck);
        Assert.Null(state.Get("missing"));
    }

    [Fact]
    public void FailureThresholdAndRecoveryUseConsecutiveObservations()
    {
        var (state, clock) = CreateState();
        state.Record("enabled", Result(success: false));
        Assert.Equal(HealthStatus.Degraded, state.Get("enabled")!.Health);
        Assert.Equal(1, state.Get("enabled")!.ConsecutiveFailures);
        clock.Advance(TimeSpan.FromSeconds(60));
        state.Record("enabled", Result(success: false));
        Assert.Equal(HealthStatus.Degraded, state.Get("enabled")!.Health);
        clock.Advance(TimeSpan.FromSeconds(60));
        state.Record("enabled", Result(success: false));
        Assert.Equal(HealthStatus.Down, state.Get("enabled")!.Health);

        state.Record("enabled", Result(success: true));
        Assert.Equal(HealthStatus.Up, state.Get("enabled")!.Health);
        Assert.Equal(0, state.Get("enabled")!.ConsecutiveFailures);
        state.Record("enabled", Result(success: false));
        Assert.Equal(HealthStatus.Degraded, state.Get("enabled")!.Health);
        Assert.Equal(1, state.Get("enabled")!.ConsecutiveFailures);
    }

    [Theory]
    [InlineData(2000, HealthStatus.Up)]
    [InlineData(2000.1, HealthStatus.Degraded)]
    public void SlowSuccessRecoversDownAndResetsTheStreak(double duration, HealthStatus expected)
    {
        var (state, _) = CreateState();
        for (var index = 0; index < 3; index++) state.Record("enabled", Result(success: false));
        state.Record("enabled", Result(success: true, duration));
        Assert.Equal(expected, state.Get("enabled")!.Health);
        Assert.Equal(0, state.Get("enabled")!.ConsecutiveFailures);
    }

    [Fact]
    public void StaleThresholdUsesCompletionAndBreaksTheFailureStreak()
    {
        var (state, clock) = CreateState();
        state.Record("enabled", Result(success: false));
        state.Record("enabled", Result(success: false));
        var observation = state.Get("enabled")!.LastCheck;
        clock.Advance(TimeSpan.FromSeconds(120));
        Assert.Equal(HealthStatus.Degraded, state.Get("enabled")!.Health);
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(HealthStatus.Unknown, state.Get("enabled")!.Health);
        Assert.Equal(0, state.Get("enabled")!.ConsecutiveFailures);
        Assert.Same(observation, state.Get("enabled")!.LastCheck);
        state.Record("enabled", Result(success: false));
        Assert.Equal(HealthStatus.Degraded, state.Get("enabled")!.Health);
        Assert.Equal(1, state.Get("enabled")!.ConsecutiveFailures);
    }

    [Fact]
    public void UtcClockAdjustmentDoesNotChangeFreshness()
    {
        var clock = new ShiftableClock();
        var (state, _) = CreateState(clock);
        state.Record("enabled", Result(success: true));
        clock.UtcOffset = TimeSpan.FromDays(365);
        Assert.Equal(HealthStatus.Up, state.Get("enabled")!.Health);
        clock.Advance(TimeSpan.FromSeconds(121));
        Assert.Equal(HealthStatus.Unknown, state.Get("enabled")!.Health);
    }

    [Fact]
    public void FreshnessStartsAtCompletionEvenIfRequestStartedEarlier()
    {
        var (state, clock) = CreateState();
        var oldStart = clock.GetUtcNow().AddMinutes(-10);
        state.Record("enabled", Result(success: true) with { CheckedAtUtc = oldStart });
        var response = state.Get("enabled")!;
        Assert.Equal(HealthStatus.Up, response.Health);
        Assert.Equal(clock.GetUtcNow(), response.LastCompletedAtUtc);
        Assert.Equal(oldStart, response.LastCheck!.CheckedAtUtc);
    }

    [Fact]
    public void CustomThresholdAppliesAndCountersSaturate()
    {
        var clock = new FakeTimeProvider();
        var options = new MonitoringOptions
        {
            Targets = [new TargetOptions { Id = "custom", Enabled = true, FailureThreshold = 2, SlowResponseThresholdMs = 50 }]
        };
        var state = new MonitoringState(Options.Create(options), clock);
        for (var index = 0; index < 20; index++) state.Record("custom", Result(success: false));
        Assert.Equal(HealthStatus.Down, state.Get("custom")!.Health);
        Assert.Equal(2, state.Get("custom")!.ConsecutiveFailures);
        state.Record("custom", Result(success: true, duration: 51));
        Assert.Equal(HealthStatus.Degraded, state.Get("custom")!.Health);
    }

    private static CheckResult Result(bool success, double duration = 10) =>
        new(DateTimeOffset.UtcNow, duration, success ? 200 : 503, success,
            success ? null : FailureCategory.UnexpectedStatus);

    private static (MonitoringState State, FakeTimeProvider Clock) CreateState(FakeTimeProvider? clock = null)
    {
        clock ??= new FakeTimeProvider();
        var options = new MonitoringOptions
        {
            Targets = [new TargetOptions { Id = "enabled", Enabled = true }, new TargetOptions { Id = "disabled" }]
        };
        return (new MonitoringState(Options.Create(options), clock), clock);
    }

    private sealed class ShiftableClock : FakeTimeProvider
    {
        public TimeSpan UtcOffset { get; set; }
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + UtcOffset;
        public override long GetTimestamp() => base.GetUtcNow().UtcTicks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
