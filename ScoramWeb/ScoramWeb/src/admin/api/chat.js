import { apiFetch } from "../../api/client";

export function listAdminChatRooms(token) {
  return apiFetch("/api/admin/chat/rooms", { token });
}

export function toggleChatLock(token, roomId, disabled) {
  return apiFetch(`/api/admin/chat/rooms/${roomId}/lock?disabled=${disabled}`, { method: "PATCH", token });
}

export function listRoomMembers(token, roomId) {
  return apiFetch(`/api/admin/chat/rooms/${roomId}/members`, { token });
}

export function removeRoomMember(token, roomId, userId) {
  return apiFetch(`/api/admin/chat/rooms/${roomId}/members/${userId}`, { method: "DELETE", token });
}

export function postNotice(token, roomId, messageText) {
  return apiFetch(`/api/admin/chat/rooms/${roomId}/notices`, { method: "POST", token, body: { messageText } });
}

export function createPoll(token, roomId, { question, options, allowMultipleChoices }) {
  return apiFetch(`/api/admin/chat/rooms/${roomId}/polls`, {
    method: "POST",
    token,
    body: { question, options, allowMultipleChoices },
  });
}

export function closePoll(token, pollId) {
  return apiFetch(`/api/admin/chat/polls/${pollId}/close`, { method: "PATCH", token });
}

export function listChatReports(token, status) {
  return apiFetch(`/api/admin/chat/reports${status ? `?status=${status}` : ""}`, { token });
}

export function resolveChatReport(token, reportId, { status, resolutionNote, deleteMessage }) {
  return apiFetch(`/api/admin/chat/reports/${reportId}/resolve`, {
    method: "PATCH",
    token,
    body: { status, resolutionNote: resolutionNote || null, deleteMessage: Boolean(deleteMessage) },
  });
}

export function listBannedWords(token) {
  return apiFetch("/api/admin/chat/banned-words", { token });
}

export function addBannedWord(token, word) {
  return apiFetch("/api/admin/chat/banned-words", { method: "POST", token, body: { word } });
}

export function removeBannedWord(token, id) {
  return apiFetch(`/api/admin/chat/banned-words/${id}`, { method: "DELETE", token });
}

// POST /api/admin/chat/sync-rooms  (Super Admin only) -- backfills chat rooms for exams created
// before this feature existed.
export function syncChatRooms(token) {
  return apiFetch("/api/admin/chat/sync-rooms", { method: "POST", token });
}
