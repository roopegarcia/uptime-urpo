using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace UptimeUrpo.IntegrationTests;

[Collection("PostgreSQL")]
[Trait("Category", "Database")]
public sealed class UpgradeTests(PostgresFixture fixture)
{
    [Fact]
    public async Task UpgradeFromM3PreservesSnapshotAndSeedsOnlyTheLatestKnownFailure()
    {
        var database = await fixture.CreateDatabaseAsync(migrate: false);
        await using var db = database.MigrationContext();
        var initial = db.Database.GetMigrations().First();
        await db.GetService<IMigrator>().MigrateAsync(initial);
        var knownFailure = MonitoringScenario.Start.AddMinutes(-1);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO monitored_services
              ("Id", "Name", "Url", "Enabled", "Retired", "IntervalSeconds", "TimeoutSeconds", "ExpectedStatusCode",
               "FailureThreshold", "SlowResponseThresholdMs", "RegisteredAtUtc", "ConsecutiveFailures",
               "LastCheckedAtUtc", "LastCompletedAtUtc", "LastDurationMs", "LastStatusCode", "LastSuccess", "LastFailureCategory")
            VALUES ('legacy', 'Legacy service', 'https://example.com/', true, false, 60, 10, 200, 3, 2000,
                    {knownFailure}, 2, {knownFailure}, {knownFailure}, 100, 503, false, 'UnexpectedStatus')
            """);
        await db.Database.MigrateAsync();
        var service = await db.Services.SingleAsync();
        Assert.Equal(2, service.ConsecutiveFailures);
        Assert.Equal(knownFailure, service.FirstFailureAtUtc);
        Assert.False(service.LastSuccess);
        Assert.Empty(await db.Incidents.ToListAsync());
    }
}
