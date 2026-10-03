// Base URL for the ScoramAPI backend. Configure via .env (see .env.example) —
// defaults to the http dev profile's port if not set (see
// ScoramAPI/Properties/launchSettings.json — match http vs https + port exactly
// to whichever profile you're actually running, that mismatch is the #1 cause
// of "Couldn't reach the API" / CORS errors here).
export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || "http://localhost:5192";

const TOKEN_STORAGE_KEY = "scoram_token";
const ADMIN_TOKEN_STORAGE_KEY = "scoram_admin_token";
const REFRESH_TOKEN_STORAGE_KEY = "scoram_refresh_token";
const ADMIN_REFRESH_TOKEN_STORAGE_KEY = "scoram_admin_refresh_token";

export function getStoredToken() {
  try {
    return localStorage.getItem(TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

export function setStoredToken(token) {
  try {
    if (token) localStorage.setItem(TOKEN_STORAGE_KEY, token);
    else localStorage.removeItem(TOKEN_STORAGE_KEY);
  } catch {
    // localStorage unavailable (private browsing etc.) — auth simply won't persist across reloads
  }
}

// Access tokens are short-lived now (see appsettings.json's Jwt:ExpiryMinutes) -- these are what
// let a session survive past that without asking for a password again. See refreshStudentSession/
// refreshAdminSession below for where they're actually used, and RefreshToken.cs's own comment on
// why the server only ever sees a hash of one, never this raw value.
export function getStoredRefreshToken() {
  try {
    return localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

export function setStoredRefreshToken(token) {
  try {
    if (token) localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, token);
    else localStorage.removeItem(REFRESH_TOKEN_STORAGE_KEY);
  } catch {
    // ignore — same as above
  }
}

export function getStoredAdminRefreshToken() {
  try {
    return localStorage.getItem(ADMIN_REFRESH_TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

export function setStoredAdminRefreshToken(token) {
  try {
    if (token) localStorage.setItem(ADMIN_REFRESH_TOKEN_STORAGE_KEY, token);
    else localStorage.removeItem(ADMIN_REFRESH_TOKEN_STORAGE_KEY);
  } catch {
    // ignore — same as above
  }
}

// Admin sessions use a completely separate storage key from student sessions, so someone testing
// the /admin panel in the same browser as a logged-in student account (or vice versa) never has
// one login silently overwrite the other.
export function getStoredAdminToken() {
  try {
    return localStorage.getItem(ADMIN_TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

export function setStoredAdminToken(token) {
  try {
    if (token) localStorage.setItem(ADMIN_TOKEN_STORAGE_KEY, token);
    else localStorage.removeItem(ADMIN_TOKEN_STORAGE_KEY);
  } catch {
    // ignore — same as above
  }
}

// Fired once a silent refresh succeeds, so AuthContext/AdminAuthContext can update their own React
// `token` state to match -- without this, everything going through apiFetch/apiFetchForm would
// keep working fine (they always re-read the latest token from storage), but anything that reads
// the token straight from context state instead (SignalR's connection setup is the one today --
// see ChatConnectionContext) would keep using the old, now-dead access token until the next full
// page load.
const TOKEN_REFRESHED_EVENT = "scoram:token-refreshed";
const ADMIN_TOKEN_REFRESHED_EVENT = "scoram:admin-token-refreshed";

// Deduped per session type: a page can easily have several authenticated requests in flight at
// once (notifications poll, SignalR negotiate, the page's own data fetch), and if the access token
// expired they'll all come back 401 around the same time -- without this, each one would kick off
// its own refresh call and race to rotate the same refresh token, and only the first to land would
// actually work (see RefreshToken's rotation/reuse-detection comment: the others would get treated
// as replaying an already-used token).
let studentRefreshPromise = null;
let adminRefreshPromise = null;

async function refreshStudentSession() {
  if (!studentRefreshPromise) {
    studentRefreshPromise = (async () => {
      const refreshToken = getStoredRefreshToken();
      if (!refreshToken) return null;
      try {
        const res = await fetch(`${API_BASE_URL}/api/auth/refresh`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ refreshToken }),
        });
        if (!res.ok) return null;
        const data = await res.json();
        setStoredToken(data.token);
        setStoredRefreshToken(data.refreshToken);
        window.dispatchEvent(new CustomEvent(TOKEN_REFRESHED_EVENT, { detail: data.token }));
        return data.token;
      } catch {
        return null;
      } finally {
        studentRefreshPromise = null;
      }
    })();
  }
  return studentRefreshPromise;
}

async function refreshAdminSession() {
  if (!adminRefreshPromise) {
    adminRefreshPromise = (async () => {
      const refreshToken = getStoredAdminRefreshToken();
      if (!refreshToken) return null;
      try {
        const res = await fetch(`${API_BASE_URL}/api/admin/auth/refresh`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ refreshToken }),
        });
        if (!res.ok) return null;
        const data = await res.json();
        setStoredAdminToken(data.token);
        setStoredAdminRefreshToken(data.refreshToken);
        window.dispatchEvent(new CustomEvent(ADMIN_TOKEN_REFRESHED_EVENT, { detail: data.token }));
        return data.token;
      } catch {
        return null;
      } finally {
        adminRefreshPromise = null;
      }
    })();
  }
  return adminRefreshPromise;
}

/**
 * Thin fetch wrapper for the ScoramAPI backend.
 * - Prefixes API_BASE_URL
 * - Attaches JSON headers + Bearer token (when present)
 * - On a 401 with a token attached, tries exactly once to silently refresh (see
 *   refreshStudentSession/refreshAdminSession above) and retry before giving up -- so a merely-
 *   expired short-lived access token doesn't look like a dead session as long as the refresh token
 *   is still good.
 * - Throws an Error with a readable message on non-2xx responses
 * - Returns parsed JSON (or null for empty 204 responses)
 *
 * `auth: true` attaches the *student* token via getStoredToken(). Admin API calls pass an explicit
 * `token` (their own admin token) instead -- see src/admin/api/*.js.
 *
 * `optionalAuth: true` attaches the student token IF one is stored, and otherwise sends the request
 * anonymously. It is for public endpoints whose result depends on WHO is asking -- "MY EXAMS" scopes
 * PYP / Question Bank / Mock Tests / Practice templates / discussions to a signed-in student's own
 * exams, and the API can only do that if it receives the token. (Signed-out visitors are unchanged.)
 */
export async function apiFetch(path, { method = "GET", body, auth = false, optionalAuth = false, token, signal, _isRetry = false } = {}) {
  const headers = { "Content-Type": "application/json" };

  const isAdminCall = token !== undefined && token !== null;
  const resolvedToken = token ?? (auth || optionalAuth ? getStoredToken() : null);
  if (resolvedToken) headers.Authorization = `Bearer ${resolvedToken}`;

  let response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      method,
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
      signal,
    });
  } catch (networkError) {
    if (networkError.name === "AbortError") throw new ApiError(TIMEOUT_MESSAGE, 0, { timedOut: true });
    // Distinguish "backend isn't reachable" from a normal HTTP error, since this is the
    // most common failure mode during local development (API not running / wrong port / CORS).
    throw new ApiError(
      `Couldn't reach the Scoram API at ${API_BASE_URL}. Is the backend running? (${networkError.message})`,
      0
    );
  }

  if (response.status === 401 && resolvedToken && !_isRetry) {
    const newToken = isAdminCall ? await refreshAdminSession() : await refreshStudentSession();
    if (newToken) {
      return apiFetch(path, {
        method,
        body,
        auth,
        optionalAuth,
        token: isAdminCall ? newToken : undefined,
        signal,
        _isRetry: true,
      });
    }
  }

  return parseApiResponse(response, resolvedToken, isAdminCall);
}

// Shown when a request is aborted via an AbortController timeout (see withTimeoutSignal below) --
// deliberately does NOT say "failed" or "couldn't reach the API", because unlike a real network
// error, the request may well have reached the server and be completing in the background (ASP.NET
// keeps processing a request after the client gives up on it unless it explicitly checks
// RequestAborted). Telling the admin to just retry immediately is how a slow-but-successful bulk
// commit turns into a confusing duplicate attempt -- see QuestionBankUploadWizard's own comment.
const TIMEOUT_MESSAGE =
  "This is taking longer than usual. It may still be completing on the server -- please wait a moment, then check Recent Imports before trying again (retrying immediately can create a confusing duplicate attempt).";

// Combines a caller-provided AbortSignal (if any) with a timeout -- used for requests, like a bulk
// import commit, that can legitimately take a while (many rows / many images) but where an
// indefinitely-hanging fetch with no feedback is worse than telling the admin what's likely going
// on. Returns a plain AbortSignal, so it's a drop-in replacement for `signal` in any apiFetch*
// call above.
export function withTimeoutSignal(timeoutMs, existingSignal) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  existingSignal?.addEventListener("abort", () => controller.abort());
  // Not exposed to the caller today, but harmless to clear once the request settles if a future
  // caller wants to; left as a documented no-op hook rather than adding API surface prematurely.
  controller.signal.addEventListener("abort", () => clearTimeout(timer));
  return controller.signal;
}

/**
 * Same contract as apiFetch, but sends a FormData body (multipart/form-data) instead of JSON --
 * for the one endpoint that takes a file today: POST /api/admin/exams (exam logo upload).
 * Never set a Content-Type header yourself for this one; the browser sets the multipart boundary.
 */
export async function apiFetchForm(path, { method = "POST", formData, auth = false, token, signal, _isRetry = false } = {}) {
  const headers = {};
  const isAdminCall = token !== undefined && token !== null;
  const resolvedToken = token ?? (auth ? getStoredToken() : null);
  if (resolvedToken) headers.Authorization = `Bearer ${resolvedToken}`;

  let response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      method,
      headers,
      body: formData,
      signal,
    });
  } catch (networkError) {
    throw new ApiError(
      `Couldn't reach the Scoram API at ${API_BASE_URL}. Is the backend running? (${networkError.message})`,
      0
    );
  }

  if (response.status === 401 && resolvedToken && !_isRetry) {
    const newToken = isAdminCall ? await refreshAdminSession() : await refreshStudentSession();
    if (newToken) {
      return apiFetchForm(path, {
        method,
        formData,
        auth,
        token: isAdminCall ? newToken : undefined,
        signal,
        _isRetry: true,
      });
    }
  }

  return parseApiResponse(response, resolvedToken, isAdminCall);
}

// Fired at most once per bad token (see the guard in parseApiResponse below) so a page full of
// requests that all happen to be using the same stale token doesn't fire this a dozen times over --
// AuthContext/AdminAuthContext each listen for their own event and clear the *matching* session only,
// so a stale student token never logs an admin out, or vice versa.
const SESSION_EXPIRED_EVENT = "scoram:session-expired";
const ADMIN_SESSION_EXPIRED_EVENT = "scoram:admin-session-expired";
let studentExpiredEventFired = false;
let adminExpiredEventFired = false;

function notifyExpiredToken(isAdminToken) {
  if (isAdminToken) {
    if (adminExpiredEventFired) return;
    adminExpiredEventFired = true;
  } else {
    if (studentExpiredEventFired) return;
    studentExpiredEventFired = true;
  }
  window.dispatchEvent(new CustomEvent(isAdminToken ? ADMIN_SESSION_EXPIRED_EVENT : SESSION_EXPIRED_EVENT));
}

// Called once a fresh login/token is stored, so the NEXT time that token eventually goes bad,
// the expired-session event is allowed to fire again instead of staying silenced forever.
export function resetSessionExpiredGuard(isAdminToken) {
  if (isAdminToken) adminExpiredEventFired = false;
  else studentExpiredEventFired = false;
}

// Same "dead token" cleanup as a REST 401 above, for SignalR's own negotiate/connection failures
// -- those never go through apiFetch, so parseApiResponse's 401 handling above never sees them.
// See ChatConnectionContext.jsx's own comment on exactly when this fires.
export function notifyStudentSessionExpired() {
  notifyExpiredToken(false);
}

async function parseApiResponse(response, resolvedToken, isAdminToken) {
  if (response.status === 204) return null;

  const isJson = response.headers.get("content-type")?.includes("application/json");
  const data = isJson ? await response.json().catch(() => null) : null;

  if (!response.ok) {
    // A 401 on a request that DID carry a token means the token itself is the problem (expired /
    // invalid / signed with an old key) -- not "you're not logged in" (that's simply not attaching
    // a token in the first place, which is expected and not a session-expiry situation). Firing this
    // lets AuthContext/AdminAuthContext clear the dead session immediately instead of every other
    // authenticated call on the page silently failing and things like SignalR/notification polling
    // retrying against a token that will never start working again (see ChatConnectionContext's
    // withAutomaticReconnect -- without this, a expired token can pile up retries indefinitely).
    if (response.status === 401 && resolvedToken) notifyExpiredToken(isAdminToken);

    const message = data?.message || data?.title || `Request failed with status ${response.status}`;
    throw new ApiError(message, response.status, data);
  }

  return data;
}

export class ApiError extends Error {
  constructor(message, status, data) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.data = data;
  }
}


