// Synthetic data for tests and the isolated browser harness; never imported by the app.
import type {
  AvailabilityWindow,
  History,
  HistoryPoint,
  Incident,
  IncidentPage,
  Overview,
} from './types.ts';
export function fixtures(now = new Date().toISOString()) {
  const time = Date.parse(now);
  const at = (minutes: number) => new Date(time - minutes * 60_000).toISOString();
  const windows = (['24h', '7d', '30d'] as const).map(
    (window) =>
      ({
        window,
        fromUtc: at(1440),
        toUtc: now,
        completedChecks: 12,
        successfulChecks: 11,
        failedChecks: 1,
        observedAvailabilityPercent: 91.6667,
        expectedOpportunities: 20,
        observedOpportunities: 12,
        missingOpportunities: 8,
        coveragePercent: 60,
      }) satisfies AvailabilityWindow,
  );
  const incident: Incident = {
    id: 12,
    firstFailureAtUtc: at(20),
    confirmedAtUtc: at(18),
    endedAtUtc: null,
    resolution: null,
    hasMonitoringGaps: true,
    durationSeconds: 1200,
  };
  const overview: Overview = {
    asOfUtc: now,
    health: 'DOWN',
    counts: { enabled: 4, disabled: 1, up: 1, down: 1, degraded: 1, unknown: 1 },
    services: (['UP', 'DOWN', 'DEGRADED', 'UNKNOWN', 'UNKNOWN'] as const).map((health, index) => ({
      service: {
        id: `test-${index}`,
        name: ['Test website', 'Test API', 'Test shop', 'Test pending', 'Test disabled'][index],
        enabled: index !== 4,
        health,
        consecutiveFailures: index === 1 ? 3 : 0,
        lastCompletedAtUtc: index < 3 ? at(1) : null,
        lastCheck:
          index < 3
            ? {
                checkedAtUtc: at(1),
                durationMs: index === 2 ? 2500 : 200,
                statusCode: index === 1 ? 503 : 200,
                success: index !== 1,
                failureCategory: index === 1 ? 'unexpected_status' : null,
              }
            : null,
      },
      windows: windows.map((w) =>
        index > 2
          ? {
              ...w,
              completedChecks: 0,
              successfulChecks: 0,
              failedChecks: 0,
              observedAvailabilityPercent: null,
              coveragePercent: null,
            }
          : w,
      ),
      currentIncident: index === 1 ? incident : null,
    })),
  };
  const points: HistoryPoint[] = Array.from({ length: 12 }, (_, index) => {
    const missing = index === 4 || index === 5;
    const disabled = index === 0;
    const noResponse = index === 8;
    return {
      fromUtc: at(60 - index * 5),
      toUtc: at(55 - index * 5),
      state: disabled ? 'not_monitored' : missing ? 'missing' : 'observed',
      enabledSeconds: disabled ? 0 : 300,
      completedChecks: missing || disabled ? 0 : 5,
      successfulChecks: missing || disabled || noResponse ? 0 : 5,
      failedChecks: noResponse ? 5 : 0,
      httpResponses: missing || disabled || noResponse ? 0 : 5,
      averageResponseMs: missing || disabled || noResponse ? null : 100 + index * 22,
      minimumResponseMs: missing || disabled || noResponse ? null : 80,
      maximumResponseMs: missing || disabled || noResponse ? null : 400,
    };
  });
  const history: History = {
    serviceId: 'test-1',
    fromUtc: at(60),
    toUtc: now,
    bucketMinutes: 5,
    points,
  };
  const incidents: IncidentPage = {
    serviceId: 'test-1',
    asOfUtc: now,
    items: [incident],
    nextBeforeId: 12,
  };
  return {
    overview,
    history,
    incidents,
    older: {
      ...incidents,
      items: [
        {
          ...incident,
          id: 11,
          endedAtUtc: at(40),
          firstFailureAtUtc: at(50),
          confirmedAtUtc: at(48),
          resolution: 'recovered' as const,
          durationSeconds: 600,
        },
      ],
      nextBeforeId: null,
    },
  };
}
