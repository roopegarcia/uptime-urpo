namespace UptimeUrpo.Api.Monitoring;

public enum FailureCategory
{
    UnexpectedStatus,
    Dns,
    Connection,
    Tls,
    Timeout,
    BlockedDestination,
    InvalidResponse,
    Network
}

public sealed record CheckResult(
    DateTimeOffset CheckedAtUtc,
    double DurationMs,
    int? StatusCode,
    bool Success,
    FailureCategory? FailureCategory);
