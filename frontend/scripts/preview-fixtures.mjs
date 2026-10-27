// Isolated browser QA only. No PostgreSQL connection or target requests.
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { dirname, extname, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { fixtures } from '../src/fixtures.ts';
const dist = resolve(dirname(fileURLToPath(import.meta.url)), '../dist');
const counts = new Map();
const server = createServer(async (request, response) => {
  const url = new URL(request.url, 'http://127.0.0.1:5181');
  if (url.pathname.startsWith('/api/')) {
    const scenario =
      new URL(request.headers.referer ?? '/', url).searchParams.get('scenario') ?? 'signals';
    response.setHeader('content-type', 'application/json');
    response.setHeader('cache-control', 'no-store');
    const count = counts.get(scenario) ?? 0;
    if (url.pathname === '/api/overview') counts.set(scenario, count + 1);
    if (scenario === 'error' || (scenario === 'stale' && count > 0)) {
      response.writeHead(503);
      response.end(JSON.stringify({ title: 'Synthetic data unavailable' }));
      return;
    }
    const data = fixtures();
    if (scenario === 'empty') {
      data.overview.services = [];
      data.overview.counts = { enabled: 0, disabled: 0, up: 0, down: 0, degraded: 0, unknown: 0 };
      data.overview.health = 'UNKNOWN';
    }
    if (scenario === 'missing') {
      data.overview.services.forEach((item) => {
        item.currentIncident = null;
        item.service.health = 'UNKNOWN';
        item.service.lastCheck = null;
        item.service.lastCompletedAtUtc = null;
        item.windows = item.windows.map((w) => ({
          ...w,
          completedChecks: 0,
          successfulChecks: 0,
          failedChecks: 0,
          observedAvailabilityPercent: null,
          coveragePercent: 0,
        }));
      });
      data.overview.health = 'UNKNOWN';

      data.history.points = data.history.points.map((p) => ({
        ...p,
        state: 'missing',
        completedChecks: 0,
        successfulChecks: 0,
        failedChecks: 0,
        httpResponses: 0,
        averageResponseMs: null,
        minimumResponseMs: null,
        maximumResponseMs: null,
      }));
      data.incidents.items = [];
      data.incidents.nextBeforeId = null;
    }
    const body = url.pathname.endsWith('/history')
      ? data.history
      : url.pathname.endsWith('/incidents')
        ? url.searchParams.has('beforeId')
          ? data.older
          : data.incidents
        : data.overview;
    const send = () => response.end(JSON.stringify(body));
    if (scenario === 'loading') setTimeout(send, 3500);
    else send();
    return;
  }
  const path = resolve(dist, `.${url.pathname === '/' ? '/index.html' : url.pathname}`);
  if (!path.startsWith(dist + sep)) {
    response.writeHead(404);
    response.end();
    return;
  }
  try {
    const body = await readFile(path);
    response.setHeader(
      'content-type',
      {
        '.html': 'text/html',
        '.js': 'application/javascript',
        '.css': 'text/css',
        '.svg': 'image/svg+xml',
      }[extname(path)] ?? 'application/octet-stream',
    );
    response.end(body);
  } catch {
    response.writeHead(404);
    response.end('Build the frontend first with npm run build.');
  }
});
server.listen(5181, '127.0.0.1', () =>
  console.log(
    'SYNTHETIC browser QA: http://127.0.0.1:5181/?scenario=signals\nScenarios: signals, empty, error, stale (Refresh once), missing, loading. No real monitoring data.',
  ),
);
