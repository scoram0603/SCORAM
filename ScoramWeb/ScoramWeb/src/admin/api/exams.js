import { apiFetch, apiFetchForm } from "../../api/client";

// GET /api/exams -- public list, also used as the admin's exam picker
export function listExams({ signal } = {}) {
  return apiFetch("/api/exams", { signal });
}

// POST /api/admin/exams  (Admin only) -- "+ New Exam": name + optional logo file
export function createExam(token, { name, logoFile }) {
  const formData = new FormData();
  formData.append("Name", name);
  if (logoFile) formData.append("Logo", logoFile);

  return apiFetchForm("/api/admin/exams", { formData, token });
}
