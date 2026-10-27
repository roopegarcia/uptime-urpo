using UptimeUrpo.Api.Monitoring;

namespace UptimeUrpo.UnitTests;

public sealed class SamplingCoverageTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 27, 12, 0, 10, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 60, 1)]
    [InlineData(0, 120, 2)]
    [InlineData(50, 110, 2)]
    [InlineData(50, 60, 1)]
    [InlineData(60, 61, 1)]
    [InlineData(-120, -1, 0)]
    [InlineData(-60, 60, 1)]
    public void CountsIntersectingNominalSlotsWithExclusiveEnd(int fromSeconds, int toSeconds, long expected)
    {
        var period = new SamplingPeriod(1, "service", Start, null, true, 60);
        Assert.Equal(expected, SamplingCoverage.ExpectedSlots(period, Start.AddSeconds(fromSeconds), Start.AddSeconds(toSeconds)));
    }

    [Fact]
    public void DisabledAndEndedPeriodsHaveNoExpectedTimeOutsideTheirEnabledRange()
    {
        var period = new SamplingPeriod(1, "service", Start, Start.AddSeconds(90), true, 60);
        Assert.Equal(2, SamplingCoverage.ExpectedSlots(period, Start.AddMinutes(-1), Start.AddMinutes(10)));
        Assert.Equal(90, SamplingCoverage.EnabledSeconds(period, Start.AddMinutes(-1), Start.AddMinutes(10)));
        Assert.Equal(0, SamplingCoverage.ExpectedSlots(period, Start.AddSeconds(90), Start.AddMinutes(10)));
        Assert.Equal(0, SamplingCoverage.ExpectedSlots(period with { Enabled = false }, Start, Start.AddMinutes(10)));
    }

    [Fact]
    public void IntervalChangesUseSeparatePeriodAnchors()
    {
        var first = new SamplingPeriod(1, "service", Start, Start.AddSeconds(90), true, 60);
        var second = new SamplingPeriod(2, "service", Start.AddSeconds(90), null, true, 30);
        var end = Start.AddSeconds(210);
        Assert.Equal(6, SamplingCoverage.ExpectedSlots(first, Start, end) + SamplingCoverage.ExpectedSlots(second, Start, end));
    }
}
