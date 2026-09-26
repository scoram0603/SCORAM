import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import * as adminAuthApi from "../api/adminAuth";
import { getStoredAdminToken, setStoredAdminToken, setStoredAdminRefreshToken, resetSessionExpiredGuard } from "../../api/client";

const ADMIN_USER_STORAGE_KEY = "scoram_admin_user";

const AdminAuthContext = createContext(null);

function readStoredAdminUser() {
  try {
    const raw = localStorage.getItem(ADMIN_USER_STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function writeStoredAdminUser(user) {
  try {
    if (user) localStorage.setItem(ADMIN_USER_STORAGE_KEY, JSON.stringify(user));
    else localStorage.removeItem(ADMIN_USER_STORAGE_KEY);
  } catch {
    // ignore — admin session just won't persist across reloads
  }
}

export function AdminAuthProvider({ children }) {
  const [admin, setAdmin] = useState(() => readStoredAdminUser());
  const [token, setToken] = useState(() => getStoredAdminToken());
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState(null);
  const [sessionExpired, setSessionExpired] = useState(false);

  useEffect(() => {
    writeStoredAdminUser(admin);
  }, [admin]);

  useEffect(() => {
    setStoredAdminToken(token);
  }, [token]);

  // See api/client.js -- fires once the moment any admin-authenticated call comes back 401 with
  // this token attached (expired/invalid), so a stale admin session doesn't sit there silently
  // failing every subsequent request until someone notices and manually logs back in.
  useEffect(() => {
    function handleExpired() {
      setStoredAdminRefreshToken(null);
      setToken(null);
      setAdmin(null);
      setSessionExpired(true);
    }
    window.addEventListener("scoram:admin-session-expired", handleExpired);
    return () => window.removeEventListener("scoram:admin-session-expired", handleExpired);
  }, []);

  // See the matching comment in ../../context/AuthContext.jsx -- same mechanism, admin session.
  useEffect(() => {
    function handleRefreshed(event) {
      setToken(event.detail);
    }
    window.addEventListener("scoram:admin-token-refreshed", handleRefreshed);
    return () => window.removeEventListener("scoram:admin-token-refreshed", handleRefreshed);
  }, []);

  // Shared by login() and verifyMfaLogin() -- the point where a login attempt has actually
  // succeeded and produced a real token pair, whether that took one step (no MFA) or two
  // (password, then a code). Never called with an mfaRequired response -- see login() below.
  const applyLoginSuccess = useCallback(async (res) => {
    resetSessionExpiredGuard(true);
    setSessionExpired(false);
    setToken(res.token);
    setStoredAdminRefreshToken(res.refreshToken);

    let permissions = [];
    try {
      const permRes = await adminAuthApi.getMyPermissions(res.token);
      permissions = permRes.permissions;
    } catch {
      // Non-fatal -- worst case the UI under-shows sections until a reload; the backend still
      // enforces permissions independently of whatever the frontend thinks it knows.
    }

    setAdmin({
      adminId: res.adminId,
      fullName: res.fullName,
      email: res.email,
      role: res.role, // "Admin" | "SuperAdmin"
      permissions,
      // True right after SuperAdminBootstrapService creates the first SuperAdmin, or for a
      // pre-existing account the RemoveDefaultSuperAdminSeed migration flagged -- AdminLogin.jsx
      // and AdminProtectedRoute both read this to route straight to the forced change-password
      // screen. The server enforces the restriction regardless (see MustChangePasswordFilter), so
      // this is a UX nicety on top of that, not the actual security boundary.
      mustChangePassword: res.mustChangePassword,
    });
  }, []);

  const login = useCallback(async (credentials) => {
    setIsLoading(true);
    setError(null);
    try {
      const res = await adminAuthApi.login(credentials);
      // mfaRequired: true means the password check passed but there's no real session yet (see
      // AdminAuthResponseDto's own comment -- Token/RefreshToken are both empty in this case).
      // Returned as-is so AdminLogin.jsx can show the code-entry step instead of navigating away.
      if (!res.mfaRequired) await applyLoginSuccess(res);
      return res;
    } catch (err) {
      setError(err.message);
      throw err;
    } finally {
      setIsLoading(false);
    }
  }, [applyLoginSuccess]);

  // Second step of an MFA login -- see login() above and AdminLogin.jsx for where this is called
  // from (after the admin enters a code in response to mfaRequired: true).
  const verifyMfaLogin = useCallback(async ({ mfaChallengeToken, code }) => {
    setIsLoading(true);
    setError(null);
    try {
      const res = await adminAuthApi.verifyMfaLogin({ mfaChallengeToken, code });
      await applyLoginSuccess(res);
      return res;
    } catch (err) {
      setError(err.message);
      throw err;
    } finally {
      setIsLoading(false);
    }
  }, [applyLoginSuccess]);

  const logout = useCallback(() => {
    // Best-effort, same reasoning as the student-side logout in ../../context/AuthContext.jsx.
    if (token) adminAuthApi.logout(token).catch(() => {});
    setStoredAdminRefreshToken(null);
    setToken(null);
    setAdmin(null);
    setSessionExpired(false);
  }, [token]);

  const refreshPermissions = useCallback(async () => {
    if (!token) return;
    try {
      const permRes = await adminAuthApi.getMyPermissions(token);
      setAdmin((a) => (a ? { ...a, permissions: permRes.permissions } : a));
    } catch {
      // Leave whatever's cached -- a stale permission list in the UI is a display-layer
      // inconvenience at worst, since the backend re-checks independently on every action.
    }
  }, [token]);

  // Re-sync once per app load so a permission change made elsewhere (e.g. a Super Admin editing
  // this admin's access in another tab) doesn't stay invisible for the whole session.
  useEffect(() => {
    if (token && admin) refreshPermissions();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const hasPermission = useCallback(
    (permission) => admin?.role === "SuperAdmin" || Boolean(admin?.permissions?.includes(permission)),
    [admin]
  );

  const value = useMemo(
    () => ({
      admin,
      token,
      isAuthenticated: Boolean(token && admin),
      isSuperAdmin: admin?.role === "SuperAdmin",
      hasPermission,
      refreshPermissions,
      isLoading,
      error,
      sessionExpired,
      login,
      verifyMfaLogin,
      logout,
      clearError: () => setError(null),
    }),
    [admin, token, isLoading, error, sessionExpired, login, verifyMfaLogin, logout, hasPermission, refreshPermissions]
  );

  return <AdminAuthContext.Provider value={value}>{children}</AdminAuthContext.Provider>;
}

export function useAdminAuth() {
  const ctx = useContext(AdminAuthContext);
  if (!ctx) throw new Error("useAdminAuth must be used within an AdminAuthProvider");
  return ctx;
}
