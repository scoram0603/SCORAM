import { apiFetch, apiFetchForm } from "../../api/client";

// GET /api/exams -- public list, also used as the admin's exam picker (excludes blocked exams)
export function listExams({ signal } = {}) {
  return apiFetch("/api/exams", { signal });
}

// GET /api/admin/exams -- same shape, but includes blocked exams too, for the Manage Exams page.
export function listAdminExams(token) {
  return apiFetch("/api/admin/exams", { token });
}

// POST /api/admin/exams  (Admin only) -- "+ New Exam": name + optional logo file
export function createExam(token, { name, logoFile }) {
  const formData = new FormData();
  formData.append("Name", name);
  if (logoFile) formData.append("Logo", logoFile);

  return apiFetchForm("/api/admin/exams", { formData, token });
}

// PATCH /api/admin/exams/{id}  (Admin only) -- rename and/or replace the logo. Both optional.
export function updateExam(token, examId, { name, logoFile }) {
  const formData = new FormData();
  if (name != null) formData.append("Name", name);
  if (logoFile) formData.append("Logo", logoFile);

  return apiFetchForm(`/api/admin/exams/${examId}`, { method: "PATCH", formData, token });
}

// PATCH /api/admin/exams/{id}/block  (Admin only) -- hide/unhide from students.
export function setExamBlocked(token, examId, isBlocked) {
  return apiFetch(`/api/admin/exams/${examId}/block`, { method: "PATCH", token, body: { isBlocked } });
}

// DELETE /api/admin/exams/{id}  (SuperAdmin only) -- only succeeds if the exam has no content
// attached; otherwise the server responds 409 suggesting Block instead.
export function deleteExam(token, examId) {
  return apiFetch(`/api/admin/exams/${examId}`, { method: "DELETE", token });
}
