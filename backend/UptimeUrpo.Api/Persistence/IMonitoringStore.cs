using UptimeUrpo.Api.Monitoring;

namespace UptimeUrpo.Api.Persistence;

public sealed record PersistedSnapshot(string ServiceId, CheckResult? Result,
    DateTimeOffset? CompletedAtUtc, int ConsecutiveFailures);

// One persistence boundary for the worker; not a generic repository abstraction.
public interface IMonitoringStore
{
    Task<IReadOnlyList<PersistedSnapshot>> SynchronizeAsync(CancellationToken token);
    Task SaveAsync(TargetOptions target, MonitoringObservation observation, CancellationToken token);
    Task<int> DeleteOldChecksAsync(DateTimeOffset cutoffUtc, int batchSize, CancellationToken token);
    Task<int> DeleteClosedIncidentsAsync(DateTimeOffset cutoffUtc, int batchSize, CancellationToken token);
    Task<bool> IsSchemaReadyAsync(CancellationToken token);
}
