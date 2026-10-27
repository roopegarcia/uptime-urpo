import { act, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { fixtures } from './fixtures';
import { getJson, historyAt } from './api';
import { responseSegments } from './ResponseChart';
import { duration, incidentSeconds, percent } from './format';
import type { Overview } from './types';

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
let data: ReturnType<typeof fixtures>;
let fetcher: ReturnType<typeof vi.fn>;
beforeEach(() => {
  location.hash = '#/';
  localStorage.clear();
  data = fixtures();
  fetcher = vi.fn(async (path: string) =>
    path.includes('/history?')
      ? json(data.history)
      : path.includes('beforeId=')
        ? json(data.older)
        : path.includes('/incidents?')
          ? json(data.incidents)
          : json(data.overview),
  );
  vi.stubGlobal('fetch', fetcher);
});
afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});
async function details(id = 'test-1') {
  const user = userEvent.setup();
  render(<App />);
  await screen.findByRole('link', { name: 'View Test API, Down' });
  await user.click(
    screen.getByRole('link', {
      name: id === 'test-1' ? 'View Test API, Down' : 'View Test website, Operational',
    }),
  );
  await screen.findByRole('slider');
  return user;
}
describe('monitoring workspace', () => {
  it('shows loading without implying operational health', async () => {
    fetcher.mockReturnValue(new Promise(() => {}));
    render(<App />);
    expect(screen.getByRole('status')).toHaveTextContent('Loading your monitoring workspace');
    expect(screen.queryByText('All systems operational')).not.toBeInTheDocument();
  });
  it('shows all health states, disabled monitoring, availability and independent coverage', async () => {
    render(<App />);
    await screen.findByRole('heading', { name: 'Some services need attention' });
    expect(screen.getByRole('link', { name: 'View Test disabled, disabled' })).toHaveTextContent(
      'Disabled',
    );
    const service = screen.getByRole('link', { name: 'View Test API, Down' });
    expect(service).toHaveTextContent('91.67%');
    expect(service).toHaveTextContent('60.00% coverage');
    expect(screen.getAllByText('—').length).toBeGreaterThan(0);
  });
  it('filters by name and attention and allows recovery from an empty filter', async () => {
    const user = userEvent.setup();
    render(<App />);
    await screen.findByRole('link', { name: 'View Test API, Down' });
    await user.click(screen.getByRole('checkbox', { name: 'Needs attention' }));
    expect(
      screen.queryByRole('link', { name: 'View Test website, Operational' }),
    ).not.toBeInTheDocument();
    await user.type(screen.getByRole('searchbox'), 'no match');
    expect(screen.getByText('No matching services')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(
      screen.getByRole('link', { name: 'View Test website, Operational' }),
    ).toBeInTheDocument();
  });
  it('explains an empty workspace without adding a target form', async () => {
    data.overview.services = [];
    data.overview.counts.enabled = 0;
    render(<App />);
    await screen.findByText('No services configured yet');
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
    expect(screen.getByText('Ready when you are')).toBeInTheDocument();
  });
  it('shows API errors without rendering arbitrary exception contents', async () => {
    fetcher.mockResolvedValue(json({ title: 'private-host-secret' }, 503));
    render(<App />);
    expect(await screen.findByRole('alert')).toHaveTextContent('temporarily unavailable');
    expect(screen.queryByText(/private-host-secret/)).not.toBeInTheDocument();
  });
  it('retains the last snapshot after a failed refresh and labels health as last known', async () => {
    const user = userEvent.setup();
    render(<App />);
    await screen.findByRole('link', { name: 'View Test API, Down' });
    fetcher.mockRejectedValue(new TypeError('private address'));
    await user.click(screen.getByRole('button', { name: /↻ Refresh/ }));
    await screen.findByRole('alert');
    expect(screen.getByText('Waiting for fresh signals')).toBeInTheDocument();
    expect(screen.getAllByText('Last known: Operational').length).toBeGreaterThan(0);
    expect(
      screen.getByRole('link', { name: 'View Test API, last known Down' }),
    ).toBeInTheDocument();
    fetcher.mockResolvedValue(json(data.overview));
    await user.click(screen.getByRole('button', { name: /↻ Refresh/ }));
    await screen.findByText('Some services need attention');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
  it('switches theme and persists a choice', async () => {
    localStorage.setItem('urpo-theme', 'light');
    const user = userEvent.setup();
    render(<App />);
    await user.click(screen.getByRole('button', { name: 'Switch to dark theme' }));
    expect(document.documentElement.dataset.theme).toBe('dark');
    expect(localStorage.getItem('urpo-theme')).toBe('dark');
  });
  it('keeps theme controls working when browser storage is unavailable', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw Error('blocked');
    });
    const user = userEvent.setup();
    render(<App />);
    await user.click(screen.getByRole('button', { name: /Switch to .* theme/ }));
    expect(screen.getByRole('button', { name: /Switch to .* theme/ })).toBeEnabled();
  });
  it('displays a missing service and supports browser back/forward routes', async () => {
    location.hash = '#/services/removed';
    render(<App />);
    await screen.findByText('Service not found');
    await act(async () => {
      location.hash = '#/';
      window.dispatchEvent(new HashChangeEvent('hashchange'));
    });
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Service overview');
  });
  it('shows a live incident, gap uncertainty, and older cursor pages', async () => {
    const user = await details();
    expect(screen.getByRole('region', { name: 'Current incident' })).toHaveTextContent(
      '20m since first failure',
    );
    expect(
      screen.getByText('Includes monitoring gaps; continuous downtime is not proven.'),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Load older incidents' }));
    await screen.findByText('Recovered');
    expect(fetcher.mock.calls.some(([path]) => path.includes('beforeId=12'))).toBe(true);
    expect(screen.queryByRole('button', { name: 'Load older incidents' })).not.toBeInTheDocument();
  });
  it('keeps loaded pages across a time-window change and keeps focus on the window control', async () => {
    const user = await details();
    await user.click(screen.getByRole('button', { name: 'Load older incidents' }));
    await screen.findByText('Recovered');
    const window = screen.getByRole('button', { name: '30 days' });
    await user.click(window);
    await screen.findByRole('slider');
    expect(window).toHaveFocus();
    expect(screen.getByText('Recovered')).toBeInTheDocument();
    expect(
      fetcher.mock.calls.some(
        ([path]) =>
          path.includes('/history?') && new URL(`http://test${path}`).searchParams.has('to'),
      ),
    ).toBe(true);
  });
  it('keeps incident rows and offers retry when an older page fails', async () => {
    const user = await details();
    fetcher.mockImplementation(async (path: string) =>
      path.includes('beforeId=') ? json({}, 429) : json(data.history),
    );
    await user.click(screen.getByRole('button', { name: 'Load older incidents' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Wait a minute');
    expect(screen.getByText('#12')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Load older incidents' })).toBeEnabled();
  });
  it('renders missing and no-response buckets with no invented response duration', async () => {
    await details();
    const slider = screen.getByRole('slider');
    fireEvent.change(slider, { target: { value: '4' } });
    expect(screen.getByText('Missing observations', { selector: 'small' })).toBeInTheDocument();
    expect(screen.getByText('—', { selector: '.bucket-detail strong' })).toBeInTheDocument();
    fireEvent.change(slider, { target: { value: '8' } });
    expect(screen.getByText(/5 checks · 5 failed/)).toBeInTheDocument();
    expect(screen.getByText('—', { selector: '.bucket-detail strong' })).toBeInTheDocument();
  });
  it('offers all chart data in a semantic table including missing rows', async () => {
    const user = await details();
    await user.click(screen.getByText('View chart data as a table'));
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(13);
    expect(within(screen.getByRole('table')).getAllByText('missing')).toHaveLength(2);
  });
  it('explains history without observations and an incident-free service', async () => {
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
    await details('test-0');
    expect(screen.getByText(/No HTTP responses in this window/)).toBeInTheDocument();
    expect(screen.getByText('No incidents recorded')).toBeInTheDocument();
  });
  it('skip link focuses content without leaving the detail route', async () => {
    const user = await details();
    await user.click(screen.getByRole('link', { name: 'Skip to content' }));
    expect(screen.getByRole('main')).toHaveFocus();
    expect(location.hash).toBe('#/services/test-1');
  });
});
describe('data semantics', () => {
  it('rejects HTML fallback pages instead of treating them as API data', async () => {
    fetcher.mockResolvedValue(
      new Response('<html>wrong origin</html>', { headers: { 'content-type': 'text/html' } }),
    );
    await expect(getJson('/api/overview', new AbortController().signal)).rejects.toThrow(
      'Could not load monitoring data',
    );
  });
  it('does not echo server diagnostics for a removed service', async () => {
    fetcher.mockResolvedValue(json({ title: 'private server exception' }, 404));
    await expect(
      getJson('/api/services/removed/history', new AbortController().signal),
    ).rejects.toThrow('no longer configured');
  });

  it('never joins a line across missing samples or network failures', () => {
    expect(
      responseSegments(data.history.points).map((segment) => segment.map((p) => p.index)),
    ).toEqual([
      [1, 2, 3],
      [6, 7],
      [9, 10, 11],
    ]);
  });
  it('normalizes server precision so 30-day queries remain within the bound', async () => {
    await historyAt(
      'test-1',
      '30d',
      '2026-10-27T12:00:00.123456+00:00',
      new AbortController().signal,
    );
    const query = new URL(`http://test${fetcher.mock.calls[0][0]}`).searchParams;
    expect(Date.parse(query.get('to')!) - Date.parse(query.get('from')!)).toBe(30 * 86_400_000);
    expect(query.get('to')).toBe('2026-10-27T12:00:00.123Z');
  });
  it('reports null percentages as no value, never as zero or 100 percent', () => {
    expect(percent(null)).toBe('—');
    expect(percent(0)).toBe('0.00%');
  });
  it('advances open elapsed time and leaves ended durations fixed', () => {
    const asOf = '2026-10-27T12:00:00Z',
      now = Date.parse(asOf) + 60_000;
    expect(incidentSeconds(120, null, asOf, now)).toBe(180);
    expect(incidentSeconds(120, asOf, asOf, now)).toBe(120);
    expect(duration(90061)).toBe('1d 1h 1m');
  });
});
// Ensure the fixtures stay assignable to the actual read contract.
const _contract: Overview = fixtures().overview;
void _contract;
