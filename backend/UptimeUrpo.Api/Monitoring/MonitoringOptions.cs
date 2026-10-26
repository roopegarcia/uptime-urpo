namespace UptimeUrpo.Api.Monitoring;

public sealed class MonitoringOptions
{
    public int MaxConcurrency { get; set; } = 3;
    public List<TargetOptions> Targets { get; set; } = [];

    // Keep M1 operator configuration working; never silently merge both shapes.
    public TargetOptions? Target { get; set; }

    public IReadOnlyList<TargetOptions> GetTargets() => Target is null ? Targets : [Target];

    public bool IsValid()
    {
        if (MaxConcurrency is < 1 or > 8 || Targets is null || (Target is not null && Targets.Count > 0))
            return false;

        var targets = GetTargets();
        return targets.Count <= 20
            && targets.All(target => target is not null && target.IsValid())
            && targets.Select(target => target.Id).Distinct(StringComparer.Ordinal).Count() == targets.Count;
    }
}
