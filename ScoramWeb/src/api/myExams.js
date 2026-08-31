import { apiFetch } from "./client";

// "MY EXAMS" -- see Controllers/UserExamsController.cs. Every call here is per-student and
// requires auth, same as the rest of this file's shape (auth: true everywhere).

// GET /api/user/exams -- current selections. An empty `exams` array is the "not configured yet"
// signal MyExamsContext uses to decide onboarding vs. Home (see AppLayout.jsx).
export function getMyExams({ signal } = {}) {
  return apiFetch("/api/user/exams", { auth: true, signal });
}

// PUT /api/user/exams -- full replace. Used by onboarding ("Continue") and the My Exams management
// screen's "Save Changes". examIds: array of exam ids (min 1). primaryExamId: optional.
export function setMyExams({ examIds, primaryExamId }) {
  return apiFetch("/api/user/exams", {
    method: "PUT",
    auth: true,
    body: { examIds, primaryExamId: primaryExamId || null },
  });
}

// POST /api/user/exams/{examId} -- add a single exam ("+ Add Exam" on the management screen).
export function addMyExam(examId) {
  return apiFetch(`/api/user/exams/${examId}`, { method: "POST", auth: true });
}

// DELETE /api/user/exams/{examId} -- remove a single exam. Rejected by the backend if it's the
// student's only remaining exam.
export function removeMyExam(examId) {
  return apiFetch(`/api/user/exams/${examId}`, { method: "DELETE", auth: true });
}

// PATCH /api/user/exams/{examId}/primary -- "Set as Primary".
export function setPrimaryExam(examId) {
  return apiFetch(`/api/user/exams/${examId}/primary`, { method: "PATCH", auth: true });
}
