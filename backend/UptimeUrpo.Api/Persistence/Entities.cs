using UptimeUrpo.Api.Monitoring;

namespace UptimeUrpo.Api.Persistence;

public sealed class MonitoredService
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Retired { get; set; }
    public int IntervalSeconds { get; set; }
    public int TimeoutSeconds { get; set; }
    public int ExpectedStatusCode { get; set; }
    public int FailureThreshold { get; set; }
    public int SlowResponseThresholdMs { get; set; }
    public DateTimeOffset RegisteredAtUtc { get; set; }

    // A durable current snapshot, updated atomically with the append-only history row.
    // Keeping it separate lets retention remove old raw rows without losing current state.
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? FirstFailureAtUtc { get; set; }
    public DateTimeOffset? LastCheckedAtUtc { get; set; }
    public DateTimeOffset? LastCompletedAtUtc { get; set; }
    public double? LastDurationMs { get; set; }
    public int? LastStatusCode { get; set; }
    public bool? LastSuccess { get; set; }
    public FailureCategory? LastFailureCategory { get; set; }
}

public sealed class MonitoringPeriod
{
    public long Id { get; set; }
    public string ServiceId { get; set; } = "";
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public string Url { get; set; } = "";
    public bool Enabled { get; set; }
    public int IntervalSeconds { get; set; }
    public int TimeoutSeconds { get; set; }
    public int ExpectedStatusCode { get; set; }
    public int FailureThreshold { get; set; }
    public int SlowResponseThresholdMs { get; set; }
}

public sealed class CheckRecord
{
    public long Id { get; set; }
    public string ServiceId { get; set; } = "";
    public long MonitoringPeriodId { get; set; }
    public DateTimeOffset CheckedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
    public double DurationMs { get; set; }
    public int? StatusCode { get; set; }
    public bool Success { get; set; }
    public FailureCategory? FailureCategory { get; set; }
}

public enum IncidentResolution { Recovered, ConfigurationChanged }

public sealed class IncidentRecord
{
    public long Id { get; set; }
    public string ServiceId { get; set; } = "";
    public DateTimeOffset FirstFailureAtUtc { get; set; }
    public DateTimeOffset ConfirmedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public IncidentResolution? Resolution { get; set; }
    public bool HasMonitoringGaps { get; set; }
}
