import { useCallback, useEffect, useRef, useState } from "react";

// Tiny query helper (the app has no query library). Runs fetcher(signal) whenever `deps` change,
// ABORTS the previous request first (so a slow response for old filters can never overwrite a newer
// one), keeps the previous data on screen while refetching, and exposes reload() for "Retry".
//
//   const q = useApiQuery((signal) => getProgressOverview(filters, { signal }), [filterKey]);
//   q.data, q.error, q.loading (true during any fetch), q.initialLoading (no data yet), q.reload()
//
// apiFetch turns an AbortError into an ApiError, so staleness is detected via signal.aborted, never
// via the error type.
export function useApiQuery(fetcher, deps, { enabled = true } = {}) {
  const [state, setState] = useState({ data: null, error: null, loading: enabled });
  const [nonce, setNonce] = useState(0);
  const fetcherRef = useRef(fetcher);

  useEffect(() => {
    fetcherRef.current = fetcher;
  });

  useEffect(() => {
    if (!enabled) return undefined;
    const controller = new AbortController();
    setState((s) => ({ ...s, error: null, loading: true }));
    Promise.resolve()
      .then(() => fetcherRef.current(controller.signal))
      .then((data) => {
        if (!controller.signal.aborted) setState({ data, error: null, loading: false });
      })
      .catch((error) => {
        if (!controller.signal.aborted) setState((s) => ({ data: s.data, error, loading: false }));
      });
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, nonce, enabled]);

  const reload = useCallback(() => setNonce((n) => n + 1), []);

  return {
    data: state.data,
    error: state.error,
    loading: state.loading,
    initialLoading: state.loading && state.data == null,
    reload,
  };
}
