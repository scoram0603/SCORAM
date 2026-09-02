import { apiFetch } from "./client";

// GET /api/exams -- public list, same endpoint the admin picker uses. organizationId (optional)
// scopes it to one Organization's exams -- see api/organizations.js's own header comment.
export function listExams({ organizationId, signal } = {}) {
  const qs = organizationId ? `?organizationId=${organizationId}` : "";
  return apiFetch(`/api/exams${qs}`, { signal });
}
