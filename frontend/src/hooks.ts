import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from './api';

export interface Resource<T> {
  data?: T;
  loading: boolean;
  error?: string;
  receivedAt?: number;
}
// Keep prior data on refresh failures, cancel obsolete requests, and never overlap a poll.
export function useResource<T>(
  load: (signal: AbortSignal) => Promise<T>,
  key: string,
  period = 30_000,
): Resource<T> & { refresh: () => void } {
  const refreshRef = useRef<() => void>(() => {});
  const reload = useCallback(() => refreshRef.current(), []);
  const loader = useRef(load);
  loader.current = load;
  const [state, setState] = useState<Resource<T> & { key: string }>({ loading: true, key });
  useEffect(() => {
    let active = true;
    let busy = false;
    let controller: AbortController | undefined;
    setState({ loading: true, key });
    async function refresh() {
      if (busy || document.hidden) return;
      busy = true;
      controller = new AbortController();
      const timeout = window.setTimeout(() => controller?.abort(), 10_000);
      setState((previous) => ({ ...previous, loading: true }));
      try {
        const data = await loader.current(controller.signal);
        if (active) setState({ data, loading: false, receivedAt: Date.now(), key });
      } catch (error) {
        if (active)
          setState((previous) => ({
            ...previous,
            loading: false,
            error:
              error instanceof ApiError
                ? error.message
                : 'Could not refresh data. Check your connection and try again.',
          }));
      } finally {
        clearTimeout(timeout);
        busy = false;
      }
    }
    refreshRef.current = () => void refresh();
    void refresh();
    const timer = window.setInterval(() => void refresh(), period);
    const onVisible = () => {
      if (!document.hidden) void refresh();
    };
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      active = false;
      controller?.abort();
      clearInterval(timer);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [key, period]);
  return { ...(state.key === key ? state : { loading: true }), refresh: reload };
}
export function useNow() {
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 5_000);
    return () => clearInterval(timer);
  }, []);
  return now;
}
export function useTheme() {
  const [theme, setTheme] = useState<'light' | 'dark'>(() => {
    try {
      const saved = localStorage.getItem('urpo-theme');
      if (saved === 'light' || saved === 'dark') return saved;
    } catch {
      /* Storage may be blocked. */
    }
    return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  });
  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    try {
      localStorage.setItem('urpo-theme', theme);
    } catch {
      /* Appearance still works without storage. */
    }
  }, [theme]);
  return { theme, toggle: () => setTheme((current) => (current === 'light' ? 'dark' : 'light')) };
}
