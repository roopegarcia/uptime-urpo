using Microsoft.EntityFrameworkCore;
using UptimeUrpo.Api.Monitoring;
using UptimeUrpo.Api.Persistence;

namespace UptimeUrpo.IntegrationTests;

[Collection("PostgreSQL")]
[Trait("Category", "Database")]
public sealed class AnalyticsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task SampleAvailabilityAndDistinctSlotCoverageHaveDifferentDenominators()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        scenario.Clock.Advance(TimeSpan.FromSeconds(10));
        await scenario.SaveAsync(duration: 100);
        scenario.Clock.Advance(TimeSpan.FromSeconds(10));
        await scenario.SaveAsync(503, 400); // Same nominal slot: another sample, not more coverage.
        scenario.Clock.Advance(TimeSpan.FromSeconds(100));
        await using var db = scenario.Database.Context();
        var queries = new MonitoringQueries(db, scenario.Clock);
        var stats = await queries.StatisticsAsync("service", CancellationToken.None);
        Assert.All(stats.Windows, window =>
        {
            Assert.Equal(2, window.CompletedChecks);
            Assert.Equal(1, window.SuccessfulChecks);
            Assert.Equal(1, window.FailedChecks);
            Assert.Equal(50, window.ObservedAvailabilityPercent);
            Assert.Equal(2, window.ExpectedOpportunities);
            Assert.Equal(1, window.ObservedOpportunities);
            Assert.Equal(1, window.MissingOpportunities);
            Assert.Equal(50, window.CoveragePercent);
        });
        var history = await queries.HistoryAsync("service", MonitoringScenario.Start, scenario.Clock.GetUtcNow(), 1, CancellationToken.None);
        Assert.Equal(2, history.Points.Count);
        Assert.Equal("observed", history.Points[0].State);
        Assert.Equal(250, history.Points[0].AverageResponseMs);
        Assert.Equal(100, history.Points[0].MinimumResponseMs);
        Assert.Equal(400, history.Points[0].MaximumResponseMs);
        Assert.Equal("missing", history.Points[1].State);
        Assert.Null(history.Points[1].AverageResponseMs);
        Assert.Equal(0, history.Points[1].CompletedChecks);
    }

    [Fact]
    public async Task NoEnabledMonitoringHasNullAvailabilityAndCoverage()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture, enabled: false);
        scenario.Clock.Advance(TimeSpan.FromSeconds(120));
        await using var db = scenario.Database.Context();
        var queries = new MonitoringQueries(db, scenario.Clock);
        var stats = await queries.StatisticsAsync("service", CancellationToken.None);
        Assert.All(stats.Windows, window =>
        {
            Assert.Null(window.ObservedAvailabilityPercent);
            Assert.Null(window.CoveragePercent);
            Assert.Equal(0, window.ExpectedOpportunities);
        });
        var history = await queries.HistoryAsync("service", MonitoringScenario.Start.AddMinutes(-1), scenario.Clock.GetUtcNow(), 1, CancellationToken.None);
        Assert.All(history.Points, point => Assert.Equal("not_monitored", point.State));
    }

    [Fact]
    public async Task MissingEnabledSamplesAreUnknownEvidenceRatherThanDowntime()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        await using var db = scenario.Database.Context();
        var stats = await new MonitoringQueries(db, scenario.Clock).StatisticsAsync("service", CancellationToken.None);
        Assert.All(stats.Windows, window =>
        {
            Assert.Equal(1, window.ExpectedOpportunities);
            Assert.Equal(0, window.CompletedChecks);
            Assert.Equal(0, window.FailedChecks);
            Assert.Null(window.ObservedAvailabilityPercent);
            Assert.Equal(0, window.CoveragePercent);
        });
    }

    [Fact]
    public async Task NonHttpFailuresDoNotBecomeResponseTimesAndPartialBucketsStayExplicit()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        await scenario.SaveAsync(status: null, duration: 700);
        scenario.Clock.Advance(TimeSpan.FromSeconds(75));
        await using var db = scenario.Database.Context();
        var history = await new MonitoringQueries(db, scenario.Clock).HistoryAsync("service",
            MonitoringScenario.Start.AddMinutes(-1), scenario.Clock.GetUtcNow(), 1, CancellationToken.None);
        Assert.Equal(3, history.Points.Count);
        Assert.Equal("not_monitored", history.Points[0].State);
        Assert.Equal("observed", history.Points[1].State);
        Assert.Equal(1, history.Points[1].FailedChecks);
        Assert.Equal(0, history.Points[1].HttpResponses);
        Assert.Null(history.Points[1].AverageResponseMs);
        Assert.Equal("missing", history.Points[2].State);
        Assert.Equal(15, history.Points[2].EnabledSeconds);
        Assert.Equal(scenario.Clock.GetUtcNow(), history.Points[2].ToUtc);
    }

    [Fact]
    public async Task IntervalChangesUseTheirOwnAnchorsAndWindowsUseExclusiveEnds()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        await scenario.SaveAsync();
        scenario.Clock.Advance(TimeSpan.FromSeconds(90));
        scenario.Target.IntervalSeconds = 30;
        await scenario.SynchronizeAsync();
        await scenario.SaveAsync();
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        await using var db = scenario.Database.Context();
        var queries = new MonitoringQueries(db, scenario.Clock);
        var stats = await queries.StatisticsAsync("service", CancellationToken.None);
        Assert.All(stats.Windows, window =>
        {
            Assert.Equal(4, window.ExpectedOpportunities);
            Assert.Equal(2, window.ObservedOpportunities);
            Assert.Equal(50, window.CoveragePercent);
            Assert.Equal(100, window.ObservedAvailabilityPercent);
        });
        var history = await queries.HistoryAsync("service", MonitoringScenario.Start,
            MonitoringScenario.Start.AddSeconds(90), 1, CancellationToken.None);
        Assert.Equal(1, history.Points.Sum(point => point.CompletedChecks)); // The result at exactly to is excluded.
    }

    [Fact]
    public async Task OverviewCountsOnlyEnabledHealthAndHandlesAnEmptyConfiguration()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        await scenario.SaveAsync();
        scenario.Clock.Advance(TimeSpan.FromSeconds(1));
        await using var db = scenario.Database.Context();
        var queries = new MonitoringQueries(db, scenario.Clock);
        var snapshot = scenario.State.Get("service")!;
        var disabled = snapshot with { Id = "disabled", Enabled = false, Health = HealthStatus.Unknown };
        var overview = await queries.OverviewAsync([snapshot, disabled], CancellationToken.None);
        Assert.Equal(HealthStatus.Up, overview.Health);
        Assert.Equal(1, overview.Counts.Enabled);
        Assert.Equal(1, overview.Counts.Disabled);
        Assert.Equal(0, overview.Counts.Unknown);
        var unknown = snapshot with { Id = "other", Health = HealthStatus.Unknown };
        Assert.Equal(HealthStatus.Unknown, (await queries.OverviewAsync([snapshot, unknown], CancellationToken.None)).Health);
        Assert.Equal(HealthStatus.Down, (await queries.OverviewAsync([snapshot with { Health = HealthStatus.Down }, unknown], CancellationToken.None)).Health);
        var empty = await queries.OverviewAsync([], CancellationToken.None);
        Assert.Equal(HealthStatus.Unknown, empty.Health);
        Assert.Empty(empty.Services);
    }
}
