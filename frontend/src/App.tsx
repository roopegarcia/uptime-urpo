import { useEffect, useRef, useState } from 'react';
import { ApiError, getIncidents, getOverview, historyAt } from './api';
import { ago, dateTime, duration, incidentSeconds, milliseconds, percent } from './format';
import { useNow, useResource, useTheme } from './hooks';
import { ResponseChart } from './ResponseChart';
import type {
  AvailabilityWindow,
  Health,
  Incident,
  IncidentPage,
  Overview,
  OverviewService,
  WindowSize,
} from './types';

const healthLabels: Record<Health, string> = {
  UP: 'Operational',
  DOWN: 'Down',
  DEGRADED: 'Degraded',
  UNKNOWN: 'Unknown',
};
function Status({
  health,
  disabled = false,
  old = false,
}: {
  health: Health;
  disabled?: boolean;
  old?: boolean;
}) {
  return (
    <span className={`status ${disabled || old ? 'UNKNOWN' : health}`}>
      <i />
      {disabled ? 'Disabled' : `${old ? 'Last known: ' : ''}${healthLabels[health]}`}
    </span>
  );
}
function Notice({ children, error = false }: { children: React.ReactNode; error?: boolean }) {
  return (
    <div className={`notice ${error ? 'notice-error' : ''}`} role={error ? 'alert' : 'status'}>
      {children}
    </div>
  );
}
function Windows({
  value,
  onChange,
}: {
  value: WindowSize;
  onChange: (value: WindowSize) => void;
}) {
  return (
    <div className="window-control" role="group" aria-label="Time window">
      {(['24h', '7d', '30d'] as const).map((window) => (
        <button key={window} aria-pressed={value === window} onClick={() => onChange(window)}>
          {window === '24h' ? '24 hours' : window === '7d' ? '7 days' : '30 days'}
        </button>
      ))}
    </div>
  );
}
function Metric({ label, value, note }: { label: string; value: string; note: string }) {
  return (
    <div className="metric">
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{note}</small>
    </div>
  );
}
function currentWindow(
  service: OverviewService,
  window: WindowSize,
): AvailabilityWindow | undefined {
  return service.windows.find((item) => item.window === window);
}
function useServiceRoute() {
  const read = () => {
    const match = /^#\/services\/([^/]+)$/.exec(location.hash);
    try {
      return match ? decodeURIComponent(match[1]) : null;
    } catch {
      return null;
    }
  };
  const [id, setId] = useState(read);
  useEffect(() => {
    const changed = () => setId(read());
    window.addEventListener('hashchange', changed);
    return () => window.removeEventListener('hashchange', changed);
  }, []);
  return id;
}
export default function App() {
  const overview = useResource(getOverview, 'overview');
  const now = useNow();
  const { theme, toggle } = useTheme();
  const [window, setWindow] = useState<WindowSize>('24h');
  const id = useServiceRoute();
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    heading.current?.focus();
    document.title = id ? 'Service details · Uptime-Urpo' : 'Overview · Uptime-Urpo';
  }, [id]);
  const data = overview.data;
  const old = !!overview.error || (!!overview.receivedAt && now - overview.receivedAt > 90_000);
  const selected = data?.services.find((item) => item.service.id === id);
  return (
    <>
      <a
        className="skip-link"
        href="#main"
        onClick={(event) => {
          event.preventDefault();
          document.getElementById('main')?.focus();
        }}
      >
        Skip to content
      </a>
      <header className="site-header">
        <div className="header-inner">
          <a className="brand" href="#/" aria-label="Uptime-Urpo overview">
            <svg viewBox="0 0 32 32" aria-hidden="true">
              <rect width="32" height="32" rx="9" />
              <path d="M5 17h6l3-8 5 15 3-7h5" />
            </svg>
            <span>
              Uptime<span className="brand-light">-Urpo</span>
            </span>
          </a>
          <span className="private-label">Private monitor</span>
          <button
            className="theme-button"
            onClick={toggle}
            aria-label={`Switch to ${theme === 'light' ? 'dark' : 'light'} theme`}
          >
            <span aria-hidden="true">{theme === 'light' ? '◐' : '☀'}</span>
            <span>{theme === 'light' ? 'Dark' : 'Light'}</span>
          </button>
        </div>
      </header>
      <main id="main" tabIndex={-1}>
        <div className="page-top">
          <div>
            <nav aria-label="Breadcrumb">
              <a href="#/">Workspace</a>
              <span aria-hidden="true">/</span>
              <span>{id ? 'Service details' : 'Overview'}</span>
            </nav>
            <h1 ref={heading} tabIndex={-1}>
              {id ? (selected?.service.name ?? 'Service details') : 'Service overview'}
            </h1>
            <p className="subtitle">
              {id
                ? 'The latest signals, with the history behind them.'
                : 'A little peace of mind. One check at a time.'}
            </p>
          </div>
          <div className="refresh-area">
            <span>
              <i className={old ? 'sync-dot old' : 'sync-dot'} />
              {old ? 'Data may be out of date' : 'Refreshes every 30s'}
            </span>
            <button className="button" onClick={overview.refresh} disabled={overview.loading}>
              ↻ <span>{overview.loading ? 'Refreshing…' : 'Refresh'}</span>
            </button>
          </div>
        </div>
        {overview.error && (
          <Notice error>
            {overview.error}{' '}
            {data && 'Showing the last successful snapshot; its health may have changed.'}
          </Notice>
        )}
        {old && !overview.error && (
          <Notice>
            Updates paused while this tab was inactive. Showing the last known snapshot.
          </Notice>
        )}
        {!data && overview.loading && (
          <div className="loading-state" role="status">
            <span className="loading-bar" />
            Loading your monitoring workspace…
          </div>
        )}
        {!data && !overview.loading && (
          <div className="empty panel">
            <h2>Monitoring data couldn’t be loaded</h2>
            <p>
              The dashboard needs the local API and its database. Use Refresh after the connection
              is restored.
            </p>
          </div>
        )}
        {data &&
          (id ? (
            selected ? (
              <ServiceDetails
                key={id}
                item={selected}
                window={window}
                setWindow={setWindow}
                asOf={data.asOfUtc}
                now={now}
                old={old}
              />
            ) : (
              <section className="empty panel">
                <h2>Service not found</h2>
                <p>This service may have been removed from private configuration.</p>
                <a href="#/">Back to overview →</a>
              </section>
            )
          ) : (
            <OverviewPanel data={data} window={window} setWindow={setWindow} now={now} old={old} />
          ))}
        {data && (
          <p className="snapshot">
            Snapshot: <time dateTime={data.asOfUtc}>{dateTime(data.asOfUtc)}</time>. Times are shown
            in your browser’s time zone.
          </p>
        )}
      </main>
      <footer>
        <span>
          Uptime-Urpo <span className="footer-dot">·</span> A watchful little worker.
        </span>
        <span>Observed samples, not an exact outage timeline.</span>
      </footer>
    </>
  );
}
function OverviewPanel({
  data,
  window,
  setWindow,
  now,
  old,
}: {
  data: Overview;
  window: WindowSize;
  setWindow: (w: WindowSize) => void;
  now: number;
  old: boolean;
}) {
  const [search, setSearch] = useState('');
  const [attention, setAttention] = useState(false);
  const services = data.services.filter(
    (item) =>
      item.service.name.toLowerCase().includes(search.toLowerCase()) &&
      (!attention || (item.service.enabled && item.service.health !== 'UP')),
  );
  const count = data.services.length;
  const healthy = data.counts.up;
  const title = old
    ? 'Waiting for fresh signals'
    : !data.counts.enabled
      ? 'Ready when you are'
      : data.health === 'UP'
        ? 'All systems operational'
        : data.health === 'DOWN'
          ? 'Some services need attention'
          : data.health === 'DEGRADED'
            ? 'Performance needs a closer look'
            : 'Waiting for completed checks';
  return (
    <>
      <section
        className={`health-banner ${old ? 'UNKNOWN' : data.health}`}
        aria-label="Overall health"
      >
        <div className="health-emblem" aria-hidden="true">
          {!old && data.health === 'UP' ? '✓' : !old && data.health === 'DOWN' ? '!' : '∿'}
        </div>
        <div>
          <span className="eyebrow">{old ? 'Last known status' : 'Current status'}</span>
          <h2>{title}</h2>
          <p>
            {data.counts.enabled
              ? `${healthy} of ${data.counts.enabled} enabled services operational`
              : 'Add approved targets in private configuration to begin monitoring.'}
            {data.counts.disabled ? ` · ${data.counts.disabled} disabled` : ''}
          </p>
        </div>
        <span className="banner-count">
          <strong>{String(count).padStart(2, '0')}</strong>services
        </span>
      </section>
      <div className="status-summary" aria-label="Service health counts">
        {(['UP', 'DEGRADED', 'DOWN', 'UNKNOWN'] as Health[]).map((health) => (
          <div key={health}>
            <Status health={health} old={old} />
            <strong>
              {data.counts[health.toLowerCase() as 'up' | 'degraded' | 'down' | 'unknown']}
            </strong>
          </div>
        ))}
      </div>
      <section className="panel services-panel" aria-labelledby="services-heading">
        <div className="panel-heading">
          <div>
            <h2 id="services-heading">
              Your services <span className="count-tag">{count}</span>
            </h2>
            <p>Health now. Observed availability over time.</p>
          </div>
          <Windows value={window} onChange={setWindow} />
        </div>
        {count > 0 && (
          <div className="table-tools">
            <label className="search">
              <span aria-hidden="true">⌕</span>
              <input
                type="search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Find a service…"
                aria-label="Find a service"
              />
            </label>
            <label className="attention-filter">
              <input
                type="checkbox"
                checked={attention}
                onChange={(e) => setAttention(e.target.checked)}
              />
              Needs attention
            </label>
          </div>
        )}
        {!count ? (
          <div className="empty">
            <h3>No services configured yet</h3>
            <p>
              Targets are managed in private server configuration. There is no URL submission form.
            </p>
          </div>
        ) : !services.length ? (
          <div className="empty">
            <h3>No matching services</h3>
            <p>Try another name or clear the attention filter.</p>
            <button
              className="button"
              onClick={() => {
                setSearch('');
                setAttention(false);
              }}
            >
              Clear filters
            </button>
          </div>
        ) : (
          <div className="service-list">
            <div className="service-row service-labels" aria-hidden="true">
              <span>Service</span>
              <span>Status</span>
              <span>Response</span>
              <span>Availability / coverage</span>
              <span>Last check</span>
              <span />
            </div>
            {services.map((item) => {
              const { service } = item,
                stats = currentWindow(item, window);
              return (
                <a
                  className="service-row"
                  href={`#/services/${encodeURIComponent(service.id)}`}
                  key={service.id}
                  aria-label={`View ${service.name}, ${service.enabled ? `${old ? 'last known ' : ''}${healthLabels[service.health]}` : 'disabled'}`}
                >
                  <span className="service-name">
                    <span
                      className={`service-avatar ${service.enabled && !old ? service.health : 'UNKNOWN'}`}
                      aria-hidden="true"
                    >
                      {service.name.slice(0, 1).toUpperCase()}
                    </span>
                    <span>
                      <strong>{service.name}</strong>
                      <small>
                        {item.currentIncident
                          ? 'Open incident'
                          : service.enabled
                            ? 'Monitoring enabled'
                            : 'Monitoring paused'}
                      </small>
                    </span>
                  </span>
                  <Status health={service.health} disabled={!service.enabled} old={old} />
                  <span className="row-metric">
                    <small className="mobile-label">Response to headers</small>
                    <strong>
                      {milliseconds(
                        service.lastCheck?.statusCode != null ? service.lastCheck.durationMs : null,
                      )}
                    </strong>
                  </span>
                  <span className="row-metric availability">
                    <strong>{percent(stats?.observedAvailabilityPercent)}</strong>
                    <small>{percent(stats?.coveragePercent)} coverage</small>
                  </span>
                  <span className="last-check">{ago(service.lastCompletedAtUtc, now)}</span>
                  <span className="row-arrow" aria-hidden="true">
                    ↗
                  </span>
                </a>
              );
            })}
          </div>
        )}
        <div className="panel-note">
          Availability = successful completed checks ÷ completed checks. Missing observations are
          excluded; coverage shows how much was observed.
        </div>
      </section>
      <div className="info-note">
        <span aria-hidden="true">ⓘ</span>
        <p>
          <strong>A sample is a signal.</strong> Slow successful responses count as available.
          Unknown means disabled, no checks, or a result older than two monitoring intervals.
        </p>
      </div>
    </>
  );
}
function ServiceDetails({
  item,
  window,
  setWindow,
  asOf,
  now,
  old,
}: {
  item: OverviewService;
  window: WindowSize;
  setWindow: (w: WindowSize) => void;
  asOf: string;
  now: number;
  old: boolean;
}) {
  const { service, currentIncident } = item;
  const stats = currentWindow(item, window);
  // Read bounds come from the server snapshot, so browser clock skew cannot request future data.
  const detail = useResource(
    (signal) => historyAt(service.id, window, asOf, signal),
    `${service.id}-${window}`,
    60_000,
  );
  const incidents = useResource((signal) => getIncidents(service.id, signal), service.id, 60_000);
  return (
    <>
      <a className="back-link" href="#/">
        ← All services
      </a>
      <div className="detail-status">
        <Status health={service.health} disabled={!service.enabled} old={old} />
        <span>
          {ago(service.lastCompletedAtUtc, now)}
          {service.lastCheck?.statusCode != null
            ? ` · HTTP ${service.lastCheck.statusCode}`
            : service.lastCheck?.failureCategory
              ? ` · ${service.lastCheck.failureCategory.replaceAll('_', ' ')}`
              : ''}
        </span>
      </div>
      {currentIncident && (
        <section className="incident-banner" aria-label="Current incident">
          <div>
            <span className="eyebrow">Open incident</span>
            <h2>
              {duration(incidentSeconds(currentIncident.durationSeconds, null, asOf, now))} since
              first failure
            </h2>
            <p>
              First failure {dateTime(currentIncident.firstFailureAtUtc)} · Confirmed{' '}
              {dateTime(currentIncident.confirmedAtUtc)}
            </p>
            <p>
              {currentIncident.hasMonitoringGaps
                ? 'Includes monitoring gaps; continuous downtime is not proven.'
                : 'Elapsed incident time is based on sampled failures.'}
            </p>
          </div>
          <span className="incident-open">Unresolved</span>
        </section>
      )}
      <div className="detail-toolbar">
        <h2>Service performance</h2>
        <Windows value={window} onChange={setWindow} />
      </div>
      <section className="metrics-grid" aria-label="Window statistics">
        <Metric
          label="Observed availability"
          value={percent(stats?.observedAvailabilityPercent)}
          note={`${(stats?.successfulChecks ?? 0).toLocaleString()} successful / ${(stats?.completedChecks ?? 0).toLocaleString()} completed`}
        />
        <Metric
          label="Monitoring coverage"
          value={percent(stats?.coveragePercent)}
          note={`${(stats?.missingOpportunities ?? 0).toLocaleString()} missing opportunities`}
        />
        <Metric
          label="Latest response"
          value={milliseconds(
            service.lastCheck?.statusCode != null ? service.lastCheck.durationMs : null,
          )}
          note="GET → response headers; no body"
        />
        <Metric
          label="Failed checks"
          value={(stats?.failedChecks ?? 0).toLocaleString()}
          note="Includes failures before DOWN"
        />
      </section>
      {detail.error && (
        <Notice error>
          {detail.error} {detail.data && 'The chart below is from an earlier refresh.'}
        </Notice>
      )}
      <section className="panel chart-panel" aria-labelledby="response-heading">
        <div className="panel-heading">
          <div>
            <h2 id="response-heading">Response time</h2>
            <p>Time until HTTP response headers arrive. Gaps stay gaps.</p>
          </div>
          <button className="button" disabled={detail.loading} onClick={detail.refresh}>
            {detail.loading ? 'Loading…' : 'Refresh history'}
          </button>
        </div>
        {!detail.data ? (
          <div className="empty" role="status">
            {detail.loading
              ? 'Loading response history…'
              : 'History is unavailable. Try Refresh history.'}
          </div>
        ) : (
          <ResponseChart key={window} history={detail.data} />
        )}
      </section>
      {incidents.error && (
        <Notice error>
          {incidents.error} {incidents.data && 'Showing the previously loaded incident list.'}
          <button className="button" onClick={incidents.refresh} disabled={incidents.loading}>
            Retry incidents
          </button>
        </Notice>
      )}
      {incidents.data ? (
        <IncidentList first={incidents.data} now={now} />
      ) : (
        <div className="panel empty" role="status">
          {incidents.loading ? 'Loading incidents…' : 'Incident history is unavailable.'}
        </div>
      )}
      <div className="info-note">
        <span aria-hidden="true">ⓘ</span>
        <p>
          Coverage counts observed monitoring opportunities while enabled. Missing checks do not
          prove availability or recovery. These samples cannot establish exact outage duration.
        </p>
      </div>
    </>
  );
}
function IncidentList({ first, now }: { first: IncidentPage; now: number }) {
  const [items, setItems] = useState(first.items);
  const [cursor, setCursor] = useState(first.nextBeforeId);
  const [loading, setLoading] = useState(false);
  const [paged, setPaged] = useState(false);
  useEffect(() => {
    // Refresh current incidents without discarding older pages the reader loaded.
    setItems((previous) =>
      paged
        ? [
            ...first.items,
            ...previous.filter((item) => item.id < (first.items.at(-1)?.id ?? Infinity)),
          ]
        : first.items,
    );
    if (!paged) setCursor(first.nextBeforeId);
  }, [first, paged]);
  const [error, setError] = useState<string>();
  const controller = useRef<AbortController | null>(null);
  useEffect(
    () => () => {
      controller.current?.abort();
      controller.current = null;
    },
    [],
  );
  async function more() {
    if (cursor == null || loading) return;
    const current = new AbortController();
    controller.current = current;
    setLoading(true);
    setError(undefined);
    const timeout = setTimeout(() => current.abort(), 10_000);
    try {
      const page = await getIncidents(first.serviceId, current.signal, cursor);
      if (!current.signal.aborted) {
        setItems((previous) => [
          ...previous,
          ...page.items.filter((next) => !previous.some((item) => item.id === next.id)),
        ]);
        setCursor(page.nextBeforeId);
        setPaged(true);
      }
    } catch (e) {
      if (!current.signal.aborted || controller.current === current)
        setError(e instanceof ApiError ? e.message : 'Could not load older incidents. Try again.');
    } finally {
      clearTimeout(timeout);
      if (controller.current === current) setLoading(false);
    }
  }
  return (
    <section className="panel incident-panel" aria-labelledby="incident-heading">
      <div className="panel-heading">
        <div>
          <h2 id="incident-heading">Incident history</h2>
          <p>Confirmed failures and their recoveries.</p>
        </div>
        <span className="count-tag">{items.length} loaded</span>
      </div>
      {!items.length ? (
        <div className="empty">
          <span className="empty-check" aria-hidden="true">
            ✓
          </span>
          <h3>No incidents recorded</h3>
          <p>A failed sample opens an incident only after the configured DOWN threshold.</p>
        </div>
      ) : (
        <ol className="incident-list">
          {items.map((incident) => (
            <IncidentRow key={incident.id} incident={incident} asOf={first.asOfUtc} now={now} />
          ))}
        </ol>
      )}
      {error && <Notice error>{error}</Notice>}
      {cursor != null && (
        <div className="load-more">
          <button className="button" disabled={loading} onClick={() => void more()}>
            {loading ? 'Loading older incidents…' : 'Load older incidents'}
          </button>
        </div>
      )}
    </section>
  );
}
function IncidentRow({ incident, asOf, now }: { incident: Incident; asOf: string; now: number }) {
  return (
    <li>
      <span className={`incident-mark ${incident.endedAtUtc ? 'closed' : ''}`} aria-hidden="true" />
      <div className="incident-copy">
        <strong>
          {incident.resolution === 'recovered'
            ? 'Recovered'
            : incident.resolution === 'configuration_changed'
              ? 'Configuration changed'
              : 'Open incident'}{' '}
          <span className="incident-id">#{incident.id}</span>
        </strong>
        <p>First failure {dateTime(incident.firstFailureAtUtc)}</p>
        <p>
          Confirmed {dateTime(incident.confirmedAtUtc)}
          {incident.endedAtUtc && ` · Ended ${dateTime(incident.endedAtUtc)}`}
        </p>
        {incident.hasMonitoringGaps && (
          <small className="gap-note">Monitoring gaps included · continuity uncertain</small>
        )}
      </div>
      <span className="incident-duration">
        {duration(incidentSeconds(incident.durationSeconds, incident.endedAtUtc, asOf, now))}
        <small>elapsed</small>
      </span>
    </li>
  );
}
