import { apiFetch, API_BASE_URL, getStoredAdminToken } from "../../api/client";

// LANDING PAGE content (admin > Landing Page) -- api/admin/landing-content*. Server-side permission: ManageLandingContent.

// -> { slots:[{key,label,group,recommended,description,hasBundledDefault,customUrl,updatedAt}], smartProgress, limits }
export function getLandingContentAdmin(token, { signal } = {}) {
  return apiFetch("/api/admin/landing-content", { token, signal });
}

export function saveSmartProgress(token, content) {
  return apiFetch("/api/admin/landing-content/smart-progress", { method: "PUT", token, body: content });
}

export function resetSmartProgress(token) {
  return apiFetch("/api/admin/landing-content/smart-progress", { method: "DELETE", token });
}

// Back to the website's bundled default for that slot (the previous upload is deleted after the DB change).
export function resetLandingMedia(token, slot) {
  return apiFetch(`/api/admin/landing-content/media/${encodeURIComponent(slot)}`, { method: "DELETE", token });
}

/**
 * Uploads a replacement image with real upload progress (fetch has no upload-progress events, so this uses XHR).
 * Resolves with { key, customUrl }; rejects with an Error whose message is user-presentable (`.status` set).
 * The server stores the new file BEFORE swapping the reference, so any failure leaves the current image untouched.
 */
export function uploadLandingMedia(token, slot, file, { onProgress, signal } = {}) {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", `${API_BASE_URL}/api/admin/landing-content/media/${encodeURIComponent(slot)}`);
    // Re-read the stored token at send time: apiFetch may have silently refreshed it since the page loaded.
    const bearer = getStoredAdminToken() || token;
    if (bearer) xhr.setRequestHeader("Authorization", `Bearer ${bearer}`);

    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) onProgress?.(Math.round((e.loaded / e.total) * 100));
    };
    xhr.onload = () => {
      let body = null;
      try { body = JSON.parse(xhr.responseText); } catch { /* non-JSON body */ }
      if (xhr.status >= 200 && xhr.status < 300) return resolve(body);
      const fallback =
        xhr.status === 401 ? "Your admin session expired. Sign in again, then retry."
        : xhr.status === 403 ? "You don't have permission to change landing-page images."
        : xhr.status === 413 ? "The image is too large."
        : "The upload failed, so the current image was kept.";
      const err = new Error(body?.message || fallback);
      err.status = xhr.status;
      reject(err);
    };
    xhr.onerror = () => reject(new Error("Couldn't reach the server. The current image was kept."));
    xhr.onabort = () => { const e = new Error("Upload cancelled."); e.name = "AbortError"; reject(e); };
    signal?.addEventListener("abort", () => xhr.abort());

    const form = new FormData();
    form.append("file", file);
    xhr.send(form);
  });
}
