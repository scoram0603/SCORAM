import { apiFetch, apiFetchForm } from "./client";

// GET /api/users/search?q=
export function searchUsers(q, { signal } = {}) {
  return apiFetch(`/api/users/search?q=${encodeURIComponent(q || "")}`, { auth: true, signal });
}

// GET /api/directmessages/conversations
export function listConversations({ signal } = {}) {
  return apiFetch("/api/directmessages/conversations", { auth: true, signal });
}

// POST /api/directmessages/conversations/start -- get-or-create a thread with this user
export function startConversation(otherUserId) {
  return apiFetch("/api/directmessages/conversations/start", {
    method: "POST",
    auth: true,
    body: { otherUserId },
  });
}

// GET /api/directmessages/conversations/{id}/messages?before=&pageSize=
export function getDirectMessages(conversationId, { before, pageSize = 30, signal } = {}) {
  const params = new URLSearchParams();
  if (before) params.set("before", before);
  params.set("pageSize", pageSize);
  return apiFetch(`/api/directmessages/conversations/${conversationId}/messages?${params.toString()}`, { auth: true, signal });
}

// POST /api/directmessages/conversations/{id}/messages -- multipart (Attachment is optional;
// attachmentDurationSeconds only applies to voice notes)
export function sendDirectMessage(conversationId, { messageText, attachment, attachmentDurationSeconds }) {
  const formData = new FormData();
  if (messageText) formData.append("MessageText", messageText);
  if (attachment) formData.append("Attachment", attachment);
  if (attachmentDurationSeconds) formData.append("AttachmentDurationSeconds", attachmentDurationSeconds);
  return apiFetchForm(`/api/directmessages/conversations/${conversationId}/messages`, { formData, auth: true });
}

export function markConversationRead(conversationId) {
  return apiFetch(`/api/directmessages/conversations/${conversationId}/read`, { method: "POST", auth: true });
}

// POST /api/directmessages/conversations/{id}/share-question -- re-share a Scoram Question Bank
// question into a DM.
export function shareQuestionToDm(conversationId, questionBankQuestionId) {
  return apiFetch(`/api/directmessages/conversations/${conversationId}/share-question`, {
    method: "POST",
    auth: true,
    body: { questionBankQuestionId },
  });
}

// DELETE /api/directmessages/messages/{id} -- "unsend", sender-only
export function deleteDirectMessage(messageId) {
  return apiFetch(`/api/directmessages/messages/${messageId}`, { method: "DELETE", auth: true });
}
