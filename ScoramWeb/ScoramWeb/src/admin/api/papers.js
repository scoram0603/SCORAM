import { apiFetch } from "../../api/client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// POST /api/admin/papers -- returns the paper on success (201-ish), or throws an ApiError with
// status 409 and the *existing* paper's data in err.data when this Exam+Year+Language+Code
// combination already exists. Callers should treat a 409 here as "resume this paper", not a failure.
export function createOrFindPaper(token, { examId, year, language, paperCode }) {
  return apiFetch("/api/admin/papers", {
    method: "POST",
    token,
    body: { examId, year, language, paperCode: paperCode || null },
  });
}

// GET /api/admin/papers?status=&examId=&mine=&year=&language=&page=&pageSize=
export function listPapers(token, { status, examId, mine, year, language, page, pageSize } = {}) {
  return apiFetch(`/api/admin/papers${toQueryString({ status, examId, mine, year, language, page, pageSize })}`, { token });
}

// GET /api/admin/papers/pending  (Publish permission required server-side)
export function listPendingPapers(token) {
  return apiFetch("/api/admin/papers/pending", { token });
}

// GET /api/admin/papers/{id} -- { paper, questions }
export function getPaper(token, id) {
  return apiFetch(`/api/admin/papers/${id}`, { token });
}

export function submitPaper(token, id) {
  return apiFetch(`/api/admin/papers/${id}/submit`, { method: "PATCH", token });
}

export function publishPaper(token, id) {
  return apiFetch(`/api/admin/papers/${id}/publish`, { method: "PATCH", token });
}

export function rejectPaper(token, id, reason) {
  return apiFetch(`/api/admin/papers/${id}/reject`, { method: "PATCH", token, body: { reason } });
}

export function unpublishPaper(token, id) {
  return apiFetch(`/api/admin/papers/${id}/unpublish`, { method: "PATCH", token });
}

export function deletePaper(token, id) {
  return apiFetch(`/api/admin/papers/${id}`, { method: "DELETE", token });
}

// POST /api/admin/papers/reindex-search  (Super Admin only) -- rebuilds the Meilisearch index from
// every currently-Published paper. Needed once after this feature's first deploy, or any time the
// index and DB might have drifted (e.g. Meilisearch was down during a publish/unpublish).
export function reindexSearch(token) {
  return apiFetch("/api/admin/papers/reindex-search", { method: "POST", token });
}
