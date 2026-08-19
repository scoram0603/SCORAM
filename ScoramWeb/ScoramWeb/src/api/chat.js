import { apiFetch, apiFetchForm } from "./client";

// GET /api/chat/rooms
export function listChatRooms({ signal } = {}) {
  return apiFetch("/api/chat/rooms", { auth: true, signal });
}

export function joinChatRoom(roomId) {
  return apiFetch(`/api/chat/rooms/${roomId}/join`, { method: "POST", auth: true });
}

export function leaveChatRoom(roomId) {
  return apiFetch(`/api/chat/rooms/${roomId}/leave`, { method: "POST", auth: true });
}

// GET /api/chat/rooms/{id}/messages?before=&pageSize=
export function getChatMessages(roomId, { before, pageSize = 30, signal } = {}) {
  const params = new URLSearchParams();
  if (before) params.set("before", before);
  params.set("pageSize", pageSize);
  return apiFetch(`/api/chat/rooms/${roomId}/messages?${params.toString()}`, { auth: true, signal });
}

// POST /api/chat/rooms/{id}/messages -- multipart (Attachment is optional)
export function sendChatMessage(roomId, { messageText, attachment }) {
  const formData = new FormData();
  if (messageText) formData.append("MessageText", messageText);
  if (attachment) formData.append("Attachment", attachment);
  return apiFetchForm(`/api/chat/rooms/${roomId}/messages`, { formData, auth: true });
}

export function deleteOwnMessage(messageId) {
  return apiFetch(`/api/chat/messages/${messageId}`, { method: "DELETE", auth: true });
}

export function reportChatMessage(messageId, reason) {
  return apiFetch(`/api/chat/messages/${messageId}/report`, { method: "POST", auth: true, body: { reason } });
}

// GET /api/chat/rooms/{id}/mentionable-users?q= -- @mention autocomplete
export function getMentionableUsers(roomId, q, { signal } = {}) {
  return apiFetch(`/api/chat/rooms/${roomId}/mentionable-users?q=${encodeURIComponent(q || "")}`, { auth: true, signal });
}

export function getMyMentions({ unreadOnly = false, signal } = {}) {
  return apiFetch(`/api/chat/mentions?unreadOnly=${unreadOnly}`, { auth: true, signal });
}

export function markMentionRead(mentionId) {
  return apiFetch(`/api/chat/mentions/${mentionId}/read`, { method: "PATCH", auth: true });
}

export function markAllMentionsRead() {
  return apiFetch("/api/chat/mentions/read-all", { method: "PATCH", auth: true });
}

export function votePoll(pollId, optionIds) {
  return apiFetch(`/api/chat/polls/${pollId}/vote`, { method: "POST", auth: true, body: { optionIds } });
}
