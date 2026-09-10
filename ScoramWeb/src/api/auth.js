import { apiFetch, apiFetchForm } from "./client";

// POST /api/auth/register — see ScoramAPI/Controllers/AuthController.cs. otpAccessToken is the
// MSG91 widget's own access-token from having just OTP-verified phoneNumber (see msg91.js) --
// the backend re-verifies it server-side and cross-checks it against phoneNumber before creating
// the account.
export function register({ username, fullName, email, password, phoneNumber, otpAccessToken, referralCode }) {
  return apiFetch("/api/auth/register", {
    method: "POST",
    body: {
      username,
      fullName,
      email,
      password,
      phoneNumber,
      otpAccessToken,
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

// POST /api/auth/login-otp -- passwordless login for an existing account, once accessToken (from
// the MSG91 widget having just OTP-verified some phone number -- see msg91.js) is re-verified
// server-side. Fails with a clear "no account" message if the verified number isn't registered --
// see AuthController.LoginWithOtp's own comment on why this never falls back to creating one.
export function loginWithOtp({ accessToken }) {
  return apiFetch("/api/auth/login-otp", {
    method: "POST",
    body: { accessToken },
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

// GET /api/auth/me -- see MeResponseDto's comment in AuthDTOs.cs for why this exists
export function getMe() {
  return apiFetch("/api/auth/me", { auth: true });
}

// PATCH /api/auth/profile -- Full Name + Username (no password confirmation, unlike the
// change-email/phone/password calls below -- see the DTO's comment in AuthDTOs.cs).
export function updateProfile({ fullName, username }) {
  return apiFetch("/api/auth/profile", {
    method: "PATCH",
    auth: true,
    body: { fullName, username },
  });
}

// ---------- Settings: Account & Security ----------

// PATCH /api/auth/change-password
export function changePassword({ currentPassword, newPassword }) {
  return apiFetch("/api/auth/change-password", {
    method: "PATCH",
    auth: true,
    body: { currentPassword, newPassword },
  });
}

// PATCH /api/auth/change-email
export function changeEmail({ currentPassword, newEmail }) {
  return apiFetch("/api/auth/change-email", {
    method: "PATCH",
    auth: true,
    body: { currentPassword, newEmail },
  });
}

// PATCH /api/auth/change-phone -- otpAccessToken is the MSG91 widget's access-token from having
// just OTP-verified newPhoneNumber (see msg91.js); currentPassword proves account ownership, the
// OTP proves ownership of the number being switched to -- both are required, see ChangePhoneDto's
// own comment on the backend.
export function changePhone({ currentPassword, newPhoneNumber, otpAccessToken }) {
  return apiFetch("/api/auth/change-phone", {
    method: "PATCH",
    auth: true,
    body: { currentPassword, newPhoneNumber, otpAccessToken },
  });
}
