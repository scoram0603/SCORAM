import { apiFetch } from "./client";

// USER FEEDBACK -- see ScoramAPI/Controllers/FeedbackController.cs.

// POST /api/feedback (student only; the user id comes from the token, never the body).
// feedbackType: Suggestion | Improvement | BugReport | ContentIssue | UiUx | Other
// rating (1-5) and source are optional. Platform is always "Web" from this app.
export function submitFeedback({ feedbackType, message, rating, source }) {
  return apiFetch("/api/feedback", {
    method: "POST",
    auth: true,
    body: { feedbackType, message, rating: rating || null, source: source || null, platform: "Web" },
  });
}
