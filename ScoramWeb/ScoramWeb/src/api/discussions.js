import { apiFetch } from "./client";

// Both "paper" (legacy PYQ question, default) and "bank" (Question Bank question) share the exact
// same backend table/logic (DiscussionsController) -- see Controllers/DiscussionsController.cs.
function basePath(questionType) {
  return questionType === "bank" ? "/api/question-bank" : "/api/questions";
}

// GET /api/discussions?page=&pageSize= — global top-voted comment feed across all questions
export function getTopDiscussions({ page = 1, pageSize = 20 } = {}, opts = {}) {
  const query = new URLSearchParams({ page, pageSize }).toString();
  return apiFetch(`/api/discussions?${query}`, opts);
}

// GET /api/questions/{questionId}/comments or /api/question-bank/{questionId}/comments -- full
// nested thread for one question
export function getQuestionComments(questionId, opts = {}, questionType = "paper") {
  return apiFetch(`${basePath(questionType)}/${questionId}/comments`, opts);
}

// POST .../comments
export function createComment(questionId, commentText, questionType = "paper") {
  return apiFetch(`${basePath(questionType)}/${questionId}/comments`, {
    method: "POST",
    body: { commentText },
    auth: true,
  });
}

// POST /api/comments/{commentId}/replies -- same route regardless of question type; the backend
// infers it from the parent comment.
export function replyToComment(commentId, commentText) {
  return apiFetch(`/api/comments/${commentId}/replies`, {
    method: "POST",
    body: { commentText },
    auth: true,
  });
}

// POST /api/comments/{commentId}/upvote -- toggles: same vote again retracts it, the other vote
// switches it. Returns { id, upvoteCount, downvoteCount, myVote }.
export function upvoteComment(commentId) {
  return apiFetch(`/api/comments/${commentId}/upvote`, { method: "POST", auth: true });
}

// POST /api/comments/{commentId}/downvote -- same toggle, opposite direction.
export function downvoteComment(commentId) {
  return apiFetch(`/api/comments/${commentId}/downvote`, { method: "POST", auth: true });
}

// PATCH /api/comments/{commentId}/resolve -- toggles; caller must be the top-level comment's author
// or an admin (checked server-side)
export function toggleCommentResolved(commentId) {
  return apiFetch(`/api/comments/${commentId}/resolve`, { method: "PATCH", auth: true });
}

// POST /api/comments/{commentId}/report
export function reportComment(commentId, reason) {
  return apiFetch(`/api/comments/${commentId}/report`, { method: "POST", auth: true, body: { reason } });
}
