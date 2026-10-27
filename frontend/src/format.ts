export const percent = (value: number | null | undefined) =>
  value == null ? '—' : `${value.toFixed(2)}%`;
export const milliseconds = (value: number | null | undefined) =>
  value == null ? '—' : `${Math.round(value).toLocaleString()} ms`;
export function duration(seconds: number) {
  const minutes = Math.floor(Math.max(0, seconds) / 60);
  if (!minutes) return '< 1m';
  const days = Math.floor(minutes / 1440),
    hours = Math.floor((minutes % 1440) / 60);
  return [days ? `${days}d` : '', hours ? `${hours}h` : '', `${minutes % 60}m`]
    .filter(Boolean)
    .join(' ');
}
export function ago(utc: string | null, now: number) {
  if (!utc) return 'No completed check';
  const seconds = Math.max(0, (now - Date.parse(utc)) / 1000);
  return seconds < 60 ? 'Just now' : `${duration(seconds)} ago`;
}
export function dateTime(utc: string) {
  return new Date(utc).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    timeZoneName: 'short',
  });
}
export function incidentSeconds(seconds: number, ended: string | null, asOf: string, now: number) {
  return seconds + (ended ? 0 : Math.max(0, now - Date.parse(asOf)) / 1000);
}
