import { apiFetch } from "../../api/client";

// USER FEEDBACK (admin > Feedback) -- api/admin/feedback*. Every call is checked server-side for the
// ManageFeedback permission; the sidebar/route guards are only convenience.

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// GET ?search=&type=&status=&platform=&from=&to=&page=&pageSize=  -> { items, totalCount, page, pageSize }
export function listFeedback(token, params = {}, { signal } = {}) {
  return apiFetch(`/api/admin/feedback${toQueryString(params)}`, { token, signal });
}

export function getFeedback(token, id) {
  return apiFetch(`/api/admin/feedback/${id}`, { token });
}

// status: New | InReview | Resolved | Rejected
export function updateFeedbackStatus(token, id, status) {
  return apiFetch(`/api/admin/feedback/${id}/status`, { method: "PATCH", token, body: { status } });
}
