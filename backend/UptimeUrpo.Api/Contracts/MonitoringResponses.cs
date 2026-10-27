using UptimeUrpo.Api.Monitoring;
using UptimeUrpo.Api.Persistence;

namespace UptimeUrpo.Api.Contracts;

public sealed record AvailabilityWindow(string Window, DateTimeOffset FromUtc, DateTimeOffset ToUtc,
    long CompletedChecks, long SuccessfulChecks, long FailedChecks, double? ObservedAvailabilityPercent,
    long ExpectedOpportunities, long ObservedOpportunities, long MissingOpportunities, double? CoveragePercent);

public sealed record IncidentResponse(long Id, DateTimeOffset FirstFailureAtUtc, DateTimeOffset ConfirmedAtUtc,
    DateTimeOffset? EndedAtUtc, IncidentResolution? Resolution, bool HasMonitoringGaps, double DurationSeconds);

public sealed record ServiceStatisticsResponse(string ServiceId, DateTimeOffset AsOfUtc,
    IReadOnlyList<AvailabilityWindow> Windows, IncidentResponse? CurrentIncident);

public sealed record HistoryPoint(DateTimeOffset FromUtc, DateTimeOffset ToUtc, string State,
    double EnabledSeconds, long CompletedChecks, long SuccessfulChecks, long FailedChecks,
    long HttpResponses, double? AverageResponseMs, double? MinimumResponseMs, double? MaximumResponseMs);

public sealed record HistoryResponse(string ServiceId, DateTimeOffset FromUtc, DateTimeOffset ToUtc,
    int BucketMinutes, IReadOnlyList<HistoryPoint> Points);

public sealed record IncidentPage(string ServiceId, DateTimeOffset AsOfUtc,
    IReadOnlyList<IncidentResponse> Items, long? NextBeforeId);

public sealed record HealthCounts(int Enabled, int Disabled, int Up, int Down, int Degraded, int Unknown);
public sealed record OverviewService(ServiceResponse Service, IReadOnlyList<AvailabilityWindow> Windows,
    IncidentResponse? CurrentIncident);
public sealed record OverviewResponse(DateTimeOffset AsOfUtc, HealthStatus Health, HealthCounts Counts,
    IReadOnlyList<OverviewService> Services);
