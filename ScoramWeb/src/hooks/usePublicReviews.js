import { useCallback, useEffect, useRef, useState } from "react";
import { getPublicReviews } from "../api/reviews";
import { filterPublicReviews } from "../utils/reviews";

// Public landing reviews with "load more" paging. No polling: one request on mount, plus one per "Show more" click.
// The first page is cached at module level for a few minutes so navigating away and back doesn't refetch, and a
// stale copy keeps showing while a background refresh runs (a failed refresh never blanks reviews already shown).
const PAGE_SIZE = 6;
const TTL_MS = 5 * 60 * 1000;
let firstPageCache = null; // { at, items, totalCount }

export function usePublicReviews() {
  const [state, setState] = useState(() =>
    firstPageCache
      ? { status: "ready", items: firstPageCache.items, totalCount: firstPageCache.totalCount, page: 1 }
      : { status: "loading", items: [], totalCount: 0, page: 0 },
  );
  const [loadingMore, setLoadingMore] = useState(false);
  const [moreError, setMoreError] = useState(false);
  const controllerRef = useRef(null);

  const loadFirst = useCallback(() => {
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    setState((s) => (s.items.length ? s : { ...s, status: "loading" }));
    getPublicReviews({ page: 1, pageSize: PAGE_SIZE, signal: controller.signal })
      .then((res) => {
        const items = filterPublicReviews(res?.items);
        const totalCount = Number.isInteger(res?.totalCount) ? res.totalCount : items.length;
        firstPageCache = { at: Date.now(), items, totalCount };
        setState({ status: "ready", items, totalCount, page: 1 });
      })
      .catch((err) => {
        if (err.name === "AbortError") return;
        setState((s) => (s.items.length ? s : { ...s, status: "error" }));
      });
  }, []);

  useEffect(() => {
    if (!firstPageCache || Date.now() - firstPageCache.at > TTL_MS) loadFirst();
    return () => controllerRef.current?.abort();
  }, [loadFirst]);

  const loadMore = useCallback(() => {
    if (loadingMore) return;
    setLoadingMore(true);
    setMoreError(false);
    getPublicReviews({ page: state.page + 1, pageSize: PAGE_SIZE })
      .then((res) => {
        setState((s) => {
          const merged = filterPublicReviews([...s.items, ...(Array.isArray(res?.items) ? res.items : [])]);
          return { ...s, items: merged, page: s.page + 1, totalCount: Number.isInteger(res?.totalCount) ? res.totalCount : s.totalCount };
        });
      })
      .catch(() => setMoreError(true))
      .finally(() => setLoadingMore(false));
  }, [loadingMore, state.page]);

  return { ...state, loadingMore, moreError, loadMore, retry: loadFirst, hasMore: state.items.length < state.totalCount };
}
