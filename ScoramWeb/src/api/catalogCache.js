import { getStoredToken } from "./client";

// In-memory, per-tab cache for the "catalog" lookups the student site repeats on every page visit
// (Question Bank exams / years / subjects / topics / total, the public exam list). The app has no
// query library, so this is a small purpose-built one. Three behaviours, nothing else:
//
//   1. keyed cache    -- a result younger than maxAgeMs (3 min, matching the server-side catalog TTL)
//                        is returned without any request;
//   2. in-flight de-dup -- concurrent callers for the same key share ONE request. The fetcher is
//                        never handed a caller's AbortSignal: one component unmounting must not cancel
//                        the request another component is waiting on. Callers simply ignore a result
//                        that arrives after they are gone;
//   3. stale-while-revalidate -- a result older than maxAgeMs is still returned immediately and one
//                        background request refreshes it (subscribers are notified when it lands).
//
// Nothing is persisted (no localStorage/sessionStorage): a full reload starts empty.
//
// SAFETY: the catalog endpoints are My-Exams scoped per student (they are called with optionalAuth),
// so a user-scoped key always embeds WHO the data is for. The identity is derived from the access
// token itself (the `sub` claim), because the token is written synchronously on login whereas the
// React `user` state / localStorage copy is not yet there when a freshly mounted child's first effect
// runs. If the claim cannot be read the key falls back to the token's own tail, i.e. it can only ever
// match that exact token -- never another student's. Public, unauthenticated endpoints (scope
// "public") are shared by everyone and survive logout.
//
// Entries are dropped (and in-flight results discarded, via the epoch) on logout / account deletion /
// session expiry and whenever the student's My Exams selection changes -- see AuthContext and
// MyExamsContext. Consumers must treat cached values as read-only (copy before sorting).

export const CATALOG_MAX_AGE_MS = 3 * 60 * 1000;

const entries = new Map(); // key -> { data, fetchedAt }
const inflight = new Map(); // key -> { promise }
const listeners = new Map(); // key -> Set<() => void>
let epoch = 0; // bumped by clear/invalidate; a request started in an older epoch never writes its result

let identityTokenMemo = null;
let identityMemo = "anon";

function base64UrlDecode(segment) {
  const padded = segment.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(segment.length / 4) * 4, "=");
  return atob(padded);
}

function subjectOfToken(token) {
  try {
    const payload = JSON.parse(base64UrlDecode(token.split(".")[1] ?? ""));
    const sub = payload?.sub ?? payload?.nameid;
    return sub ? String(sub) : null;
  } catch {
    return null;
  }
}

// Who the current catalog data belongs to. Memoised on the token string so the hot render path does
// not decode a JWT every time.
export function currentCatalogIdentity() {
  const token = getStoredToken();
  if (!token) return "anon";
  if (token !== identityTokenMemo) {
    identityTokenMemo = token;
    identityMemo = subjectOfToken(token) ?? `t:${token.slice(-32)}`;
  }
  return identityMemo;
}

// scope "user" (default): depends on the signed-in student. scope "public": identical for everyone.
export function catalogKey(name, params = "", { scope = "user" } = {}) {
  const prefix = scope === "public" ? "pub" : `u:${currentCatalogIdentity()}`;
  return `${prefix}|${name}|${params ?? ""}`;
}

export function peekCatalog(key) {
  return entries.get(key)?.data;
}

// Stable object per write (or undefined) -- what useSyncExternalStore snapshots.
export function getCatalogEntry(key) {
  return entries.get(key);
}

export function subscribeCatalog(key, callback) {
  let set = listeners.get(key);
  if (!set) {
    set = new Set();
    listeners.set(key, set);
  }
  set.add(callback);
  return () => {
    set.delete(callback);
    if (set.size === 0 && listeners.get(key) === set) listeners.delete(key);
  };
}

function notify(key) {
  const set = listeners.get(key);
  if (set) [...set].forEach((cb) => cb());
}

// Seed/overwrite an entry from a response the caller already has (e.g. the question total that
// comes back with the first page of results), so no extra request is needed.
export function setCatalog(key, data) {
  entries.set(key, { data, fetchedAt: Date.now() });
  notify(key);
}

function startFetch(key, fetcher) {
  const existing = inflight.get(key);
  if (existing) return existing.promise;

  const startedInEpoch = epoch;
  const record = {};
  record.promise = Promise.resolve()
    .then(() => fetcher())
    .then((data) => {
      if (startedInEpoch === epoch) {
        entries.set(key, { data, fetchedAt: Date.now() });
        notify(key);
      }
      return data;
    })
    .finally(() => {
      if (inflight.get(key) === record) inflight.delete(key);
    });
  inflight.set(key, record);
  return record.promise;
}

// Resolves with the data. Fresh -> cached, no request. Stale -> cached now, one background refresh
// (its failure is swallowed; the stale value stays). Missing -> shared request; rejects on failure
// and caches nothing.
export function loadCatalog(key, fetcher, { maxAgeMs = CATALOG_MAX_AGE_MS } = {}) {
  const entry = entries.get(key);
  if (entry) {
    const age = Date.now() - entry.fetchedAt;
    // age < 0 = the clock moved backwards; do not trust it as "fresh forever".
    if (age >= maxAgeMs || age < 0) startFetch(key, fetcher).catch(() => {});
    return Promise.resolve(entry.data);
  }
  return startFetch(key, fetcher);
}

function dropEntries(shouldDrop) {
  epoch += 1;
  const dropped = [];
  for (const key of [...entries.keys()]) {
    if (shouldDrop(key)) {
      entries.delete(key);
      dropped.push(key);
    }
  }
  for (const key of [...inflight.keys()]) {
    if (shouldDrop(key)) inflight.delete(key);
  }
  // Notify AFTER everything is removed so a subscriber that refetches starts a clean request.
  dropped.forEach(notify);
}

const isUserScoped = (key) => key.startsWith("u:");

// The student's My Exams selection changed: every user-scoped list may now differ. Mounted hooks see
// their entry disappear and refetch.
export function invalidateCatalog() {
  dropEntries(isUserScoped);
}

// Logout / account deleted / session expired: nothing of this student's may survive. Public entries
// (the exam list) are identical for everyone and stay.
export function clearCatalog() {
  dropEntries(isUserScoped);
}

export function resetCatalogForTests() {
  epoch += 1;
  entries.clear();
  inflight.clear();
  listeners.clear();
  identityTokenMemo = null;
  identityMemo = "anon";
}

if (typeof window !== "undefined" && typeof window.addEventListener === "function") {
  window.addEventListener("scoram:session-expired", clearCatalog);
}
