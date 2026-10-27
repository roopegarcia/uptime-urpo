using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using UptimeUrpo.Api.Monitoring;
using UptimeUrpo.Api.Persistence;

namespace UptimeUrpo.IntegrationTests;

internal sealed class MonitoringScenario
{
    public static readonly DateTimeOffset Start = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    public TestDatabase Database { get; }
    public FakeTimeProvider Clock { get; } = new(Start);
    public TargetOptions Target { get; } = new() { Id = "service", Name = "Test service", Enabled = true };
    public MonitoringOptions Options { get; } = new();
    public MonitoringState State { get; private set; }

    private MonitoringScenario(TestDatabase database)
    {
        Database = database;
        Options.Targets = [Target];
        State = new MonitoringState(Microsoft.Extensions.Options.Options.Create(Options), Clock);
    }

    public static async Task<MonitoringScenario> CreateAsync(PostgresFixture fixture, bool enabled = true)
    {
        var scenario = new MonitoringScenario(await fixture.CreateDatabaseAsync());
        scenario.Target.Enabled = enabled;
        await scenario.SynchronizeAsync();
        return scenario;
    }

    public MonitoringStore Store(MonitoringDbContext db) => new(db, Microsoft.Extensions.Options.Options.Create(Options), Clock);

    public async Task SynchronizeAsync()
    {
        await using var db = Database.Context();
        var snapshots = await Store(db).SynchronizeAsync(CancellationToken.None);
        State = new MonitoringState(Microsoft.Extensions.Options.Options.Create(Options), Clock);
        State.Restore(snapshots);
    }

    public async Task SaveAsync(int? status = 200, double duration = 100)
    {
        var success = status == Target.ExpectedStatusCode;
        var result = new CheckResult(Clock.GetUtcNow(), duration, status, success,
            success ? null : status is null ? FailureCategory.Dns : FailureCategory.UnexpectedStatus);
        var observation = State.Prepare(Target.Id, result);
        await using var db = Database.Context();
        await Store(db).SaveAsync(Target, observation, CancellationToken.None);
        State.Apply(observation);
    }

    public async Task FailThreeAsync()
    {
        for (var index = 0; index < 3; index++)
        {
            if (index > 0) Clock.Advance(TimeSpan.FromSeconds(60));
            await SaveAsync(503);
        }
    }
}
