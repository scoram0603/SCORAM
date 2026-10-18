import { apiFetch, apiFetchForm, API_BASE_URL, withTimeoutSignal } from "../../api/client";

// Bulk upload of Mock Tests / Practice Test templates. kind = "mock" | "practice".

// POST multipart -- parses + validates, writes nothing (except a PendingReview history row).
// Returns { token, jobId, kind, fileName, totalTests, validTests, invalidTests, totalNewQuestions,
// totalExistingQuestions, fileErrors[], tests[] }.
export function previewTestImport(token, kind, file) {
  const formData = new FormData();
  formData.append("file", file);
  return apiFetchForm(`/api/admin/test-import/${kind}/preview`, { formData, token });
}

// selectedIndexes omitted/empty = every valid test.
export function commitTestImport(token, previewToken, selectedIndexes) {
  return apiFetch("/api/admin/test-import/commit", {
    method: "POST",
    token,
    body: { token: previewToken, selectedIndexes: selectedIndexes?.length ? selectedIndexes : null },
    signal: withTimeoutSignal(2 * 60 * 1000),
  });
}

export function getTestImportHistory(token, kind) {
  return apiFetch(`/api/admin/test-import/history${kind ? `?kind=${kind}` : ""}`, { token });
}

export function getTestImportJob(token, jobId) {
  return apiFetch(`/api/admin/test-import/history/${jobId}`, { token });
}

export function rollbackTestImport(token, jobId) {
  return apiFetch(`/api/admin/test-import/history/${jobId}/rollback`, { method: "POST", token });
}

// Templates are generated server-side so they always match what the importer parses.
export async function downloadTestImportTemplate(token, kind, format) {
  const response = await fetch(`${API_BASE_URL}/api/admin/test-import/template/${kind}/${format}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  if (!response.ok) throw new Error(`Couldn't download the template (status ${response.status}).`);
  const blob = await response.blob();
  const disposition = response.headers.get("content-disposition") || "";
  const match = disposition.match(/filename="?([^"]+)"?/);
  const fileName = match?.[1] || `${kind}-template.${format}`;
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
