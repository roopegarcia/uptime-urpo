using Microsoft.EntityFrameworkCore;
using UptimeUrpo.Api.Monitoring;
using UptimeUrpo.Api.Persistence;

namespace UptimeUrpo.IntegrationTests;

[Collection("PostgreSQL")]
[Trait("Category", "Database")]
public sealed class IncidentTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ThresholdOpensOneIncidentAndSlowSuccessRecoversIt()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        await scenario.FailThreeAsync();
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        await scenario.SaveAsync(503);
        await using (var verify = scenario.Database.Context())
        {
            var incident = await verify.Incidents.SingleAsync();
            Assert.Equal(MonitoringScenario.Start, incident.FirstFailureAtUtc);
            Assert.Equal(MonitoringScenario.Start.AddSeconds(120), incident.ConfirmedAtUtc);
            Assert.Null(incident.EndedAtUtc);
        }
        scenario.Clock.Advance(TimeSpan.FromSeconds(30));
        await using (var read = scenario.Database.Context())
        {
            var current = (await new MonitoringQueries(read, scenario.Clock).StatisticsAsync("service", CancellationToken.None)).CurrentIncident;
            Assert.NotNull(current);
            Assert.Equal(210, current.DurationSeconds);
            Assert.False(current.HasMonitoringGaps);
        }
        await scenario.SaveAsync(duration: 3000);
        Assert.Equal(HealthStatus.Degraded, scenario.State.Get("service")!.Health);
        await using var final = scenario.Database.Context();
        var ended = await final.Incidents.SingleAsync();
        Assert.Equal(IncidentResolution.Recovered, ended.Resolution);
        Assert.Equal(MonitoringScenario.Start.AddSeconds(210), ended.EndedAtUtc);
        Assert.Null((await final.Services.SingleAsync()).FirstFailureAtUtc);
    }

    [Fact]
    public async Task PendingAndOpenIncidentsSurviveRestartAndFlagMonitoringGaps()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        await scenario.SaveAsync(503);
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        await scenario.SaveAsync(503);
        await scenario.SynchronizeAsync();
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        await scenario.SaveAsync(503);
        scenario.Clock.Advance(TimeSpan.FromSeconds(180));
        await using (var read = scenario.Database.Context())
            Assert.True((await new MonitoringQueries(read, scenario.Clock).StatisticsAsync("service", CancellationToken.None)).CurrentIncident!.HasMonitoringGaps);
        await scenario.SynchronizeAsync();
        Assert.Equal(HealthStatus.Unknown, scenario.State.Get("service")!.Health);
        await scenario.SaveAsync(503);
        await using (var verify = scenario.Database.Context())
        {
            var incident = await verify.Incidents.SingleAsync();
            Assert.Null(incident.EndedAtUtc);
            Assert.True(incident.HasMonitoringGaps);
            Assert.Equal(MonitoringScenario.Start, incident.FirstFailureAtUtc);
        }
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        await scenario.SaveAsync();
        await using var final = scenario.Database.Context();
        Assert.Equal(IncidentResolution.Recovered, (await final.Incidents.SingleAsync()).Resolution);
    }

    [Fact]
    public async Task GapBeforeConfirmationRestartsThePendingFailureTime()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        await scenario.SaveAsync(503);
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        await scenario.SaveAsync(503);
        scenario.Clock.Advance(TimeSpan.FromSeconds(180));
        var restartedAt = scenario.Clock.GetUtcNow();
        await scenario.FailThreeAsync();
        await using var db = scenario.Database.Context();
        Assert.Equal(restartedAt, (await db.Incidents.SingleAsync()).FirstFailureAtUtc);
    }

    [Fact]
    public async Task DisablingDoesNotClaimRecoveryAndChangedUrlHasADistinctResolution()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        await scenario.FailThreeAsync();
        scenario.Target.Enabled = false;
        await scenario.SynchronizeAsync();
        await using (var verify = scenario.Database.Context())
        {
            var incident = await verify.Incidents.SingleAsync();
            Assert.Null(incident.EndedAtUtc);
            Assert.True(incident.HasMonitoringGaps);
        }
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        scenario.Target.Enabled = true;
        await scenario.SynchronizeAsync();
        await scenario.SaveAsync();
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        await scenario.FailThreeAsync();
        scenario.Target.Url = "https://changed.example/";
        await scenario.SynchronizeAsync();
        await using var final = scenario.Database.Context();
        var incidents = await final.Incidents.OrderBy(incident => incident.Id).ToListAsync();
        Assert.Equal(2, incidents.Count);
        Assert.Equal(IncidentResolution.Recovered, incidents[0].Resolution);
        Assert.Equal(IncidentResolution.ConfigurationChanged, incidents[1].Resolution);
        Assert.True(incidents[1].HasMonitoringGaps);
    }

    [Fact]
    public async Task InvalidHistoryInsertRollsBackTheIncidentAndSnapshotToo()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        await scenario.SaveAsync(503);
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        await scenario.SaveAsync(503);
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        await Assert.ThrowsAsync<DbUpdateException>(() => scenario.SaveAsync(503, duration: -1));
        await using (var verify = scenario.Database.Context())
        {
            Assert.Equal(2, await verify.Checks.CountAsync());
            Assert.Empty(await verify.Incidents.ToListAsync());
            Assert.Equal(2, (await verify.Services.SingleAsync()).ConsecutiveFailures);
        }
        await scenario.SaveAsync(503);
        await using var duplicate = scenario.Database.Context();
        duplicate.Incidents.Add(new IncidentRecord
        {
            ServiceId = "service",
            FirstFailureAtUtc = scenario.Clock.GetUtcNow(),
            ConfirmedAtUtc = scenario.Clock.GetUtcNow()
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Fact]
    public async Task RetentionDeletesOnlyClosedOlderIncidentsInBoundedBatches()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        var cutoff = scenario.Clock.GetUtcNow().AddDays(-365);
        IncidentRecord Closed(DateTimeOffset ended) => new()
        {
            ServiceId = "service",
            FirstFailureAtUtc = ended.AddHours(-1),
            ConfirmedAtUtc = ended.AddMinutes(-58),
            EndedAtUtc = ended,
            Resolution = IncidentResolution.Recovered
        };
        await using var db = scenario.Database.Context();
        db.Incidents.AddRange(Closed(cutoff.AddDays(-1)), Closed(cutoff.AddDays(-2)), Closed(cutoff), new IncidentRecord
        {
            ServiceId = "service",
            FirstFailureAtUtc = cutoff.AddYears(-1),
            ConfirmedAtUtc = cutoff.AddYears(-1).AddMinutes(2)
        });
        await db.SaveChangesAsync();
        var store = scenario.Store(db);
        Assert.Equal(1, await store.DeleteClosedIncidentsAsync(cutoff, 1, CancellationToken.None));
        Assert.Equal(1, await store.DeleteClosedIncidentsAsync(cutoff, 1, CancellationToken.None));
        Assert.Equal(0, await store.DeleteClosedIncidentsAsync(cutoff, 1, CancellationToken.None));
        Assert.Equal(2, await db.Incidents.CountAsync());
        Assert.Equal(1, await store.DeleteClosedIncidentsAsync(cutoff.AddSeconds(1), 10, CancellationToken.None));
        Assert.Null((await db.Incidents.SingleAsync()).EndedAtUtc);
    }

    [Fact]
    public async Task CursorPagesStayStableWhenANewerIncidentIsInserted()
    {
        var scenario = await MonitoringScenario.CreateAsync(fixture);
        for (var index = 0; index < 3; index++)
        {
            await scenario.FailThreeAsync();
            await scenario.SaveAsync();
            scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        }
        await using var db = scenario.Database.Context();
        var queries = new MonitoringQueries(db, scenario.Clock);
        var page = await queries.IncidentsAsync("service", null, 2, CancellationToken.None);
        Assert.Equal(2, page.Items.Count);
        Assert.NotNull(page.NextBeforeId);
        await scenario.FailThreeAsync();
        var next = await queries.IncidentsAsync("service", page.NextBeforeId, 2, CancellationToken.None);
        Assert.Single(next.Items);
        Assert.Null(next.NextBeforeId);
        Assert.True(next.Items[0].Id < page.Items[^1].Id);
    }
}
