using UptimeUrpo.Api.Monitoring;

namespace UptimeUrpo.Api.Contracts;

public sealed record HealthResponse(string Status);

// Deliberately excludes the target URL and network/exception details.
public sealed record ServiceResponse(string Id, string Name, bool Enabled, HealthStatus Health,
    int ConsecutiveFailures, DateTimeOffset? LastCompletedAtUtc, CheckResult? LastCheck);
