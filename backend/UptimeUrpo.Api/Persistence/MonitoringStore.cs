using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UptimeUrpo.Api.Monitoring;

namespace UptimeUrpo.Api.Persistence;

public sealed class MonitoringStore(MonitoringDbContext db, IOptions<MonitoringOptions> options, TimeProvider time)
    : IMonitoringStore
{
    public async Task<IReadOnlyList<PersistedSnapshot>> SynchronizeAsync(CancellationToken token)
    {
        var targets = options.Value.GetTargets();
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var services = await db.Services.ToDictionaryAsync(service => service.Id, token);
        var periods = await db.Periods.Where(period => period.EndedAtUtc == null)
            .ToDictionaryAsync(period => period.ServiceId, token);

        foreach (var service in services.Values.Where(service => !targets.Any(target => target.Id == service.Id)))
        {
            service.Retired = true;
            service.Enabled = false;
            if (periods.TryGetValue(service.Id, out var period)) period.EndedAtUtc = now;
        }

        // Close changed periods before inserting replacements (the partial index permits one open period).
        var newPeriods = new List<MonitoringPeriod>();
        foreach (var target in targets)
        {
            if (!services.TryGetValue(target.Id, out var service))
            {
                service = new MonitoredService { Id = target.Id, RegisteredAtUtc = now };
                services.Add(service.Id, service);
                db.Services.Add(service);
            }
            var changed = service.Retired || !Matches(service, target);
            if (changed || !periods.ContainsKey(target.Id))
            {
                if (periods.TryGetValue(target.Id, out var period)) period.EndedAtUtc = now;
                ClearSnapshot(service);
                newPeriods.Add(NewPeriod(target, now));
            }
            CopyConfiguration(service, target);
        }
        await db.SaveChangesAsync(token);
        db.Periods.AddRange(newPeriods);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);

        return targets.Select(target => ToSnapshot(services[target.Id])).ToArray();
    }

    public async Task SaveAsync(TargetOptions target, MonitoringObservation observation, CancellationToken token)
    {
        if (observation.ServiceId != target.Id) throw new ArgumentException("Observation and target IDs must match.");
        var service = await db.Services.SingleAsync(service => service.Id == target.Id, token);
        if (service.Retired || !service.Enabled || !Matches(service, target))
            throw new InvalidOperationException("Persisted service configuration does not match this check.");
        var period = await db.Periods.SingleAsync(period => period.ServiceId == target.Id && period.EndedAtUtc == null, token);
        var result = observation.Result;
        db.Checks.Add(new CheckRecord
        {
            ServiceId = target.Id,
            MonitoringPeriodId = period.Id,
            CheckedAtUtc = result.CheckedAtUtc,
            CompletedAtUtc = observation.CompletedAtUtc,
            DurationMs = result.DurationMs,
            StatusCode = result.StatusCode,
            Success = result.Success,
            FailureCategory = result.FailureCategory
        });
        service.LastCheckedAtUtc = result.CheckedAtUtc;
        service.LastCompletedAtUtc = observation.CompletedAtUtc;
        service.LastDurationMs = result.DurationMs;
        service.LastStatusCode = result.StatusCode;
        service.LastSuccess = result.Success;
        service.LastFailureCategory = result.FailureCategory;
        service.ConsecutiveFailures = observation.ConsecutiveFailures;
        // EF wraps these related writes in one transaction. Publish to memory only after it succeeds.
        await db.SaveChangesAsync(token);
    }

    public async Task<IReadOnlyList<CheckRecord>> ReadHistoryAsync(string serviceId,
        DateTimeOffset startUtc, DateTimeOffset endUtc, int limit, CancellationToken token)
    {
        if (startUtc >= endUtc || limit is < 1 or > 1000)
            throw new ArgumentException("History requires an ordered range and a limit from 1 to 1000.");
        startUtc = startUtc.ToUniversalTime();
        endUtc = endUtc.ToUniversalTime();
        return await db.Checks.AsNoTracking()
            .Where(check => check.ServiceId == serviceId && check.CheckedAtUtc >= startUtc && check.CheckedAtUtc < endUtc)
            .OrderByDescending(check => check.CheckedAtUtc).ThenByDescending(check => check.Id)
            .Take(limit).ToListAsync(token);
    }

    public async Task<int> DeleteOldChecksAsync(DateTimeOffset cutoffUtc, int batchSize, CancellationToken token)
    {
        if (batchSize is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var ids = await db.Checks.Where(check => check.CompletedAtUtc < cutoffUtc)
            .OrderBy(check => check.CompletedAtUtc).ThenBy(check => check.Id)
            .Take(batchSize).Select(check => check.Id).ToArrayAsync(token);
        return await db.Checks.Where(check => ids.Contains(check.Id)).ExecuteDeleteAsync(token);
    }

    public async Task<bool> IsSchemaReadyAsync(CancellationToken token)
    {
        if ((await db.Database.GetPendingMigrationsAsync(token)).Any()) return false;
        _ = await db.Services.AsNoTracking().Select(service => service.Id).FirstOrDefaultAsync(token);
        return true;
    }

    private static bool Matches(MonitoredService service, TargetOptions target) =>
        service.Url == new Uri(target.Url).AbsoluteUri && service.Enabled == target.Enabled
        && service.IntervalSeconds == target.IntervalSeconds && service.TimeoutSeconds == target.TimeoutSeconds
        && service.ExpectedStatusCode == target.ExpectedStatusCode && service.FailureThreshold == target.FailureThreshold
        && service.SlowResponseThresholdMs == target.SlowResponseThresholdMs;

    private static void CopyConfiguration(MonitoredService service, TargetOptions target)
    {
        service.Name = target.Name;
        service.Url = new Uri(target.Url).AbsoluteUri;
        service.Enabled = target.Enabled;
        service.Retired = false;
        service.IntervalSeconds = target.IntervalSeconds;
        service.TimeoutSeconds = target.TimeoutSeconds;
        service.ExpectedStatusCode = target.ExpectedStatusCode;
        service.FailureThreshold = target.FailureThreshold;
        service.SlowResponseThresholdMs = target.SlowResponseThresholdMs;
    }

    private static MonitoringPeriod NewPeriod(TargetOptions target, DateTimeOffset now) => new()
    {
        ServiceId = target.Id,
        StartedAtUtc = now,
        Url = new Uri(target.Url).AbsoluteUri,
        Enabled = target.Enabled,
        IntervalSeconds = target.IntervalSeconds,
        TimeoutSeconds = target.TimeoutSeconds,
        ExpectedStatusCode = target.ExpectedStatusCode,
        FailureThreshold = target.FailureThreshold,
        SlowResponseThresholdMs = target.SlowResponseThresholdMs
    };

    private static void ClearSnapshot(MonitoredService service)
    {
        service.LastCheckedAtUtc = null;
        service.LastCompletedAtUtc = null;
        service.LastDurationMs = null;
        service.LastStatusCode = null;
        service.LastSuccess = null;
        service.LastFailureCategory = null;
        service.ConsecutiveFailures = 0;
    }

    private static PersistedSnapshot ToSnapshot(MonitoredService service) => new(service.Id,
        service.LastSuccess is null ? null : new CheckResult(service.LastCheckedAtUtc!.Value,
            service.LastDurationMs!.Value, service.LastStatusCode, service.LastSuccess.Value, service.LastFailureCategory),
        service.LastCompletedAtUtc, service.ConsecutiveFailures);
}
