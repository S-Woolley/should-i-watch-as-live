import { useCallback, useEffect, useState } from 'react';

/** After this long the API is probably cold-starting (scaled to zero, database resuming). */
export const SLOW_AFTER_MS = 3_000;
/** Give up and offer a retry after this long. */
export const TIMEOUT_AFTER_MS = 90_000;

export type RequestState<T> =
  | { status: 'loading'; slow: boolean }
  | { status: 'loaded'; data: T }
  | { status: 'notFound' }
  | { status: 'failed'; timedOut: boolean };

/**
 * Fetches JSON from `url` (nothing when null). Reports `slow` after 3 seconds and fails as `timedOut` after 90.
 * Returns the state and a function that retries the request.
 */
export function useJson<T>(url: string | null): [RequestState<T>, () => void] {
  const [attempt, setAttempt] = useState(0);
  const [state, setState] = useState<RequestState<T>>({ status: 'loading', slow: false });

  useEffect(() => {
    if (url === null) return;

    const controller = new AbortController();
    let timedOut = false;
    setState({ status: 'loading', slow: false });

    const slowTimer = setTimeout(() => setState({ status: 'loading', slow: true }), SLOW_AFTER_MS);
    const timeoutTimer = setTimeout(() => {
      timedOut = true;
      controller.abort();
    }, TIMEOUT_AFTER_MS);

    fetch(url, { signal: controller.signal, headers: { Accept: 'application/json' } })
      .then(async (response) => {
        if (response.status === 404) {
          setState({ status: 'notFound' });
        } else if (!response.ok) {
          setState({ status: 'failed', timedOut: false });
        } else {
          setState({ status: 'loaded', data: (await response.json()) as T });
        }
      })
      .catch(() => {
        if (controller.signal.aborted && !timedOut) return; // unmounted or superseded
        setState({ status: 'failed', timedOut });
      })
      .finally(() => {
        clearTimeout(slowTimer);
        clearTimeout(timeoutTimer);
      });

    return () => {
      clearTimeout(slowTimer);
      clearTimeout(timeoutTimer);
      controller.abort();
    };
  }, [url, attempt]);

  const retry = useCallback(() => setAttempt((n) => n + 1), []);
  return [state, retry];
}
