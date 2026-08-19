import { apiFetch } from "../../api/client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// GET /api/admin/reports/pending?page=&pageSize=
export function listPendingReports(token, { page, pageSize } = {}) {
  return apiFetch(`/api/admin/reports/pending${toQueryString({ page, pageSize })}`, { token });
}

// PATCH /api/admin/reports/{id}/status  { status: "UnderReview" | "Resolved" | "Rejected" }
export function updateReportStatus(token, id, status) {
  return apiFetch(`/api/admin/reports/${id}/status`, { method: "PATCH", token, body: { status } });
}
