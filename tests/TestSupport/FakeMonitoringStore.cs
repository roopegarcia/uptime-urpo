using System.Collections.Concurrent;
using UptimeUrpo.Api.Monitoring;
using UptimeUrpo.Api.Persistence;

namespace UptimeUrpo.TestSupport;

// Tests only. The application has no in-memory persistence fallback.
public sealed class FakeMonitoringStore : IMonitoringStore
{
    public bool FailWrites { get; set; }
    public bool FailInitialization { get; set; }
    public bool SchemaReady { get; set; } = true;
    public IReadOnlyList<PersistedSnapshot> Snapshots { get; set; } = [];
    public ConcurrentQueue<MonitoringObservation> Saved { get; } = new();

    public Task<IReadOnlyList<PersistedSnapshot>> SynchronizeAsync(CancellationToken token)
    {
        if (FailInitialization) throw new InvalidOperationException("private storage detail");
        return Task.FromResult(Snapshots);
    }

    public Task SaveAsync(TargetOptions target, MonitoringObservation observation, CancellationToken token)
    {
        if (FailWrites) throw new InvalidOperationException("private storage detail");
        Saved.Enqueue(observation);
        return Task.CompletedTask;
    }

    public Task<int> DeleteOldChecksAsync(DateTimeOffset cutoffUtc, int batchSize, CancellationToken token) => Task.FromResult(0);
    public Task<bool> IsSchemaReadyAsync(CancellationToken token) => Task.FromResult(SchemaReady);
}
