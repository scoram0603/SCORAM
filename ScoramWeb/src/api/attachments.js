import { API_BASE_URL, apiFetchBlob } from "./client";

// PRIVATE CHAT / DM ATTACHMENTS
//
// Chat and direct-message attachments are no longer public "/uploads/..." URLs. The API now sends, for each
// message that has an attachment, an authenticated path such as
//   /api/chat/messages/{id}/attachment          (group chat)
//   /api/directmessages/messages/{id}/attachment (direct message)
// which only works with the student's Bearer token. A browser cannot attach that header to <img src> or
// <audio src>, so the bytes are fetched here with the shared client (apiFetchBlob: token in the header, one
// silent refresh on 401) and shown through an object URL.
//
// Object URLs are shared per path and reference-counted: every component showing the same attachment reuses
// ONE download, and the URL is revoked (memory freed) shortly after the last component stops showing it.
// Nothing here puts a token in a URL, and nothing is written to localStorage.

const PRIVATE_ATTACHMENT_PATH =
  /^\/api\/(?:chat|directmessages)\/messages\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\/attachment$/i;

// After the last user of an attachment lets go, keep the object URL alive this long. It absorbs React's
// mount/unmount churn (StrictMode, scrolling a message just off-screen and back) without re-downloading, and
// gives the browser time to start a download that was triggered from the URL before it is revoked.
export const RELEASE_GRACE_MS = 15000;

/**
 * Returns the API path for a message's attachment if `url` is one (a relative path, or an absolute URL on our
 * own API origin), otherwise null. Anything else -- including a legacy "/uploads/chat-attachments/..." value --
 * is NOT treated as loadable: those URLs no longer work, and we never fall back to loading them anonymously.
 */
export function toPrivateAttachmentPath(url) {
  if (typeof url !== "string" || !url) return null;
  let path = url;
  if (/^https?:\/\//i.test(path)) {
    if (!path.startsWith(API_BASE_URL)) return null;
    path = path.slice(API_BASE_URL.length);
  }
  return PRIVATE_ATTACHMENT_PATH.test(path) ? path : null;
}

/**
 * Turns whatever a failed attachment load threw into something the UI can show.
 *   unauthorized -- 401/403: the session ended or the student no longer has access
 *   gone         -- 404: deleted, never existed, or the file is missing in storage
 *   network      -- the API couldn't be reached
 *   server       -- 5xx
 *   unavailable  -- not a loadable attachment path (e.g. an old public URL)
 */
export function describeAttachmentError(error) {
  const status = error?.status;
  if (status === 401 || status === 403) {
    return { kind: "unauthorized", message: "Sign in again to view this attachment.", retryable: false };
  }
  if (status === 404) {
    return { kind: "gone", message: "This attachment is no longer available.", retryable: false };
  }
  if (status === 0 || status === undefined) {
    return { kind: "network", message: "Couldn't load this attachment. Check your connection.", retryable: true };
  }
  if (status >= 500) {
    return { kind: "server", message: "Couldn't load this attachment right now.", retryable: true };
  }
  return { kind: "server", message: "Couldn't load this attachment.", retryable: true };
}

export const UNAVAILABLE_ATTACHMENT_ERROR = {
  kind: "unavailable",
  message: "This attachment is no longer available.",
  retryable: false,
};

const entries = new Map(); // path -> { refs, controller, blob, objectUrl, promise, timer }

function dispose(path, entry) {
  clearTimeout(entry.timer);
  if (entries.get(path) === entry) entries.delete(path);
  if (entry.objectUrl) {
    URL.revokeObjectURL(entry.objectUrl);
    entry.objectUrl = null;
  } else {
    entry.controller.abort(); // still downloading and nobody wants it any more
  }
  entry.blob = null;
}

/**
 * Starts (or joins) the download of one attachment. Resolves to { url, blob, release }:
 *   url     -- an object URL usable as <img src> / <audio src> / an anchor href
 *   release -- call it exactly when the caller stops using the URL (idempotent)
 * Rejects with the ApiError from the client (see describeAttachmentError). If every user has released the
 * entry before the download finished, the result is discarded and `url` is null.
 */
export function acquireAttachment(path) {
  let entry = entries.get(path);
  if (!entry) {
    const created = { refs: 0, controller: new AbortController(), blob: null, objectUrl: null, promise: null, timer: null };
    created.promise = apiFetchBlob(path, { signal: created.controller.signal })
      .then(({ blob }) => {
        if (entries.get(path) !== created) return created; // everyone left while it downloaded
        created.blob = blob;
        created.objectUrl = URL.createObjectURL(blob);
        return created;
      })
      .catch((error) => {
        if (entries.get(path) === created) entries.delete(path); // a failed load is not cached: a retry starts fresh
        throw error;
      });
    entries.set(path, created);
    entry = created;
  }

  clearTimeout(entry.timer);
  entry.refs += 1;
  const mine = entry;
  let released = false;
  const release = () => {
    if (released) return;
    released = true;
    mine.refs -= 1;
    if (mine.refs <= 0) {
      clearTimeout(mine.timer);
      mine.timer = setTimeout(() => {
        if (mine.refs <= 0) dispose(path, mine);
      }, RELEASE_GRACE_MS);
    }
  };

  return mine.promise.then(
    (loaded) => ({ url: loaded.objectUrl, blob: loaded.blob, release }),
    (error) => {
      release();
      throw error;
    }
  );
}

/** Frees every cached attachment immediately (sign-out / expired session) so one student's private files are
 *  never kept in memory for the next person using the tab. */
export function clearAttachmentCache() {
  for (const [path, entry] of [...entries]) {
    entry.refs = 0;
    dispose(path, entry);
  }
}

const EXTENSION_BY_TYPE = {
  "application/pdf": ".pdf",
  "application/msword": ".doc",
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document": ".docx",
  "application/vnd.ms-excel": ".xls",
  "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet": ".xlsx",
  "application/vnd.ms-powerpoint": ".ppt",
  "application/vnd.openxmlformats-officedocument.presentationml.presentation": ".pptx",
  "image/png": ".png",
  "image/jpeg": ".jpg",
  "image/webp": ".webp",
  "audio/webm": ".webm",
  "audio/mp4": ".m4a",
  "audio/mpeg": ".mp3",
  "audio/ogg": ".ogg",
  "audio/wav": ".wav",
};

export function extensionForContentType(contentType) {
  const type = String(contentType || "").split(";")[0].trim().toLowerCase();
  return EXTENSION_BY_TYPE[type] || "";
}

function saveBlobAs(url, fileName) {
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  link.rel = "noopener";
  document.body.appendChild(link);
  link.click();
  link.remove();
}

/** Downloads a (document) attachment through the authenticated API. Throws the same errors as acquireAttachment. */
export async function downloadAttachment(path, baseName = "attachment") {
  const { url, blob, release } = await acquireAttachment(path);
  try {
    if (!url) return;
    saveBlobAs(url, `${baseName}${extensionForContentType(blob?.type)}`);
  } finally {
    release(); // the grace period keeps the URL valid long enough for the download to start
  }
}

/**
 * "Open document": a PDF is previewed in a new tab, anything else is downloaded. The tab is opened
 * synchronously (inside the click) so popup blockers allow it, then pointed at the object URL once the file
 * arrives; if it was blocked, the file is downloaded instead.
 */
export async function openDocumentAttachment(path, baseName = "attachment") {
  const tab = typeof window !== "undefined" ? window.open("about:blank", "_blank") : null;
  if (tab) tab.opener = null;
  try {
    const { url, blob, release } = await acquireAttachment(path);
    if (!url) {
      tab?.close();
      return;
    }
    if (tab && blob?.type === "application/pdf") {
      tab.location.href = url;
      setTimeout(release, 60000); // keep it alive while the viewer loads
      return;
    }
    tab?.close();
    try {
      saveBlobAs(url, `${baseName}${extensionForContentType(blob?.type)}`);
    } finally {
      release();
    }
  } catch (error) {
    tab?.close();
    throw error;
  }
}

if (typeof window !== "undefined" && typeof window.addEventListener === "function") {
  window.addEventListener("scoram:session-expired", clearAttachmentCache);
}
