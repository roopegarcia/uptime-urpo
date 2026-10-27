using UptimeUrpo.Api.Monitoring;

namespace UptimeUrpo.UnitTests;

public sealed class MonitoringOptionsTests
{
    [Fact]
    public void ServiceIdMustMatchTheEntireSafeIdentifier() =>
        Assert.False(new TargetOptions { Id = "service\n" }.IsValid());

    [Fact]
    public void LegacyTargetIsSupportedButNotMergedWithTargets()
    {
        var options = new MonitoringOptions { Target = new TargetOptions() };
        Assert.True(options.IsValid());
        Assert.Single(options.GetTargets());
        options.Targets.Add(new TargetOptions { Id = "second" });
        Assert.False(options.IsValid());
    }

    [Fact]
    public void EmptyConfigurationIsSafeAndDuplicateIdsAreRejected()
    {
        var options = new MonitoringOptions();
        Assert.True(options.IsValid());
        options.Targets.Add(new TargetOptions());
        options.Targets.Add(new TargetOptions());
        Assert.False(options.IsValid());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void ConcurrencyIsBounded(int concurrency) =>
        Assert.False(new MonitoringOptions { MaxConcurrency = concurrency }.IsValid());

    [Fact]
    public void TargetCountIsBounded() => Assert.False(new MonitoringOptions
    {
        Targets = Enumerable.Range(0, 21).Select(index => new TargetOptions { Id = $"service-{index}" }).ToList()
    }.IsValid());

    [Theory]
    [InlineData(5, 4, true)]
    [InlineData(5, 5, false)]
    [InlineData(5, 10, false)]
    public void TimeoutMustBeShorterThanTheInterval(int interval, int timeout, bool valid) =>
        Assert.Equal(valid, new TargetOptions { IntervalSeconds = interval, TimeoutSeconds = timeout }.IsValid());

    [Theory]
    [InlineData(4, 3, 2000)]
    [InlineData(86401, 3, 2000)]
    [InlineData(60, 0, 2000)]
    [InlineData(60, 11, 2000)]
    [InlineData(60, 3, 0)]
    [InlineData(60, 3, 30001)]
    public void SchedulingAndHealthLimitsAreValidated(int interval, int failures, int slowMs) =>
        Assert.False(new TargetOptions
        {
            IntervalSeconds = interval,
            FailureThreshold = failures,
            SlowResponseThresholdMs = slowMs
        }.IsValid());
}
