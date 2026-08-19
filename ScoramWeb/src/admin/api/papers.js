import { apiFetch } from "../../api/client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// POST /api/admin/papers -- returns the paper on success (201-ish), or throws an ApiError with
// status 409 and the *existing* paper's data in err.data when this Exam+Year+Language+Tier+Date+
// Shift+PaperLabel+Code combination already exists. Callers should treat a 409 here as "resume this
// paper", not a failure.
export function createOrFindPaper(token, { examId, year, language, paperCode, tier, examDate, shift, paperLabel }) {
  return apiFetch("/api/admin/papers", {
    method: "POST",
    token,
    body: {
      examId, year, language,
      paperCode: paperCode || null,
      tier: tier || null,
      examDate: examDate || null,
      shift: shift || null,
      paperLabel: paperLabel || null,
    },
  });
}

// GET /api/admin/papers?status=&examId=&mine=&year=&language=&page=&pageSize=
export function listPapers(token, { status, examId, mine, year, language, tier, examDate, shift, paperLabel, page, pageSize } = {}) {
  return apiFetch(`/api/admin/papers${toQueryString({ status, examId, mine, year, language, tier, examDate, shift, paperLabel, page, pageSize })}`, { token });
}

// GET /api/admin/papers/pending  (Publish permission required server-side)
export function listPendingPapers(token) {
  return apiFetch("/api/admin/papers/pending", { token });
}

// GET /api/admin/papers/{id} -- { paper, questions }
export function getPaper(token, id) {
  return apiFetch(`/api/admin/papers/${id}`, { token });
}

export function submitPaper(token, id) {
  return apiFetch(`/api/admin/papers/${id}/submit`, { method: "PATCH", token });
}

export function publishPaper(token, id) {
  return apiFetch(`/api/admin/papers/${id}/publish`, { method: "PATCH", token });
}

export function rejectPaper(token, id, reason) {
  return apiFetch(`/api/admin/papers/${id}/reject`, { method: "PATCH", token, body: { reason } });
}

export function unpublishPaper(token, id) {
  return apiFetch(`/api/admin/papers/${id}/unpublish`, { method: "PATCH", token });
}

export function deletePaper(token, id) {
  return apiFetch(`/api/admin/papers/${id}`, { method: "DELETE", token });
}

// ---------- Previous Year Paper Practice ----------

// GET /api/admin/papers/{id}/mapped-questions -- FULL merged question list (legacy PYQ-upload +
// Question-Bank-mapped), each tagged with its source. What the admin question-mapping UI renders.
export function getMappedQuestions(token, id) {
  return apiFetch(`/api/admin/papers/${id}/mapped-questions`, { token });
}

// PATCH /api/admin/papers/{id}/config -- sets Duration/Negative marking/Required question count.
export function updatePaperConfig(token, id, { durationMinutes, negativeMarkingRatio, requiredQuestionCount }) {
  return apiFetch(`/api/admin/papers/${id}/config`, {
    method: "PATCH",
    token,
    body: { durationMinutes, negativeMarkingRatio, requiredQuestionCount },
  });
}

// POST /api/admin/papers/{id}/map-question -- map an EXISTING Question Bank question onto this
// paper at a given question number.
export function mapQuestionToPaper(token, id, { questionBankQuestionId, questionNumber }) {
  return apiFetch(`/api/admin/papers/${id}/map-question`, {
    method: "POST",
    token,
    body: { questionBankQuestionId, questionNumber },
  });
}

// DELETE /api/admin/papers/{id}/map-question/{linkId} -- unmap (never deletes the QB question itself).
export function unmapQuestionFromPaper(token, id, linkId) {
  return apiFetch(`/api/admin/papers/${id}/map-question/${linkId}`, { method: "DELETE", token });
}

// POST /api/admin/papers/{id}/map-questions-bulk -- add several existing Question Bank questions at
// once (e.g. everything already tagged for this paper's Exam+Year). Q.No are auto-assigned and
// flagged "approximate" server-side -- see PaperQuestionBankLink.IsNumberExact.
export function mapQuestionsBulkToPaper(token, id, questionBankQuestionIds) {
  return apiFetch(`/api/admin/papers/${id}/map-questions-bulk`, {
    method: "POST",
    token,
    body: { questionBankQuestionIds },
  });
}

// GET /api/admin/papers/{id}/validate -- completeness/duplicate/missing-number check.
export function validatePaper(token, id) {
  return apiFetch(`/api/admin/papers/${id}/validate`, { token });
}

// POST /api/admin/papers/reindex-search  (Super Admin only) -- rebuilds the Meilisearch index from
// every currently-Published paper. Needed once after this feature's first deploy, or any time the
// index and DB might have drifted (e.g. Meilisearch was down during a publish/unpublish).
export function reindexSearch(token) {
  return apiFetch("/api/admin/papers/reindex-search", { method: "POST", token });
}
