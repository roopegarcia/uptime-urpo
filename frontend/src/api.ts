import type { History, IncidentPage, Overview, WindowSize } from './types';

export class ApiError extends Error {
  constructor(public status: number) {
    super(
      status === 429
        ? 'Too many requests. Wait a minute, then try again.'
        : status === 404
          ? 'This service is no longer configured.'
          : status === 503
            ? 'Monitoring data is temporarily unavailable.'
            : 'Could not load monitoring data. Check the API connection and try again.',
    );
  }
}
export async function getJson<T>(path: string, signal: AbortSignal): Promise<T> {
  const response = await fetch(path, {
    signal,
    headers: { Accept: 'application/json' },
    cache: 'no-store',
  });
  if (!response.ok) throw new ApiError(response.status);
  if (!response.headers.get('content-type')?.includes('application/json')) throw new ApiError(502);
  return response.json() as Promise<T>;
}
export const getOverview = (signal: AbortSignal) => getJson<Overview>('/api/overview', signal);
export function historyAt(id: string, window: WindowSize, asOf: string, signal: AbortSignal) {
  const hours = window === '24h' ? 24 : window === '7d' ? 168 : 720;
  // Normalize both ends to milliseconds: mixing .NET sub-millisecond precision with JS
  // would accidentally exceed the 30-day maximum or create an extra tiny bucket.
  const to = new Date(asOf).toISOString();
  const query = new URLSearchParams({
    from: new Date(Date.parse(to) - hours * 3_600_000).toISOString(),
    to,
  });
  return getJson<History>(`/api/services/${encodeURIComponent(id)}/history?${query}`, signal);
}
export const getIncidents = (id: string, signal: AbortSignal, before?: number) =>
  getJson<IncidentPage>(
    `/api/services/${encodeURIComponent(id)}/incidents?limit=10${before ? `&beforeId=${before}` : ''}`,
    signal,
  );
