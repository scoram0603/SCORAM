import { apiFetch, apiFetchForm } from "./client";

// POST /api/auth/register — see ScoramAPI/Controllers/AuthController.cs
export function register({ username, fullName, email, password, phoneNumber, referralCode }) {
  return apiFetch("/api/auth/register", {
    method: "POST",
    body: {
      username,
      fullName,
      email,
      password,
      phoneNumber,
      referralCode: referralCode || null,
    },
  });
}

// POST /api/auth/login -- identifier is either an email or a username
export function login({ identifier, password }) {
  return apiFetch("/api/auth/login", {
    method: "POST",
    body: { identifier, password },
  });
}

// GET /api/auth/check-username?username=... -- live availability check while typing
export function checkUsername(username, { signal } = {}) {
  return apiFetch(`/api/auth/check-username?username=${encodeURIComponent(username)}`, { signal });
}

// PATCH /api/auth/notification-preferences -- the two global mute switches
export function updateNotificationPreferences({ notifyOnGroupMessages, notifyOnDirectMessages }) {
  return apiFetch("/api/auth/notification-preferences", {
    method: "PATCH",
    auth: true,
    body: { notifyOnGroupMessages, notifyOnDirectMessages },
  });
}

// POST /api/auth/profile-photo -- sets/replaces the student's own avatar
export function uploadProfilePhoto(file) {
  const formData = new FormData();
  formData.append("file", file);
  return apiFetchForm("/api/auth/profile-photo", { formData, auth: true });
}

// DELETE /api/auth/profile-photo -- reverts to the initials avatar
export function removeProfilePhoto() {
  return apiFetch("/api/auth/profile-photo", { method: "DELETE", auth: true });
}
