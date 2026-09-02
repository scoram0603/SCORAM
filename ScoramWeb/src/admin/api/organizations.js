import { apiFetch, apiFetchForm } from "../../api/client";

// ORGANIZATION HIERARCHY -- see ScoramAPI/Controllers/OrganizationsController.cs. Same shape as
// admin/api/exams.js for exactly the same reason Organization mirrors Exam's own model.

// GET /api/admin/organizations -- includes blocked organizations too, for the Manage
// Organizations page (the public GET /api/organizations, used by student-facing pickers, excludes
// them).
export function listAdminOrganizations(token) {
  return apiFetch("/api/admin/organizations", { token });
}

// POST /api/admin/organizations  (Admin only) -- "+ New Organization": name + optional logo file
export function createOrganization(token, { name, logoFile }) {
  const formData = new FormData();
  formData.append("Name", name);
  if (logoFile) formData.append("Logo", logoFile);

  return apiFetchForm("/api/admin/organizations", { formData, token });
}

// PATCH /api/admin/organizations/{id}  (Admin only) -- rename and/or replace the logo. Both optional.
export function updateOrganization(token, organizationId, { name, logoFile }) {
  const formData = new FormData();
  if (name != null) formData.append("Name", name);
  if (logoFile) formData.append("Logo", logoFile);

  return apiFetchForm(`/api/admin/organizations/${organizationId}`, { method: "PATCH", formData, token });
}

// PATCH /api/admin/organizations/{id}/block  (Admin only) -- hides the organization AND every exam
// under it from students, without touching each exam's own IsBlocked flag (see
// Organization.IsBlocked's own comment on the backend).
export function setOrganizationBlocked(token, organizationId, isBlocked) {
  return apiFetch(`/api/admin/organizations/${organizationId}/block`, { method: "PATCH", token, body: { isBlocked } });
}

// DELETE /api/admin/organizations/{id}  (SuperAdmin only) -- only succeeds if no exam is currently
// assigned to it; otherwise the server responds 409 suggesting reassignment or Block instead.
export function deleteOrganization(token, organizationId) {
  return apiFetch(`/api/admin/organizations/${organizationId}`, { method: "DELETE", token });
}
