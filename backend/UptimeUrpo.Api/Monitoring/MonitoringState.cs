using Microsoft.Extensions.Options;
using UptimeUrpo.Api.Contracts;

namespace UptimeUrpo.Api.Monitoring;

public enum HealthStatus { Unknown, Up, Down, Degraded }

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

    public void Record(string id, CheckResult result)
    {
        lock (_gate)
        {
            var entry = _entries[id];
            if (!entry.Target.Enabled) return;
            if (IsStale(entry)) entry.Failures = 0;
            entry.Failures = result.Success ? 0 : Math.Min(entry.Failures + 1, entry.Target.FailureThreshold);
            entry.LastCheck = result;
            entry.CompletedAtUtc = _time.GetUtcNow();
            entry.CompletedTimestamp = _time.GetTimestamp();
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
