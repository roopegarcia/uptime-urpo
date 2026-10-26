using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using UptimeUrpo.Api.Monitoring;
using UptimeUrpo.Api.Persistence;

namespace UptimeUrpo.IntegrationTests;

[Collection("PostgreSQL")]
[Trait("Category", "Database")]
public sealed class DatabaseTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static TargetOptions Target() => new() { Id = "database-test", Enabled = true };
    private static MonitoringOptions OptionsFor(params TargetOptions[] targets) => new() { Targets = targets.ToList() };
    private static MonitoringStore Store(MonitoringDbContext db, MonitoringOptions options, FakeTimeProvider clock) =>
        new(db, Options.Create(options), clock);
    private static MonitoringObservation Observation(FakeTimeProvider clock, bool success = true, int failures = 0,
        double duration = 50) => new("database-test", new CheckResult(clock.GetUtcNow(), duration, success ? 200 : 503,
            success, success ? null : FailureCategory.UnexpectedStatus), clock.GetUtcNow(), clock.GetTimestamp(), failures);

    [Fact]
    public async Task MigrationsApplyToEmptyDatabaseAndRuntimeCannotChangeSchema()
    {
        var database = await fixture.CreateDatabaseAsync(migrate: false);
        await using (var migration = database.MigrationContext())
        {
            Assert.Single(await migration.Database.GetPendingMigrationsAsync());
            await migration.Database.MigrateAsync();
            await migration.Database.MigrateAsync();
            Assert.Empty(await migration.Database.GetPendingMigrationsAsync());
            Assert.False(migration.Database.HasPendingModelChanges());
        }
        await using var runtime = database.Context();
        var store = Store(runtime, OptionsFor(Target()), new FakeTimeProvider(Now));
        Assert.True(await store.IsSchemaReadyAsync(CancellationToken.None));
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            runtime.Database.ExecuteSqlRawAsync("CREATE TABLE must_not_exist (id integer)"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task ResultsPersistAndHistoryUsesUtcRangeOrderingAndLimit()
    {
        var database = await fixture.CreateDatabaseAsync();
        var target = Target();
        var options = OptionsFor(target);
        var clock = new FakeTimeProvider(Now);
        await using (var db = database.Context())
        {
            var store = Store(db, options, clock);
            await store.SynchronizeAsync(CancellationToken.None);
            await store.SaveAsync(target, Observation(clock), CancellationToken.None);
            clock.Advance(TimeSpan.FromMinutes(1));
            await store.SaveAsync(target, Observation(clock, success: false, failures: 1), CancellationToken.None);
        }
        await using var fresh = database.Context();
        var reader = Store(fresh, options, clock);
        var rows = await reader.ReadHistoryAsync(target.Id, Now, Now.AddMinutes(2), 1, CancellationToken.None);
        Assert.Single(rows);
        Assert.False(rows[0].Success);
        Assert.Equal(TimeSpan.Zero, rows[0].CheckedAtUtc.Offset);
        Assert.Equal(2, await fresh.Checks.CountAsync());
        Assert.Single(await fresh.Periods.ToListAsync());
        var endExclusive = await reader.ReadHistoryAsync(target.Id, Now, Now.AddMinutes(1), 10, CancellationToken.None);
        Assert.Single(endExclusive);
        Assert.True(endExclusive[0].Success);
    }

    [Fact]
    public async Task SnapshotAndHistoryRollbackTogetherWhenAnInsertFails()
    {
        var database = await fixture.CreateDatabaseAsync();
        var target = Target();
        var options = OptionsFor(target);
        var clock = new FakeTimeProvider(Now);
        await using (var db = database.Context())
        {
            var store = Store(db, options, clock);
            await store.SynchronizeAsync(CancellationToken.None);
            await store.SaveAsync(target, Observation(clock), CancellationToken.None);
            clock.Advance(TimeSpan.FromMinutes(1));
            await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveAsync(target,
                Observation(clock, success: false, failures: 1, duration: -1), CancellationToken.None));
        }
        await using var verify = database.Context();
        Assert.Equal(1, await verify.Checks.CountAsync());
        var service = await verify.Services.SingleAsync();
        Assert.True(service.LastSuccess);
        Assert.Equal(0, service.ConsecutiveFailures);
        Assert.Equal(Now, service.LastCompletedAtUtc);
    }

    [Fact]
    public async Task ConfigurationPeriodsPreserveDisabledAndRetiredTimeWithoutRewritingHistory()
    {
        var database = await fixture.CreateDatabaseAsync();
        var clock = new FakeTimeProvider(Now);
        var target = Target();
        async Task<IReadOnlyList<PersistedSnapshot>> Sync(params TargetOptions[] targets)
        {
            await using var db = database.Context();
            return await Store(db, OptionsFor(targets), clock).SynchronizeAsync(CancellationToken.None);
        }
        await Sync(target);
        await using (var db = database.Context())
            await Store(db, OptionsFor(target), clock).SaveAsync(target, Observation(clock), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));
        target.Name = "Renamed";
        Assert.NotNull(Assert.Single(await Sync(target)).Result); // Display-only change keeps identity/state.
        target.IntervalSeconds = 90;
        Assert.Null(Assert.Single(await Sync(target)).Result);
        clock.Advance(TimeSpan.FromMinutes(1));
        target.Enabled = false;
        await Sync(target);
        clock.Advance(TimeSpan.FromMinutes(1));
        await Sync();
        await using (var verify = database.Context())
        {
            Assert.True((await verify.Services.SingleAsync()).Retired);
            Assert.Equal(3, await verify.Periods.CountAsync());
            Assert.Empty(await verify.Periods.Where(period => period.EndedAtUtc == null).ToListAsync());
            Assert.Equal(1, await verify.Checks.CountAsync());
            Assert.Equal(60, (await verify.Periods.OrderBy(period => period.Id).FirstAsync()).IntervalSeconds);
        }
        clock.Advance(TimeSpan.FromMinutes(1));
        target.Enabled = true;
        await Sync(target);
        await using var after = database.Context();
        Assert.Equal(4, await after.Periods.CountAsync());
        Assert.Single(await after.Periods.Where(period => period.EndedAtUtc == null).ToListAsync());
    }

    [Fact]
    public async Task RetentionIsStrictlyOlderThanCutoffAndDeletesOnlyOneBoundedBatch()
    {
        var database = await fixture.CreateDatabaseAsync();
        var target = Target();
        var clock = new FakeTimeProvider(Now.AddDays(-91));
        await using var db = database.Context();
        var store = Store(db, OptionsFor(target), clock);
        await store.SynchronizeAsync(CancellationToken.None);
        for (var index = 0; index < 3; index++) await store.SaveAsync(target, Observation(clock), CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(1));
        await store.SaveAsync(target, Observation(clock), CancellationToken.None); // Exactly at the cutoff.
        clock.Advance(TimeSpan.FromDays(90));
        Assert.Equal(2, await store.DeleteOldChecksAsync(Now.AddDays(-90), 2, CancellationToken.None));
        Assert.Equal(2, await db.Checks.CountAsync());
        Assert.Equal(1, await store.DeleteOldChecksAsync(Now.AddDays(-90), 2, CancellationToken.None));
        Assert.Equal(1, await db.Checks.CountAsync());
        Assert.Equal(0, await store.DeleteOldChecksAsync(Now.AddDays(-90), 2, CancellationToken.None));
        Assert.Equal(1, await store.DeleteOldChecksAsync(Now.AddDays(-90).AddSeconds(1), 2, CancellationToken.None));
        Assert.Empty(await db.Checks.ToListAsync());
        Assert.True((await db.Services.AsNoTracking().SingleAsync()).LastSuccess);
        Assert.Single(await db.Services.ToListAsync());
        Assert.Single(await db.Periods.ToListAsync());
    }

    [Fact]
    public async Task RestartRestoresTheLastObservationBeforeTheNextCheckCompletes()
    {
        var database = await fixture.CreateDatabaseAsync();
        var clock = new FakeTimeProvider(Now);
        DateTimeOffset timestamp;
        using (var first = new DatabaseApiFactory(database.Runtime, clock))
        using (var client = first.CreateClient())
        {
            await WaitUntilAsync(async () => (await ReadServiceAsync(client)).GetProperty("lastCheck").ValueKind != JsonValueKind.Null);
            timestamp = (await ReadServiceAsync(client)).GetProperty("lastCheck").GetProperty("checkedAtUtc").GetDateTimeOffset();
        }
        clock.Advance(TimeSpan.FromSeconds(30));
        using (var second = new DatabaseApiFactory(database.Runtime, clock, block: true))
        using (var client = second.CreateClient())
        {
            await second.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var snapshot = await ReadServiceAsync(client);
            Assert.Equal("UP", snapshot.GetProperty("health").GetString());
            Assert.Equal(timestamp, snapshot.GetProperty("lastCheck").GetProperty("checkedAtUtc").GetDateTimeOffset());
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        }
        await using var db = database.Context();
        Assert.Equal(1, await db.Checks.CountAsync()); // Cancelled second request did not invent an observation.
    }

    [Fact]
    public async Task RestartRestoresFailureStreakButAnOldSnapshotIsUnknown()
    {
        var database = await fixture.CreateDatabaseAsync();
        var target = Target();
        var options = OptionsFor(target);
        var clock = new FakeTimeProvider(Now);
        await using (var db = database.Context())
        {
            var store = Store(db, options, clock);
            await store.SynchronizeAsync(CancellationToken.None);
            await store.SaveAsync(target, Observation(clock, success: false, failures: 2), CancellationToken.None);
        }
        await using var restarted = database.Context();
        var saved = await Store(restarted, options, clock).SynchronizeAsync(CancellationToken.None);
        var state = new MonitoringState(Options.Create(options), clock);
        state.Restore(saved);
        Assert.Equal(2, state.Get(target.Id)!.ConsecutiveFailures);
        Assert.Equal(HealthStatus.Degraded, state.Get(target.Id)!.Health);
        Assert.Equal(3, state.Prepare(target.Id, Observation(clock, success: false).Result).ConsecutiveFailures);
        clock.Advance(TimeSpan.FromSeconds(121));
        var afterGap = new MonitoringState(Options.Create(options), clock);
        afterGap.Restore(saved);
        Assert.Equal(HealthStatus.Unknown, afterGap.Get(target.Id)!.Health);
        Assert.Equal(0, afterGap.Get(target.Id)!.ConsecutiveFailures);
        Assert.NotNull(afterGap.Get(target.Id)!.LastCheck);
    }

    [Fact]
    public async Task FailedDatabaseWriteDoesNotPublishAndReadinessRecoversAfterTheNextSavedResult()
    {
        var database = await fixture.CreateDatabaseAsync();
        var clock = new FakeTimeProvider(Now);
        using var factory = new DatabaseApiFactory(database.Runtime, clock);
        using var client = factory.CreateClient();
        await WaitUntilAsync(async () => (await ReadServiceAsync(client)).GetProperty("lastCheck").ValueKind != JsonValueKind.Null);
        var initial = (await ReadServiceAsync(client)).GetProperty("lastCheck").GetProperty("checkedAtUtc").GetDateTimeOffset();
        await WaitUntilAsync(() => Task.FromResult(factory.Clock.IntervalTimers >= 1));
        await database.ExecuteAdminAsync("REVOKE INSERT ON check_results FROM urpo_runtime");
        clock.Advance(TimeSpan.FromSeconds(60));
        await WaitUntilAsync(async () => (await client.GetAsync("/health/ready")).StatusCode == HttpStatusCode.ServiceUnavailable);
        Assert.Equal(initial, (await ReadServiceAsync(client)).GetProperty("lastCheck").GetProperty("checkedAtUtc").GetDateTimeOffset());
        await using (var verify = database.Context()) Assert.Equal(1, await verify.Checks.CountAsync());
        await database.ExecuteAdminAsync("GRANT INSERT ON check_results TO urpo_runtime");
        // The failed attempt has completed and schedules its next fixed delay.
        await WaitUntilAsync(() => Task.FromResult(factory.Clock.IntervalTimers >= 2));
        clock.Advance(TimeSpan.FromSeconds(60));
        await WaitUntilAsync(async () => (await ReadServiceAsync(client)).GetProperty("lastCheck").GetProperty("checkedAtUtc").GetDateTimeOffset() > initial);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        await using var final = database.Context();
        Assert.Equal(2, await final.Checks.CountAsync());
    }

    [Fact]
    public async Task UnreachableDatabaseKeepsLivenessAndPreventsTargetChecks()
    {
        var clock = new FakeTimeProvider(Now);
        using var factory = new DatabaseApiFactory(
            "Host=127.0.0.1;Port=1;Database=missing;Username=missing;Timeout=1;Pooling=false", clock);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("{\"status\":\"not_ready\"}", await ready.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.Handler.Calls);
    }

    private static async Task<JsonElement> ReadServiceAsync(HttpClient client)
    {
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/services/database-test"));
        return json.RootElement.Clone();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!await condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException("Database scenario did not reach its expected state.");
            await Task.Delay(5);
        }
    }

    private sealed class DatabaseApiFactory(string connection, FakeTimeProvider time, bool block = false) : WebApplicationFactory<Program>
    {
        public CountingClock Clock { get; } = new(time);
        public DatabaseHandler Handler { get; } = new(block);
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(service => service.ServiceType == typeof(IHostedService)
                    && service.ImplementationType == typeof(RetentionWorker)).ToArray()) services.Remove(descriptor);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.PostConfigure<DatabaseOptions>(options => options.ConnectionString = connection);
                services.PostConfigure<MonitoringOptions>(options =>
                {
                    options.Target = null;
                    options.Targets = [Target()];
                    options.MaxConcurrency = 3;
                });
                services.AddHttpClient(HttpChecker.ClientName).ConfigurePrimaryHttpMessageHandler(() => Handler);
            });
        }
    }

    private sealed class DatabaseHandler(bool block) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            Started.TrySetResult();
            if (block) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class CountingClock(FakeTimeProvider inner) : TimeProvider
    {
        private int _intervalTimers;
        public int IntervalTimers => Volatile.Read(ref _intervalTimers);
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
        public override long GetTimestamp() => inner.GetTimestamp();
        public override long TimestampFrequency => inner.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = inner.CreateTimer(callback, state, dueTime, period);
            if (dueTime == TimeSpan.FromSeconds(60)) Interlocked.Increment(ref _intervalTimers);
            return timer;
        }
    }
}
