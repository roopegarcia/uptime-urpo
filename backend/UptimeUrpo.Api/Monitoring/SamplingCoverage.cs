namespace UptimeUrpo.Api.Monitoring;

internal sealed record SamplingPeriod(long Id, string ServiceId, DateTimeOffset StartUtc,
    DateTimeOffset? EndUtc, bool Enabled, int IntervalSeconds);

internal static class SamplingCoverage
{
    public static long ExpectedSlots(SamplingPeriod period, DateTimeOffset from, DateTimeOffset to)
    {
        if (!period.Enabled) return 0;
        var start = from > period.StartUtc ? from : period.StartUtc;
        var end = period.EndUtc is { } ended && ended < to ? ended : to;
        if (start >= end) return 0;
        var interval = TimeSpan.FromSeconds(period.IntervalSeconds).Ticks;
        var firstSlot = (start - period.StartUtc).Ticks / interval;
        var endTicks = (end - period.StartUtc).Ticks;
        var exclusiveLastSlot = endTicks / interval + (endTicks % interval == 0 ? 0 : 1);
        return exclusiveLastSlot - firstSlot;
    }

    public static double EnabledSeconds(SamplingPeriod period, DateTimeOffset from, DateTimeOffset to)
    {
        if (!period.Enabled) return 0;
        var start = from > period.StartUtc ? from : period.StartUtc;
        var end = period.EndUtc is { } ended && ended < to ? ended : to;
        return Math.Max(0, (end - start).TotalSeconds);
    }
}
