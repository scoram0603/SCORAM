import { apiFetch } from "./client";

export function listNotifications({ page = 1, pageSize = 20, signal } = {}) {
  return apiFetch(`/api/notifications?page=${page}&pageSize=${pageSize}`, { auth: true, signal });
}

export function getUnreadCount({ signal } = {}) {
  return apiFetch("/api/notifications/unread-count", { auth: true, signal });
}

export function markNotificationRead(id) {
  return apiFetch(`/api/notifications/${id}/read`, { method: "POST", auth: true });
}

export function markAllNotificationsRead() {
  return apiFetch("/api/notifications/read-all", { method: "POST", auth: true });
}

// One notification by id. The server only returns the caller's own rows (404 otherwise, whether the
// id is missing, deleted or belongs to somebody else).
export function getNotification(id, { signal } = {}) {
  return apiFetch(`/api/notifications/${id}`, { auth: true, signal });
}
