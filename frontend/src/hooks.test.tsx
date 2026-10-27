import { act, renderHook } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { useResource } from './hooks';
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});
it('does not overlap slow requests, aborts at ten seconds, and cancels on unmount', async () => {
  vi.useFakeTimers();
  const signals: AbortSignal[] = [];
  const load = vi.fn((signal: AbortSignal) => {
    signals.push(signal);
    return new Promise<number>((_, reject) =>
      signal.addEventListener('abort', () => reject(new Error('aborted'))),
    );
  });
  const { result, unmount } = renderHook(() => useResource(load, 'test', 1000));
  await act(() => vi.advanceTimersByTimeAsync(9000));
  expect(load).toHaveBeenCalledTimes(1);
  await act(() => vi.advanceTimersByTimeAsync(1000));
  expect(signals[0].aborted).toBe(true);
  expect(result.current.error).toBeDefined();
  await act(() => vi.advanceTimersByTimeAsync(1000));
  expect(load).toHaveBeenCalledTimes(2);
  unmount();
  expect(signals[1].aborted).toBe(true);
});
it('skips hidden-tab polling and refreshes upon returning', async () => {
  vi.useFakeTimers();
  let hidden = false;
  vi.spyOn(document, 'hidden', 'get').mockImplementation(() => hidden);
  const load = vi.fn(async () => 42);
  const { result } = renderHook(() => useResource(load, 'test', 1000));
  await act(() => vi.advanceTimersByTimeAsync(1));
  expect(result.current.data).toBe(42);
  hidden = true;
  await act(() => vi.advanceTimersByTimeAsync(5000));
  expect(load).toHaveBeenCalledTimes(1);
  hidden = false;
  await act(async () => {
    document.dispatchEvent(new Event('visibilitychange'));
  });
  expect(load).toHaveBeenCalledTimes(2);
});
it('cancels obsolete window requests and never shows their data under the new key', async () => {
  const signals: AbortSignal[] = [];
  const load = vi.fn((signal: AbortSignal) => {
    signals.push(signal);
    return new Promise<number>(() => {});
  });
  const { result, rerender, unmount } = renderHook(({ key }) => useResource(load, key), {
    initialProps: { key: '24h' },
  });
  rerender({ key: '30d' });
  expect(signals[0].aborted).toBe(true);
  expect(result.current.data).toBeUndefined();
  unmount();
  expect(signals[1].aborted).toBe(true);
});
