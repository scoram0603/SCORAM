import { apiFetch, apiFetchForm } from "../../api/client";

// GET /api/exams -- public list, also used as the admin's exam picker (excludes blocked exams).
// organizationId (optional) scopes it to one Organization's exams.
export function listExams({ organizationId, signal } = {}) {
  const qs = organizationId ? `?organizationId=${organizationId}` : "";
  return apiFetch(`/api/exams${qs}`, { signal });
}

// GET /api/admin/exams -- same shape, but includes blocked exams too, for the Manage Exams page.
export function listAdminExams(token) {
  return apiFetch("/api/admin/exams", { token });
}

// POST /api/admin/exams  (Admin only) -- "+ New Exam": name + optional logo file + optional
// organizationId (ORGANIZATION HIERARCHY -- an exam can be created with no Organization and have
// one assigned later via updateExam).
export function createExam(token, { name, logoFile, organizationId }) {
  const formData = new FormData();
  formData.append("Name", name);
  if (logoFile) formData.append("Logo", logoFile);
  if (organizationId) formData.append("OrganizationId", organizationId);

  return apiFetchForm("/api/admin/exams", { formData, token });
}

// PATCH /api/admin/exams/{id}  (Admin only) -- rename and/or replace the logo and/or change which
// Organization the exam belongs to. All optional. clearOrganization removes the current
// Organization assignment -- needed as its own explicit flag rather than just omitting
// organizationId, since over multipart form data there's no way to distinguish "not provided,
// leave unchanged" from "explicitly cleared" for a plain id field (see ExamUpdateDto.
// ClearOrganization's own comment on the backend).
export function updateExam(token, examId, { name, logoFile, organizationId, clearOrganization }) {
  const formData = new FormData();
  if (name != null) formData.append("Name", name);
  if (logoFile) formData.append("Logo", logoFile);
  if (clearOrganization) formData.append("ClearOrganization", "true");
  else if (organizationId) formData.append("OrganizationId", organizationId);

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

// DELETE /api/admin/exams/{id}/empty-cleanup  (DeletePaper permission -- Admin or SuperAdmin) --
// narrower sibling of deleteExam above, reached only from BulkImportPanel's rollback flow after the
// admin confirms "this exam has nothing else on it -- delete it too?". Runs the exact same
// emptiness check server-side; only who's allowed to call it differs from deleteExam.
export function cleanupEmptyExam(token, examId) {
  return apiFetch(`/api/admin/exams/${examId}/empty-cleanup`, { method: "DELETE", token });
}
