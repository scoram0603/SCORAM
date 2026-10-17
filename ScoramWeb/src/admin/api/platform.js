import { apiFetch } from "../../api/client";

function qs(params = {}) {
  const q = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => {
    if (v !== undefined && v !== null && v !== "") q.set(k, v);
  });
  const s = q.toString();
  return s ? `?${s}` : "";
}

// ---- Users (ManageUsers) ---------------------------------------------------------------------------
export const listUsers = (token, params) => apiFetch(`/api/admin/users${qs(params)}`, { token });
export const getUser = (token, id) => apiFetch(`/api/admin/users/${id}`, { token });
export const setUserStatus = (token, id, isActive, reason) =>
  apiFetch(`/api/admin/users/${id}/status`, { method: "PATCH", token, body: { isActive, reason } });
export const getUserSessions = (token, id) => apiFetch(`/api/admin/users/${id}/sessions`, { token });
export const revokeUserSession = (token, id, sessionId) =>
  apiFetch(`/api/admin/users/${id}/sessions/${sessionId}/revoke`, { method: "POST", token });
export const revokeAllUserSessions = (token, id) =>
  apiFetch(`/api/admin/users/${id}/sessions/revoke-all`, { method: "POST", token });

// ---- Admin sessions (SuperAdmin) -------------------------------------------------------------------
export const listAdminSessions = (token) => apiFetch("/api/admin/sessions", { token });
export const revokeAdminSession = (token, id) => apiFetch(`/api/admin/sessions/${id}/revoke`, { method: "POST", token });
export const forceLogoutAdmin = (token, adminId) =>
  apiFetch(`/api/admin/sessions/admins/${adminId}/force-logout`, { method: "POST", token });

// ---- App configuration (ManageAppConfig) -----------------------------------------------------------
export const getAppConfig = (token) => apiFetch("/api/admin/app-config", { token });
export const updateAppConfig = (token, changes, note) =>
  apiFetch("/api/admin/app-config", { method: "PUT", token, body: { changes, note } });
export const listConfigRevisions = (token, params) => apiFetch(`/api/admin/app-config/revisions${qs(params)}`, { token });
export const rollbackConfigRevision = (token, id) =>
  apiFetch(`/api/admin/app-config/revisions/${id}/rollback`, { method: "POST", token });
export const getVersionImpact = (token, platform, version) =>
  apiFetch(`/api/admin/app-config/version-impact${qs({ platform, version })}`, { token });

// ---- Notifications center (PostNotices) ------------------------------------------------------------
export const sendAnnouncement = (token, body) => apiFetch("/api/admin/notifications/announce", { method: "POST", token, body });
export const previewAudience = (token, body) => apiFetch("/api/admin/notifications/audience-preview", { method: "POST", token, body });
export const findNotificationUsers = (token, q) => apiFetch(`/api/admin/notifications/find-users${qs({ q })}`, { token });
export const listCampaigns = (token, params) => apiFetch(`/api/admin/notifications/campaigns${qs(params)}`, { token });

// ---- Support tickets (ManageFeedback) --------------------------------------------------------------
export const listTickets = (token, params) => apiFetch(`/api/admin/support/tickets${qs(params)}`, { token });
export const getTicket = (token, id) => apiFetch(`/api/admin/support/tickets/${id}`, { token });
export const listSupportAgents = (token) => apiFetch("/api/admin/support/agents", { token });
export const updateTicket = (token, id, body) => apiFetch(`/api/admin/support/tickets/${id}`, { method: "PATCH", token, body });
export const addTicketNote = (token, id, body) =>
  apiFetch(`/api/admin/support/tickets/${id}/notes`, { method: "POST", token, body: { body } });
export const respondToTicket = (token, id, body) =>
  apiFetch(`/api/admin/support/tickets/${id}/respond`, { method: "POST", token, body: { body } });

// ---- System health (ViewAnalytics) -----------------------------------------------------------------
export const getSystemHealth = (token) => apiFetch("/api/admin/system-health", { token });
