import { apiFetch, apiFetchForm } from "../../api/client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// POST /api/admin/papers/{paperId}/bulk-import/preview -- parses + validates, writes nothing to
// Questions yet. Returns { jobId, fileName, format, totalRows, validCount, invalidCount, rows }.
export function previewBulkImport(token, paperId, file) {
  const formData = new FormData();
  formData.append("file", file);
  return apiFetchForm(`/api/admin/papers/${paperId}/bulk-import/preview`, { formData, token });
}

// POST /api/admin/bulk-import/{jobId}/commit -- rowNumbers omitted = commit every valid row;
// pass a subset for a partial import.
export function commitBulkImport(token, jobId, rowNumbers) {
  return apiFetch(`/api/admin/bulk-import/${jobId}/commit`, {
    method: "POST",
    token,
    body: { rowNumbers: rowNumbers || null },
  });
}

// GET /api/admin/bulk-import/history?paperId=&page=&pageSize=
export function getImportHistory(token, { paperId, page, pageSize } = {}) {
  return apiFetch(`/api/admin/bulk-import/history${toQueryString({ paperId, page, pageSize })}`, { token });
}

// POST /api/admin/bulk-import/{jobId}/rollback -- only works while the paper is still Draft
export function rollbackImport(token, jobId) {
  return apiFetch(`/api/admin/bulk-import/${jobId}/rollback`, { method: "POST", token });
}
