import { lazy } from "react";

// React.lazy() wrapper that survives the classic "deploy happened while the tab was open" failure:
// the open tab still references hashed chunk filenames (e.g. Progress-AbC123.js) that the new
// deployment no longer serves, so the dynamic import() rejects. The fix is to reload once so the
// browser picks up the fresh index.html and its new chunk names.
//
// Guard against a reload loop: the reload is attempted at most once per RELOAD_WINDOW_MS (tracked in
// sessionStorage). If the import still fails after that -- genuinely offline, CDN down -- the error
// is rethrown so ChunkErrorBoundary can show a "Try again" button instead of looping forever.
// A successful chunk load clears the marker, so a later, unrelated deployment can reload again.
const RELOAD_KEY = "scoram:chunk-reload-at";
const RELOAD_WINDOW_MS = 15_000;

const CHUNK_ERROR_RE =
  /Failed to fetch dynamically imported module|error loading dynamically imported module|Importing a module script failed|Loading (CSS )?chunk .* failed|ChunkLoadError|Unable to preload CSS/i;

export function isChunkLoadError(error) {
  if (!error) return false;
  return error.name === "ChunkLoadError" || CHUNK_ERROR_RE.test(String(error.message ?? error));
}

function readReloadMarker() {
  try {
    return Number(sessionStorage.getItem(RELOAD_KEY)) || 0;
  } catch {
    return 0;
  }
}

function writeReloadMarker(value) {
  try {
    if (value) sessionStorage.setItem(RELOAD_KEY, String(value));
    else sessionStorage.removeItem(RELOAD_KEY);
  } catch {
    // sessionStorage unavailable (private mode etc.) -- without a marker we must NOT auto-reload.
  }
}

// Returns true if a reload was started. Needs a working sessionStorage so the loop guard can hold.
export function reloadOnceForStaleChunk(now = Date.now(), reload = () => window.location.reload()) {
  const last = readReloadMarker();
  if (last && now - last < RELOAD_WINDOW_MS) return false;
  writeReloadMarker(now);
  if (readReloadMarker() !== now) return false; // storage not writable -> can't guarantee no loop
  reload();
  return true;
}

export function lazyWithRetry(importer) {
  return lazy(async () => {
    try {
      const mod = await importer();
      writeReloadMarker(0);
      return mod;
    } catch (error) {
      if (isChunkLoadError(error) && reloadOnceForStaleChunk()) {
        // The page is reloading; keep Suspense showing the fallback instead of flashing an error.
        return new Promise(() => {});
      }
      throw error;
    }
  });
}
