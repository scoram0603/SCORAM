import { apiFetch } from "./client";

// POST /api/quiz-challenges { attemptId, challengedUserIds, challengedGroupId } -- turns a completed
// Quiz result into a challenge for one or more friends and/or every member of a Group Chat room at
// once. Returns a QuizChallengeBatchResultDto ({ batchId, challenges: [...], skippedCount }).
// Reuses api/directMessages.js's searchUsers() to pick friends and api/chat.js's listChatRooms() to
// pick a group -- no separate "find a student"/"find a group" endpoint needed.
export function createQuizChallenge(attemptId, { challengedUserIds = [], challengedGroupId } = {}) {
  return apiFetch("/api/quiz-challenges", {
    method: "POST",
    auth: true,
    body: { attemptId, challengedUserIds, challengedGroupId: challengedGroupId || null },
  });
}

// GET /api/quiz-challenges/batch/{batchId} -- every challenge sent together in one action (sender only).
export function getQuizChallengeBatch(batchId, opts = {}) {
  return apiFetch(`/api/quiz-challenges/batch/${batchId}`, { ...opts, auth: true });
}

// GET /api/quiz-challenges/by-attempt/{attemptId} -- the caller's own challenge(s) involving this
// specific attempt, whichever side they're on. Backs the "vs [opponent]" comparison card on a Quiz
// result page.
export function getChallengesByAttempt(attemptId, opts = {}) {
  return apiFetch(`/api/quiz-challenges/by-attempt/${attemptId}`, { ...opts, auth: true });
}

// GET /api/quiz-challenges/mine?direction=received|sent&status=
export function getMyQuizChallenges(direction, status, opts = {}) {
  const params = new URLSearchParams({ direction });
  if (status) params.set("status", status);
  return apiFetch(`/api/quiz-challenges/mine?${params.toString()}`, { ...opts, auth: true });
}

export function getQuizChallenge(id, opts = {}) {
  return apiFetch(`/api/quiz-challenges/${id}`, { ...opts, auth: true });
}

// POST /api/quiz-challenges/{id}/start -- returns a TestAttemptStartResponseDto, same as every
// other quiz-start call; navigate to /tests/attempt/:attemptId with the result.
export function startQuizChallenge(id) {
  return apiFetch(`/api/quiz-challenges/${id}/start`, { method: "POST", auth: true });
}

export function declineQuizChallenge(id) {
  return apiFetch(`/api/quiz-challenges/${id}/decline`, { method: "POST", auth: true });
}
