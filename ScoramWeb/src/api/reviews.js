import { apiFetch } from "./client";

// STUDENT REVIEWS -- see ScoramAPI/Controllers/ReviewsController.cs. The owner is always the signed-in student
// (taken from the token on the server); no call here ever sends a user id.

// GET /api/reviews/mine -> { review: { id, rating, text, showNamePublicly, status, createdAt, updatedAt } | null }
export function getMyReview({ signal } = {}) {
  return apiFetch("/api/reviews/mine", { auth: true, signal });
}

// PUT /api/reviews/mine -> the saved review. An edit sends it back to "Pending" for moderation.
export function saveMyReview({ rating, text, showNamePublicly }) {
  return apiFetch("/api/reviews/mine", { method: "PUT", auth: true, body: { rating, text, showNamePublicly: !!showNamePublicly } });
}

// DELETE /api/reviews/mine -> 204 (withdraws the review permanently)
export function withdrawMyReview() {
  return apiFetch("/api/reviews/mine", { method: "DELETE", auth: true });
}

// PUBLIC: GET /api/public/reviews?page=&pageSize= -> { items:[{id,rating,text,displayName,createdAt}], totalCount, page, pageSize }
// The server only returns Approved reviews rated 3-5 stars.
export function getPublicReviews({ page = 1, pageSize = 6, signal } = {}) {
  return apiFetch(`/api/public/reviews?page=${page}&pageSize=${pageSize}`, { signal });
}
