// Explicit read contracts mirror backend/Contracts. TypeScript does not validate JSON at runtime.
export type Health = 'UP' | 'DOWN' | 'DEGRADED' | 'UNKNOWN';
export type WindowSize = '24h' | '7d' | '30d';
export interface CheckResult {
  checkedAtUtc: string;
  durationMs: number;
  statusCode: number | null;
  success: boolean;
  failureCategory: string | null;
}
export interface Service {
  id: string;
  name: string;
  enabled: boolean;
  health: Health;
  consecutiveFailures: number;
  lastCompletedAtUtc: string | null;
  lastCheck: CheckResult | null;
}
export interface AvailabilityWindow {
  window: WindowSize;
  fromUtc: string;
  toUtc: string;
  completedChecks: number;
  successfulChecks: number;
  failedChecks: number;
  observedAvailabilityPercent: number | null;
  expectedOpportunities: number;
  observedOpportunities: number;
  missingOpportunities: number;
  coveragePercent: number | null;
}
export interface Incident {
  id: number;
  firstFailureAtUtc: string;
  confirmedAtUtc: string;
  endedAtUtc: string | null;
  resolution: 'recovered' | 'configuration_changed' | null;
  hasMonitoringGaps: boolean;
  durationSeconds: number;
}
export interface OverviewService {
  service: Service;
  windows: AvailabilityWindow[];
  currentIncident: Incident | null;
}
export interface Overview {
  asOfUtc: string;
  health: Health;
  counts: {
    enabled: number;
    disabled: number;
    up: number;
    down: number;
    degraded: number;
    unknown: number;
  };
  services: OverviewService[];
}
export interface HistoryPoint {
  fromUtc: string;
  toUtc: string;
  state: 'observed' | 'missing' | 'not_monitored';
  enabledSeconds: number;
  completedChecks: number;
  successfulChecks: number;
  failedChecks: number;
  httpResponses: number;
  averageResponseMs: number | null;
  minimumResponseMs: number | null;
  maximumResponseMs: number | null;
}
export interface History {
  serviceId: string;
  fromUtc: string;
  toUtc: string;
  bucketMinutes: number;
  points: HistoryPoint[];
}
export interface IncidentPage {
  serviceId: string;
  asOfUtc: string;
  items: Incident[];
  nextBeforeId: number | null;
}
