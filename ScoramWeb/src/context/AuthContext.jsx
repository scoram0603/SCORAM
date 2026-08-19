import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import * as authApi from "../api/auth";
import { getStoredToken, setStoredToken, resetSessionExpiredGuard } from "../api/client";

const USER_STORAGE_KEY = "scoram_user";

const AuthContext = createContext(null);

function readStoredUser() {
  try {
    const raw = localStorage.getItem(USER_STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function writeStoredUser(user) {
  try {
    if (user) localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));
    else localStorage.removeItem(USER_STORAGE_KEY);
  } catch {
    // ignore — auth just won't persist across reloads
  }
}

export function AuthProvider({ children }) {
  const [user, setUser] = useState(() => readStoredUser());
  const [token, setToken] = useState(() => getStoredToken());
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState(null);
  // Set only when a session was cleared *because the server rejected the token* (expired/invalid),
  // not on a normal manual logout -- so the login screen can show "please log in again" instead of
  // silently landing there with no explanation.
  const [sessionExpired, setSessionExpired] = useState(false);

  // Keep localStorage in sync whenever user/token state changes
  useEffect(() => {
    writeStoredUser(user);
  }, [user]);

  useEffect(() => {
    setStoredToken(token);
  }, [token]);

  // See api/client.js: fires at most once per bad token, the moment ANY authenticated request comes
  // back 401 with that token attached. Without this, every other authenticated call on the page
  // (notifications poll, SignalR negotiate/reconnect, etc.) just keeps failing against a token that
  // will never start working again until a real re-login happens.
  useEffect(() => {
    function handleExpired() {
      setToken(null);
      setUser(null);
      setSessionExpired(true);
    }
    window.addEventListener("scoram:session-expired", handleExpired);
    return () => window.removeEventListener("scoram:session-expired", handleExpired);
  }, []);

  const applyAuthResponse = useCallback((res) => {
    resetSessionExpiredGuard(false);
    setSessionExpired(false);
    setToken(res.token);
    setUser({
      userId: res.userId,
      username: res.username,
      fullName: res.fullName,
      email: res.email,
      photoUrl: res.photoUrl ?? null,
      notifyOnGroupMessages: res.notifyOnGroupMessages,
      notifyOnDirectMessages: res.notifyOnDirectMessages,
    });
  }, []);

  const login = useCallback(
    async (credentials) => {
      setIsLoading(true);
      setError(null);
      try {
        const res = await authApi.login(credentials);
        applyAuthResponse(res);
        return res;
      } catch (err) {
        setError(err.message);
        throw err;
      } finally {
        setIsLoading(false);
      }
    },
    [applyAuthResponse]
  );

  const register = useCallback(
    async (details) => {
      setIsLoading(true);
      setError(null);
      try {
        const res = await authApi.register(details);
        applyAuthResponse(res);
        return res;
      } catch (err) {
        setError(err.message);
        throw err;
      } finally {
        setIsLoading(false);
      }
    },
    [applyAuthResponse]
  );

  const logout = useCallback(() => {
    setToken(null);
    setUser(null);
    setSessionExpired(false);
  }, []);

  const updateNotificationPreferences = useCallback(async (prefs) => {
    const updated = await authApi.updateNotificationPreferences(prefs);
    setUser((prev) => (prev ? { ...prev, ...updated } : prev));
    return updated;
  }, []);

  const updateProfilePhoto = useCallback(async (file) => {
    const res = await authApi.uploadProfilePhoto(file);
    setUser((prev) => (prev ? { ...prev, photoUrl: res.photoUrl } : prev));
    return res;
  }, []);

  const removeProfilePhoto = useCallback(async () => {
    const res = await authApi.removeProfilePhoto();
    setUser((prev) => (prev ? { ...prev, photoUrl: res.photoUrl } : prev));
    return res;
  }, []);

  const value = useMemo(
    () => ({
      user,
      token,
      isAuthenticated: Boolean(token && user),
      isLoading,
      error,
      sessionExpired,
      login,
      register,
      logout,
      updateNotificationPreferences,
      updateProfilePhoto,
      removeProfilePhoto,
      clearError: () => setError(null),
    }),
    [user, token, isLoading, error, sessionExpired, login, register, logout, updateNotificationPreferences, updateProfilePhoto, removeProfilePhoto]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within an AuthProvider");
  return ctx;
}
