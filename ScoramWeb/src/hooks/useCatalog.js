import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from "react";
import { getCatalogEntry, loadCatalog, subscribeCatalog } from "../api/catalogCache";

// React binding for api/catalogCache.js.
//
//   const { data, loading, error, reload } = useCatalog(key, () => getQuestionBankExams());
//
// - `key` comes from catalogKey(...) and is recomputed by the caller each render (it embeds the
//   signed-in student, so a different student can never read this one's entry).
// - A cached value paints on the very first render (no spinner on a revisit); a stale one is
//   refreshed in the background and the component re-renders when it lands.
// - `enabled: false` neither reads-through nor fetches (it still shows a value that is already cached).
// - If the entry is dropped (My Exams changed, logout) the hook refetches by itself.
// - Failures are returned in `error` and never cached; the caller decides whether to show them.
// - The fetcher is deliberately NOT given an AbortSignal (see catalogCache.js): the request is shared.
export function useCatalog(key, fetcher, { enabled = true, maxAgeMs } = {}) {
  const subscribe = useCallback((callback) => subscribeCatalog(key, callback), [key]);
  const getSnapshot = useCallback(() => getCatalogEntry(key), [key]);
  const entry = useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
  const hasEntry = entry !== undefined;

  const [failure, setFailure] = useState(null); // { key, error }
  const [nonce, setNonce] = useState(0);
  const forceRef = useRef(false);
  const fetcherRef = useRef(fetcher);
  useEffect(() => {
    fetcherRef.current = fetcher;
  });

  useEffect(() => {
    if (!enabled) return undefined;
    let cancelled = false;
    const force = forceRef.current;
    forceRef.current = false;
    loadCatalog(key, () => fetcherRef.current(), force ? { maxAgeMs: 0 } : { maxAgeMs }).then(
      () => {
        if (cancelled) return;
        setFailure(null);
        // The result was discarded because the cache was cleared while it was in flight: ask again.
        if (getCatalogEntry(key) === undefined) setNonce((n) => n + 1);
      },
      (error) => {
        if (!cancelled) setFailure({ key, error });
      }
    );
    return () => {
      cancelled = true;
    };
  }, [key, enabled, hasEntry, nonce, maxAgeMs]);

  const reload = useCallback(() => {
    forceRef.current = true;
    setFailure(null);
    setNonce((n) => n + 1);
  }, []);

  const error = failure && failure.key === key ? failure.error : null;
  return {
    data: entry ? entry.data : undefined,
    loading: enabled && !hasEntry && !error,
    error,
    reload,
  };
}
