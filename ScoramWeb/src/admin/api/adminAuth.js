import { apiFetch } from "../../api/client";

// POST /api/admin/auth/login — see ScoramAPI/Controllers/AdminAuthController.cs
export function login({ email, password }) {
  return apiFetch("/api/admin/auth/login", {
    method: "POST",
    body: { email, password },
  });
}

// POST /api/admin/auth/mfa/verify-login -- completes a login that came back with mfaRequired:true.
// `code` is either a 6-digit authenticator code or one of the admin's backup codes.
export function verifyMfaLogin({ mfaChallengeToken, code }) {
  return apiFetch("/api/admin/auth/mfa/verify-login", {
    method: "POST",
    body: { mfaChallengeToken, code },
  });
}

// POST /api/admin/auth/mfa/setup -- (re)starts MFA setup for the calling admin. Returns the secret,
// a provisioning URI (for a QR code or manual entry), and backup codes shown exactly once.
export function setupMfa(token) {
  return apiFetch("/api/admin/auth/mfa/setup", { method: "POST", token });
}

// POST /api/admin/auth/mfa/enable -- confirms setup with a code from the now-configured
// authenticator app; MFA isn't actually required at login until this succeeds.
export function enableMfa(token, code) {
  return apiFetch("/api/admin/auth/mfa/enable", { method: "POST", token, body: { code } });
}

// POST /api/admin/auth/mfa/disable -- requires both the current password and a valid code.
export function disableMfa(token, { currentPassword, code }) {
  return apiFetch("/api/admin/auth/mfa/disable", { method: "POST", token, body: { currentPassword, code } });
}

// PATCH /api/admin/auth/change-password -- reachable even while MustChangePasswordFilter is
// blocking everything else for this account (see AdminChangePassword.jsx). Success means the
// backend has already invalidated the current token server-side (SecurityStamp regenerated), so
// the caller should log out and send the admin back to /admin/login rather than trying to keep
// using the session that was just superseded.
export function changePassword(token, { currentPassword, newPassword }) {
  return apiFetch("/api/admin/auth/change-password", {
    method: "PATCH",
    token,
    body: { currentPassword, newPassword },
  });
}

// POST /api/admin/auth/logout -- see the student-side logout() in ../../api/auth.js for the same
// "ends every session server-side, but the caller clears local state regardless" reasoning.
export function logout(token) {
  return apiFetch("/api/admin/auth/logout", { method: "POST", token });
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
