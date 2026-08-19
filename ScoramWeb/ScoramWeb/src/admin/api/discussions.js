import { apiFetch } from "../../api/client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// GET /api/admin/comment-reports/pending?page=&pageSize=  (ModerateDiscussions permission required)
export function listPendingCommentReports(token, { page, pageSize } = {}) {
  return apiFetch(`/api/admin/comment-reports/pending${toQueryString({ page, pageSize })}`, { token });
}

// PATCH /api/admin/comment-reports/{reportId}/dismiss -- report was unfounded, comment stays as-is
export function dismissCommentReport(token, reportId) {
  return apiFetch(`/api/admin/comment-reports/${reportId}/dismiss`, { method: "PATCH", token });
}

// DELETE /api/admin/comment-reports/{reportId}/remove-comment -- report was valid, removes the
// comment (and any replies under it)
export function removeReportedComment(token, reportId) {
  return apiFetch(`/api/admin/comment-reports/${reportId}/remove-comment`, { method: "DELETE", token });
}

// POST /api/admin/questions/{questionId}/comments/{parentCommentId?} -- an official admin reply
export function createCommentAsAdmin(token, questionId, commentText, parentCommentId) {
  const path = parentCommentId
    ? `/api/admin/questions/${questionId}/comments/${parentCommentId}`
    : `/api/admin/questions/${questionId}/comments`;
  return apiFetch(path, { method: "POST", token, body: { commentText } });
}
