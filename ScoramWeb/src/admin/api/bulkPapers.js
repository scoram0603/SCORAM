import { apiFetch, apiFetchForm } from "../../api/client";

// POST /api/admin/bulk-papers/preview  (UploadPaper permission) -- parses a CSV/Excel file of
// paper shells (one row per paper: ExamName, Year, Tier, Shift, Date, Medium, PaperCode,
// PaperLabel -- only ExamName/Year/Medium are required) and returns a preview with each row's
// validity, whether its exam already exists or will be created new, and whether an identical
// paper already exists (in which case Commit skips it). Nothing is created yet.
export function previewBulkPapers(token, file) {
  const formData = new FormData();
  formData.append("file", file);
  return apiFetchForm("/api/admin/bulk-papers/preview", { formData, token });
}

// POST /api/admin/bulk-papers/{jobId}/commit  (UploadPaper permission) -- creates a Draft Paper
// for every valid, not-already-existing row (or just the given rowNumbers, if provided). Returns
// { createdCount, skippedExistingCount, createdPapers } directly, OR (when the backend has a
// Redis-backed background queue configured) a 202 with an empty/placeholder body -- in that case,
// poll getBulkPapersCommitStatus(jobId) below until its status moves past "Processing".
export function commitBulkPapers(token, jobId, rowNumbers) {
  return apiFetch(`/api/admin/bulk-papers/${jobId}/commit`, {
    method: "POST",
    token,
    body: rowNumbers ? { rowNumbers } : {}
  });
}

// GET /api/admin/bulk-papers/{jobId}/commit-status -- only meaningful after a 202 from Commit
// above. Returns { status: "Processing"|"Committed"|"Failed", result, errorMessage }. Unlike
// BulkImportController's GetStatus, this is a purpose-built endpoint rather than reusing an
// existing job-status one -- BulkPaperImportController has no DB job row to poll (see
// BulkPaperImportCommitJob's own backend comment on why).
export function getBulkPapersCommitStatus(token, jobId) {
  return apiFetch(`/api/admin/bulk-papers/${jobId}/commit-status`, { token });
}
