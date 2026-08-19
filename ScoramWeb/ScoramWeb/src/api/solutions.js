import { apiFetch } from "./client";

// Both routes share the exact same backend table/logic (SolutionsController) -- see
// Controllers/SolutionsController.cs. questionType picks which route family to call:
// "paper" (default, unchanged behavior) -> /api/questions/{id}/solutions
// "bank" (new, Question Bank)          -> /api/question-bank/{id}/solutions
function basePath(questionType) {
  return questionType === "bank" ? "/api/question-bank" : "/api/questions";
}

// GET /api/questions/{questionId}/solutions or /api/question-bank/{questionId}/solutions -- public,
// but shows the caller's own pending submission too when auth:true and they're logged in.
export function getSolutions(questionId, opts = {}, questionType = "paper") {
  return apiFetch(`${basePath(questionType)}/${questionId}/solutions`, { ...opts, auth: true });
}

// POST .../solutions -- starts unapproved; only the submitter sees it until an admin approves it
// from the moderation queue.
export function submitSolution(questionId, { title, solutionType, solutionText, imageUrl }, questionTypeFamily = "paper") {
  return apiFetch(`${basePath(questionTypeFamily)}/${questionId}/solutions`, {
    method: "POST",
    auth: true,
    body: { title, solutionType, solutionText, imageUrl: imageUrl || null },
  });
}

// POST /api/solutions/{id}/upvote -- same endpoint for both question types (upvote acts on the
// solution's own id, not the question's).
export function upvoteSolution(id) {
  return apiFetch(`/api/solutions/${id}/upvote`, { method: "POST", auth: true });
}
