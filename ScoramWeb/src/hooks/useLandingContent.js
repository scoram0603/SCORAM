import { useSyncExternalStore } from "react";
import { getPublicLandingContent } from "../api/landingContent";

// ONE shared fetch of the public landing configuration for the whole page (logo, feature images, founder photos,
// Smart Progress copy all read from it). Module-level cache: every component that calls the hook shares the same
// request and result, and a re-mount within the TTL does not hit the API again. A failed request is cached as
// "empty" for a short time so an API outage can't cause a request storm -- consumers then simply use their
// bundled defaults, so the page is never blocked or broken by this optional call.
const TTL_MS = 5 * 60 * 1000;
const ERROR_TTL_MS = 30 * 1000;
const EMPTY = { media: {}, smartProgress: null };

let state = { status: "idle", data: EMPTY, at: 0 };
let inflight = null;
const listeners = new Set();

function publish(next) {
  state = next;
  listeners.forEach((fn) => fn(state));
}

function isFresh() {
  const ttl = state.status === "error" ? ERROR_TTL_MS : TTL_MS;
  return state.status !== "idle" && Date.now() - state.at < ttl;
}

function load() {
  if (inflight || isFresh()) return;
  inflight = getPublicLandingContent()
    .then((res) => {
      const media = res && typeof res.media === "object" && res.media ? res.media : {};
      publish({ status: "ready", data: { media, smartProgress: res?.smartProgress ?? null }, at: Date.now() });
    })
    .catch(() => publish({ status: "error", data: EMPTY, at: Date.now() }))
    .finally(() => { inflight = null; });
}

/** Test helper: forget everything (module state survives between unit tests otherwise). */
export function __resetLandingContentCache() {
  state = { status: "idle", data: EMPTY, at: 0 };
  inflight = null;
}

function subscribe(listener) {
  listeners.add(listener);
  load();
  return () => { listeners.delete(listener); };
}

const getSnapshot = () => state;

export function useLandingContent() {
  const snapshot = useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
  return { loaded: snapshot.status === "ready" || snapshot.status === "error", failed: snapshot.status === "error", ...snapshot.data };
}
