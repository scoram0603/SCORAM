import { apiFetch } from "./client";

// GET /api/quizzes/weak-topics/preview -- "Your weak areas: Reasoning (42%), Quant (58%)" before
// the student commits to starting. Empty array for a student with no graded history yet.
export function previewWeakTopics(opts = {}) {
  return apiFetch("/api/quizzes/weak-topics/preview", { ...opts, auth: true });
}

// POST /api/quizzes/weak-topics/generate -- returns a TestAttemptStartResponseDto, same shape as
// startPaper/generatePracticeTest, so the caller navigates straight to /tests/attempt/:attemptId
// and TestRunner handles the rest with zero Quiz-specific UI needed.
export function generateWeakTopicsQuiz(questionCount) {
  return apiFetch("/api/quizzes/weak-topics/generate", {
    method: "POST",
    auth: true,
    body: { questionCount },
  });
}

// GET /api/quizzes/daily -- every currently-Live/Upcoming admin-curated quiz (Phase 2). No auth
// needed to browse; MyAttemptCount only comes back populated when signed in.
export function listDailyQuizzes(opts = {}) {
  return apiFetch("/api/quizzes/daily", opts);
}

// POST /api/quizzes/{id}/start -- resumes an in-progress attempt if one exists, otherwise starts a
// fresh one. Same TestAttemptStartResponseDto shape as everything else.
export function startDailyQuiz(id) {
  return apiFetch(`/api/quizzes/${id}/start`, { method: "POST", auth: true });
}
