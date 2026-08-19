import { apiFetch } from "../../api/client";

// POST /api/admin/auth/login — see ScoramAPI/Controllers/AdminAuthController.cs
export function login({ email, password }) {
  return apiFetch("/api/admin/auth/login", {
    method: "POST",
    body: { email, password },
  });
}

// GET /api/admin/me/permissions -- the logged-in admin's own permissions (any admin can call this
// for themselves; used to decide what the UI shows).
export function getMyPermissions(token) {
  return apiFetch("/api/admin/me/permissions", { token });
}

// GET /api/admin/admins/{id}/permissions  (Super Admin only)
export function getAdminPermissions(token, id) {
  return apiFetch(`/api/admin/admins/${id}/permissions`, { token });
}

// PUT /api/admin/admins/{id}/permissions  (Super Admin only) -- replace-all
export function setAdminPermissions(token, id, permissions) {
  return apiFetch(`/api/admin/admins/${id}/permissions`, {
    method: "PUT",
    token,
    body: { permissions },
  });
}
// GET /api/admin/admins  (Super Admin only)
export function listAdmins(token) {
  return apiFetch("/api/admin/admins", { token });
}

// POST /api/admin/admins  (Super Admin only) -- create a new Admin or Super Admin account
export function createAdmin(token, { fullName, email, password, role }) {
  return apiFetch("/api/admin/admins", {
    method: "POST",
    token,
    body: { fullName, email, password, role },
  });
}

// PATCH /api/admin/admins/{id}/status  (Super Admin only) -- activate/deactivate
export function setAdminStatus(token, id, isActive) {
  return apiFetch(`/api/admin/admins/${id}/status`, {
    method: "PATCH",
    token,
    body: { isActive },
  });
}
