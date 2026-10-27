import { useId, useState } from 'react';
import { dateTime, milliseconds } from './format';
import type { History, HistoryPoint } from './types';

// Separate segments prevent SVG from drawing across missing HTTP observations.
export function responseSegments(points: HistoryPoint[]) {
  const segments: { point: HistoryPoint; index: number }[][] = [];
  let current: { point: HistoryPoint; index: number }[] = [];
  points.forEach((point, index) => {
    if (point.averageResponseMs == null) {
      if (current.length) segments.push(current);
      current = [];
    } else current.push({ point, index });
  });
  if (current.length) segments.push(current);
  return segments;
}
export function ResponseChart({ history }: { history: History }) {
  const pattern = useId().replaceAll(':', '');
  const [selected, setSelected] = useState<number | null>(null);
  const { points } = history;
  if (!points.length) return <p className="empty">No history in this window.</p>;
  const index = Math.min(selected ?? points.length - 1, points.length - 1);
  const point = points[index];
  const start = Date.parse(history.fromUtc),
    span = Date.parse(history.toUtc) - start;
  const x = (utc: string) => ((Date.parse(utc) - start) / span) * 1000;
  const middle = (p: HistoryPoint) => (x(p.fromUtc) + x(p.toUtc)) / 2;
  const maximum = Math.max(100, ...points.map((p) => p.averageResponseMs ?? 0)) * 1.15;
  const y = (value: number) => 180 - (value / maximum) * 160;
  const observed = points.some((p) => p.httpResponses > 0);
  function inspect(clientX: number, svg: SVGSVGElement) {
    const rect = svg.getBoundingClientRect();
    const utc = start + Math.max(0, Math.min(1, (clientX - rect.left) / rect.width)) * span;
    const next = points.findIndex((p) => Date.parse(p.toUtc) > utc);
    setSelected(next < 0 ? points.length - 1 : next);
  }
  return (
    <>
      <div className="chart-scale">
        <span>{observed ? milliseconds(maximum) : 'No response-time samples'}</span>
        <span>Average response · {history.bucketMinutes}-minute buckets</span>
      </div>
      <svg
        className="response-chart"
        viewBox="0 0 1000 210"
        preserveAspectRatio="none"
        aria-hidden="true"
        onPointerMove={(event) => {
          if (event.pointerType !== 'touch' || event.buttons)
            inspect(event.clientX, event.currentTarget);
        }}
        onPointerDown={(event) => inspect(event.clientX, event.currentTarget)}
      >
        <defs>
          <pattern id={pattern} width="8" height="8" patternUnits="userSpaceOnUse">
            <path d="M0 8L8 0" className="gap-stroke" />
          </pattern>
        </defs>
        {[20, 100, 180].map((value) => (
          <line key={value} x1="0" x2="1000" y1={value} y2={value} className="gridline" />
        ))}
        {points.map((p, i) => (
          <g key={p.fromUtc}>
            {p.state === 'missing' && (
              <rect
                x={x(p.fromUtc)}
                y="10"
                width={Math.max(0.3, x(p.toUtc) - x(p.fromUtc))}
                height="170"
                fill={`url(#${pattern})`}
              />
            )}
            <rect
              x={x(p.fromUtc)}
              y="197"
              width={Math.max(0.3, x(p.toUtc) - x(p.fromUtc))}
              height="8"
              className={`sample ${p.state === 'observed' ? (p.failedChecks ? 'failed' : 'success') : p.state}`}
            />
            {p.averageResponseMs != null && (i === index || points.length < 3) && (
              <circle cx={middle(p)} cy={y(p.averageResponseMs)} r="3" className="chart-dot" />
            )}
          </g>
        ))}
        {responseSegments(points).map((segment, i) =>
          segment.length > 1 ? (
            <polyline
              key={i}
              points={segment
                .map(({ point: p }) => `${middle(p)},${y(p.averageResponseMs!)}`)
                .join(' ')}
              className="response-line"
            />
          ) : (
            <circle
              key={i}
              cx={middle(segment[0].point)}
              cy={y(segment[0].point.averageResponseMs!)}
              r="2.5"
              className="chart-dot"
            />
          ),
        )}
        <line x1={middle(point)} x2={middle(point)} y1="10" y2="207" className="chart-cursor" />
      </svg>
      {!observed && (
        <p className="chart-no-data">
          No HTTP responses in this window. Missing observations have no response time.
        </p>
      )}
      <div className="chart-scale">
        <span>{dateTime(history.fromUtc)}</span>
        <span>{dateTime(history.toUtc)}</span>
      </div>
      <label className="chart-slider-label" htmlFor={`${pattern}-inspect`}>
        Inspect a bucket <span>Hover, touch, or use the arrow keys</span>
      </label>
      <input
        id={`${pattern}-inspect`}
        className="chart-slider"
        type="range"
        min="0"
        max={points.length - 1}
        value={index}
        aria-valuetext={`${dateTime(point.fromUtc)}. ${point.state.replaceAll('_', ' ')}. ${milliseconds(point.averageResponseMs)}.`}
        onChange={(event) => setSelected(Number(event.target.value))}
      />
      <div className="bucket-detail" aria-live="polite" aria-atomic="true">
        <span>
          <strong>{dateTime(point.fromUtc)}</strong>
          <small>to {dateTime(point.toUtc)}</small>
        </span>
        <span>
          <strong>{milliseconds(point.averageResponseMs)}</strong>
          <small>Average to headers</small>
        </span>
        <span>
          <strong>
            {point.completedChecks.toLocaleString()} checks · {point.failedChecks.toLocaleString()}{' '}
            failed
          </strong>
          <small>
            {point.state === 'missing'
              ? 'Missing observations'
              : point.state === 'not_monitored'
                ? 'Monitoring not enabled'
                : `${point.httpResponses.toLocaleString()} HTTP responses · min ${milliseconds(point.minimumResponseMs)} / max ${milliseconds(point.maximumResponseMs)}`}
          </small>
        </span>
      </div>
      <div className="legend">
        <span>
          <i className="success" />
          Successful checks
        </span>
        <span>
          <i className="failed" />
          Includes failed checks
        </span>
        <span>
          <i className="missing" />
          Missing observations
        </span>
        <span>
          <i className="not_monitored" />
          Not monitored
        </span>
      </div>
      <details className="data-disclosure">
        <summary>View chart data as a table</summary>
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Response history data">
          <table>
            <caption>
              All buckets, including missing data. Times use your browser time zone.
            </caption>
            <thead>
              <tr>
                <th scope="col">Bucket start</th>
                <th scope="col">State</th>
                <th scope="col">Checks</th>
                <th scope="col">Failed</th>
                <th scope="col">Average</th>
              </tr>
            </thead>
            <tbody>
              {points.map((p) => (
                <tr key={p.fromUtc}>
                  <th scope="row">{dateTime(p.fromUtc)}</th>
                  <td>{p.state.replaceAll('_', ' ')}</td>
                  <td>{p.completedChecks}</td>
                  <td>{p.failedChecks}</td>
                  <td>{milliseconds(p.averageResponseMs)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </>
  );
}
