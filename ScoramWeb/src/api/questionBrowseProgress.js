import { API_BASE_URL, apiFetch, getStoredToken } from "./client";

// Per-student PYQ (Question Bank) browsing progress -- see QuestionBrowseProgressController on the API.
// Identity always comes from the JWT; there is no user id in any of these calls.

// GET /api/question-bank/progress -> null (204) when the student has no active browsing session.
export function getBrowseProgress({ signal } = {}) {
  return apiFetch("/api/question-bank/progress", { auth: true, signal });
}

// PUT /api/question-bank/progress { answers?: [{questionId, selectedOption, clientUpdatedAt}], position?: {...} }
// Idempotent and last-write-wins per question (the server compares clientUpdatedAt).
export function syncBrowseProgress(body, { signal } = {}) {
  return apiFetch("/api/question-bank/progress", { method: "PUT", body, auth: true, signal });
}

// POST /api/question-bank/progress/new-session -- "Start fresh". The old session is archived, never deleted.
export function startFreshBrowse() {
  return apiFetch("/api/question-bank/progress/new-session", { method: "POST", auth: true });
}

// Same PUT, but with `keepalive` so the browser still delivers it while the tab is closing / hiding.
// (apiFetch can't be used here: it has no keepalive option and may retry through a token refresh.)
export function syncBrowseProgressKeepalive(body) {
  const token = getStoredToken();
  if (!token) return;
  try {
    fetch(`${API_BASE_URL}/api/question-bank/progress`, {
      method: "PUT",
      keepalive: true,
      headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
      body: JSON.stringify(body),
    }).catch(() => {});
  } catch {
    /* best effort -- the same data is also mirrored in localStorage and re-sent on the next visit */
  }
}
