using System.Data;
using Microsoft.EntityFrameworkCore;
using UptimeUrpo.Api.Contracts;
using UptimeUrpo.Api.Monitoring;

namespace UptimeUrpo.Api.Persistence;

// Read-side SQL aggregation stays separate from the small transactional write path.
public sealed class MonitoringQueries(MonitoringDbContext db, TimeProvider time)
{
    public async Task<ServiceStatisticsResponse> StatisticsAsync(string id, CancellationToken token)
    {
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var windows = await WindowsAsync([id], now, token);
        var current = await CurrentIncidentsAsync([id], now, token);
        return new ServiceStatisticsResponse(id, now, windows[id], current.GetValueOrDefault(id));
    }

    public async Task<OverviewResponse> OverviewAsync(IReadOnlyList<ServiceResponse> services, CancellationToken token)
    {
        var now = time.GetUtcNow();
        var ids = services.Select(service => service.Id).ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var windows = await WindowsAsync(ids, now, token);
        var incidents = await CurrentIncidentsAsync(ids, now, token);
        var enabled = services.Where(service => service.Enabled).ToArray();
        var counts = new HealthCounts(enabled.Length, services.Count - enabled.Length,
            enabled.Count(service => service.Health == HealthStatus.Up), enabled.Count(service => service.Health == HealthStatus.Down),
            enabled.Count(service => service.Health == HealthStatus.Degraded), enabled.Count(service => service.Health == HealthStatus.Unknown));
        var health = counts.Down > 0 ? HealthStatus.Down : counts.Degraded > 0 ? HealthStatus.Degraded
            : counts.Unknown > 0 || counts.Enabled == 0 ? HealthStatus.Unknown : HealthStatus.Up;
        return new OverviewResponse(now, health, counts, services.Select(service =>
            new OverviewService(service, windows[service.Id], incidents.GetValueOrDefault(service.Id))).ToArray());
    }

    public async Task<HistoryResponse> HistoryAsync(string id, DateTimeOffset from, DateTimeOffset to,
        int bucketMinutes, CancellationToken token)
    {
        var seconds = bucketMinutes * 60;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var periods = await PeriodsAsync([id], from, to, token);
        // SqlQuery binds every interpolation as a parameter. No query text comes from the client.
        var rows = await db.Database.SqlQuery<HistoryTotals>($"""
            SELECT floor(extract(epoch FROM (c."CheckedAtUtc" - {from})) / {seconds})::integer AS "BucketIndex",
                   count(*) AS "Checks", count(*) FILTER (WHERE c."Success") AS "Successful",
                   count(*) FILTER (WHERE c."StatusCode" IS NOT NULL) AS "Responses",
                   avg(c."DurationMs") FILTER (WHERE c."StatusCode" IS NOT NULL) AS "AverageMs",
                   min(c."DurationMs") FILTER (WHERE c."StatusCode" IS NOT NULL) AS "MinimumMs",
                   max(c."DurationMs") FILTER (WHERE c."StatusCode" IS NOT NULL) AS "MaximumMs"
            FROM check_results c
            WHERE c."ServiceId" = {id} AND c."CheckedAtUtc" >= {from} AND c."CheckedAtUtc" < {to}
            GROUP BY 1
            """).ToDictionaryAsync(row => row.BucketIndex, token);
        var points = new List<HistoryPoint>();
        var count = (int)Math.Ceiling((to - from).TotalSeconds / seconds);
        for (var index = 0; index < count; index++)
        {
            var start = from.AddSeconds((long)index * seconds);
            var end = start.AddSeconds(seconds) < to ? start.AddSeconds(seconds) : to;
            var enabledSeconds = periods.Sum(period => SamplingCoverage.EnabledSeconds(period, start, end));
            rows.TryGetValue(index, out var row);
            var checks = row?.Checks ?? 0;
            var successful = row?.Successful ?? 0;
            points.Add(new HistoryPoint(start, end, checks > 0 ? "observed" : enabledSeconds > 0 ? "missing" : "not_monitored",
                enabledSeconds, checks, successful, checks - successful, row?.Responses ?? 0,
                row?.AverageMs, row?.MinimumMs, row?.MaximumMs));
        }
        return new HistoryResponse(id, from, to, bucketMinutes, points);
    }

    public async Task<IncidentPage> IncidentsAsync(string id, long? beforeId, int limit, CancellationToken token)
    {
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var rows = await db.Incidents.AsNoTracking().Where(incident => incident.ServiceId == id
                && (beforeId == null || incident.Id < beforeId))
            .OrderByDescending(incident => incident.Id).Take(limit + 1).ToListAsync(token);
        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(service => service.Id == id, token);
        var items = rows.Take(limit).Select(incident => ToResponse(incident, service, now)).ToArray();
        return new IncidentPage(id, now, items, rows.Count > limit ? items[^1].Id : null);
    }

    private async Task<Dictionary<string, IReadOnlyList<AvailabilityWindow>>> WindowsAsync(
        string[] ids, DateTimeOffset now, CancellationToken token)
    {
        var result = ids.ToDictionary(id => id, _ => new List<AvailabilityWindow>());
        var periods = await PeriodsAsync(ids, now.AddDays(-30), now, token);
        foreach (var (label, days) in new[] { ("24h", 1), ("7d", 7), ("30d", 30) })
        {
            var from = now.AddDays(-days);
            var rows = await db.Database.SqlQuery<PeriodTotals>($"""
                SELECT c."ServiceId", c."MonitoringPeriodId" AS "PeriodId",
                       count(*) AS "Checks", count(*) FILTER (WHERE c."Success") AS "Successful",
                       count(DISTINCT floor(extract(epoch FROM (c."CheckedAtUtc" - p."StartedAtUtc")) / p."IntervalSeconds"))
                         FILTER (WHERE p."Enabled" AND c."CheckedAtUtc" >= p."StartedAtUtc"
                           AND (p."EndedAtUtc" IS NULL OR c."CheckedAtUtc" < p."EndedAtUtc")) AS "ObservedSlots"
                FROM check_results c JOIN monitoring_periods p ON p."Id" = c."MonitoringPeriodId"
                WHERE c."ServiceId" = ANY ({ids}) AND c."CheckedAtUtc" >= {from} AND c."CheckedAtUtc" < {now}
                GROUP BY c."ServiceId", c."MonitoringPeriodId"
                """).ToListAsync(token);
            foreach (var id in ids)
            {
                var totals = rows.Where(row => row.ServiceId == id).ToArray();
                var checks = totals.Sum(row => row.Checks);
                var successful = totals.Sum(row => row.Successful);
                var expected = periods.Where(period => period.ServiceId == id)
                    .Sum(period => SamplingCoverage.ExpectedSlots(period, from, now));
                var observed = totals.Sum(row => row.ObservedSlots);
                result[id].Add(new AvailabilityWindow(label, from, now, checks, successful, checks - successful,
                    checks == 0 ? null : 100d * successful / checks, expected, observed, expected - observed,
                    expected == 0 ? null : 100d * observed / expected));
            }
        }
        return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<AvailabilityWindow>)pair.Value);
    }

    private Task<List<SamplingPeriod>> PeriodsAsync(string[] ids, DateTimeOffset from, DateTimeOffset to, CancellationToken token) =>
        db.Periods.AsNoTracking().Where(period => ids.Contains(period.ServiceId) && period.StartedAtUtc < to
            && (period.EndedAtUtc == null || period.EndedAtUtc > from))
            .Select(period => new SamplingPeriod(period.Id, period.ServiceId, period.StartedAtUtc,
                period.EndedAtUtc, period.Enabled, period.IntervalSeconds)).ToListAsync(token);

    private async Task<Dictionary<string, IncidentResponse>> CurrentIncidentsAsync(string[] ids, DateTimeOffset now, CancellationToken token)
    {
        var incidents = await db.Incidents.AsNoTracking().Where(incident => ids.Contains(incident.ServiceId) && incident.EndedAtUtc == null)
            .ToListAsync(token);
        var services = await db.Services.AsNoTracking().Where(service => ids.Contains(service.Id)).ToDictionaryAsync(service => service.Id, token);
        return incidents.ToDictionary(incident => incident.ServiceId,
            incident => ToResponse(incident, services.GetValueOrDefault(incident.ServiceId), now));
    }

    private static IncidentResponse ToResponse(IncidentRecord incident, MonitoredService? service, DateTimeOffset now)
    {
        var liveGap = incident.EndedAtUtc is null && (service?.LastCompletedAtUtc is null
            || service.LastCompletedAtUtc > now || now - service.LastCompletedAtUtc > TimeSpan.FromSeconds(2 * service.IntervalSeconds));
        return new IncidentResponse(incident.Id, incident.FirstFailureAtUtc, incident.ConfirmedAtUtc, incident.EndedAtUtc,
            incident.Resolution, incident.HasMonitoringGaps || liveGap,
            Math.Max(0, ((incident.EndedAtUtc ?? now) - incident.FirstFailureAtUtc).TotalSeconds));
    }

    private sealed class PeriodTotals
    {
        public string ServiceId { get; set; } = "";
        public long PeriodId { get; set; }
        public long Checks { get; set; }
        public long Successful { get; set; }
        public long ObservedSlots { get; set; }
    }

    private sealed class HistoryTotals
    {
        public int BucketIndex { get; set; }
        public long Checks { get; set; }
        public long Successful { get; set; }
        public long Responses { get; set; }
        public double? AverageMs { get; set; }
        public double? MinimumMs { get; set; }
        public double? MaximumMs { get; set; }
    }
}
