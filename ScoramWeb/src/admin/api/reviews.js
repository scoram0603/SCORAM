import { apiFetch } from "../../api/client";

// STUDENT REVIEWS moderation (admin > Student Reviews) -- api/admin/reviews*. Every call is checked server-side for the
// ModerateReviews permission; the sidebar/route guards are only convenience.

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// GET ?status=&rating=&search=&page=&pageSize= -> { items, totalCount, page, pageSize, summary }
export function listReviews(token, params = {}, { signal } = {}) {
  return apiFetch(`/api/admin/reviews${toQueryString(params)}`, { token, signal });
}

// status: Pending | Approved | Rejected | Hidden
export function setReviewStatus(token, id, status, note) {
  return apiFetch(`/api/admin/reviews/${id}/status`, { method: "PATCH", token, body: { status, note: note || null } });
}

// Permanently removes ONLY that review (never the student's account).
export function deleteReview(token, id) {
  return apiFetch(`/api/admin/reviews/${id}`, { method: "DELETE", token });
}
