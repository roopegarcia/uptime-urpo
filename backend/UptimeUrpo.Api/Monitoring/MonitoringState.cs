using Microsoft.Extensions.Options;
using UptimeUrpo.Api.Contracts;
using UptimeUrpo.Api.Persistence;

namespace UptimeUrpo.Api.Monitoring;

public enum HealthStatus { Unknown, Up, Down, Degraded }

public sealed record MonitoringObservation(string ServiceId, CheckResult Result, DateTimeOffset CompletedAtUtc,
    long CompletedTimestamp, int ConsecutiveFailures);

public sealed class MonitoringState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries;
    private readonly TimeProvider _time;

    public MonitoringState(IOptions<MonitoringOptions> options, TimeProvider time)
    {
        _time = time;
        _entries = options.Value.GetTargets().ToDictionary(target => target.Id, target => new Entry(target));
    }

    internal void Record(string id, CheckResult result) => Apply(Prepare(id, result));

    public MonitoringObservation Prepare(string id, CheckResult result)
    {
        lock (_gate)
        {
            var entry = _entries[id];
            var failures = IsStale(entry) ? 0 : entry.Failures;
            failures = result.Success ? 0 : Math.Min(failures + 1, entry.Target.FailureThreshold);
            return new MonitoringObservation(id, result, _time.GetUtcNow(), _time.GetTimestamp(), failures);
        }
    }

    public void Apply(MonitoringObservation observation)
    {
        lock (_gate)
        {
            var entry = _entries[observation.ServiceId];
            if (!entry.Target.Enabled) return;
            entry.Failures = observation.ConsecutiveFailures;
            entry.LastCheck = observation.Result;
            entry.CompletedAtUtc = observation.CompletedAtUtc;
            entry.CompletedTimestamp = observation.CompletedTimestamp;
        }
    }

    public void Restore(IReadOnlyList<PersistedSnapshot> snapshots)
    {
        lock (_gate)
        {
            foreach (var snapshot in snapshots)
            {
                if (!_entries.TryGetValue(snapshot.ServiceId, out var entry)) continue;
                entry.LastCheck = snapshot.Result;
                entry.CompletedAtUtc = snapshot.CompletedAtUtc;
                entry.CompletedTimestamp = null;
                entry.Failures = 0;
                if (snapshot.Result is null || snapshot.CompletedAtUtc is null) continue;
                var age = _time.GetUtcNow() - snapshot.CompletedAtUtc.Value;
                // UTC bridges process lifetimes; future/old observations restart as UNKNOWN.
                if (age < TimeSpan.Zero || age > TimeSpan.FromSeconds(2 * entry.Target.IntervalSeconds)) continue;
                entry.CompletedTimestamp = _time.GetTimestamp() - (long)(age.TotalSeconds * _time.TimestampFrequency);
                entry.Failures = Math.Min(snapshot.ConsecutiveFailures, entry.Target.FailureThreshold);
            }
        }
    }

    public ServiceResponse? Get(string id)
    {
        lock (_gate)
            return _entries.TryGetValue(id, out var entry) ? Snapshot(entry) : null;
    }

    public IReadOnlyList<ServiceResponse> GetAll()
    {
        lock (_gate)
            return _entries.Values.Select(Snapshot).ToArray();
    }

    private bool IsStale(Entry entry) => entry.CompletedTimestamp is null
        || _time.GetElapsedTime(entry.CompletedTimestamp.Value) > TimeSpan.FromSeconds(2 * entry.Target.IntervalSeconds);

    private ServiceResponse Snapshot(Entry entry)
    {
        var stale = IsStale(entry);
        var health = HealthStatus.Unknown;
        if (entry.Target.Enabled && !stale && entry.LastCheck is { } check)
        {
            health = check.Success
                ? check.DurationMs > entry.Target.SlowResponseThresholdMs ? HealthStatus.Degraded : HealthStatus.Up
                : entry.Failures >= entry.Target.FailureThreshold ? HealthStatus.Down : HealthStatus.Degraded;
        }

        return new ServiceResponse(entry.Target.Id, entry.Target.Name, entry.Target.Enabled,
            health, stale ? 0 : entry.Failures, entry.CompletedAtUtc, entry.LastCheck);
    }

    private sealed class Entry(TargetOptions target)
    {
        public TargetOptions Target { get; } = target;
        public CheckResult? LastCheck { get; set; }
        public int Failures { get; set; }
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public long? CompletedTimestamp { get; set; }
    }
}
