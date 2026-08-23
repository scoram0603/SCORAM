import { apiFetch } from "./client";

// Maps each bookmarkable type to its toggle route -- mirrors votes.js's basePath idea, just with
// one more type since Bookmarks covers five content kinds instead of two.
const TOGGLE_PATH = {
  question: (id) => `/api/questions/${id}/bookmark`,
  questionBank: (id) => `/api/question-bank/${id}/bookmark`,
  discussion: (id) => `/api/discussions/${id}/bookmark`,
  paper: (id) => `/api/papers/${id}/bookmark`,
  mockTest: (id) => `/api/mocktests/${id}/bookmark`,
};

// POST .../bookmark -- toggles: bookmarks it if not already, un-bookmarks it if it already was.
// Returns { isBookmarked }. `type` is one of the keys in TOGGLE_PATH above.
export function toggleBookmark(type, id) {
  const path = TOGGLE_PATH[type];
  if (!path) throw new Error(`Unknown bookmark type: ${type}`);
  return apiFetch(path(id), { method: "POST", auth: true });
}

// GET /api/bookmarks -- unified, most-recent-first list across every type this student has saved.
// `type` is "all" | "questions" | "discussions" | "papers" | "mocktests" (matches the backend's
// BookmarksController.List query param, NOT the singular TOGGLE_PATH keys above).
export function getBookmarks({ type = "all", page = 1, pageSize = 20 } = {}) {
  const params = new URLSearchParams({ type, page: String(page), pageSize: String(pageSize) });
  return apiFetch(`/api/bookmarks?${params}`, { auth: true });
}
