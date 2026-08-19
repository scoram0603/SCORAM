import { apiFetch } from "../../api/client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// GET /api/admin/audit-logs?page=&pageSize=&adminId=&action=  (Audit permission required server-side)
export function listAuditLogs(token, { page, pageSize, adminId, action } = {}) {
  return apiFetch(`/api/admin/audit-logs${toQueryString({ page, pageSize, adminId, action })}`, { token });
}
