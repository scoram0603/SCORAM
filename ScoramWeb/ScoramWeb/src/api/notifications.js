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
