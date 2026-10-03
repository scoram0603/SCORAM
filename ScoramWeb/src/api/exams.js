import { apiFetch } from "./client";

// GET /api/exams -- public list, same endpoint the admin picker uses. organizationId (optional)
// scopes it to one Organization's exams -- see api/organizations.js's own header comment.
// search (optional) is matched SERVER-side, case-insensitively, against the exam and organization
// name -- used by the My Exams picker so searching never needs the whole catalog on the client.
export function listExams({ organizationId, search, signal } = {}) {
  const params = new URLSearchParams();
  if (organizationId) params.set("organizationId", organizationId);
  if (search && search.trim()) params.set("search", search.trim());
  const qs = params.toString();
  return apiFetch(`/api/exams${qs ? `?${qs}` : ""}`, { signal });
}
