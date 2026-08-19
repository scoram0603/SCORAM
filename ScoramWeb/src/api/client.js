// Base URL for the ScoramAPI backend. Configure via .env (see .env.example) —
// defaults to the http dev profile's port if not set (see
// ScoramAPI/Properties/launchSettings.json — match http vs https + port exactly
// to whichever profile you're actually running, that mismatch is the #1 cause
// of "Couldn't reach the API" / CORS errors here).
export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || "http://localhost:5192";

const TOKEN_STORAGE_KEY = "scoram_token";
const ADMIN_TOKEN_STORAGE_KEY = "scoram_admin_token";

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

/**
 * Thin fetch wrapper for the ScoramAPI backend.
 * - Prefixes API_BASE_URL
 * - Attaches JSON headers + Bearer token (when present)
 * - Throws an Error with a readable message on non-2xx responses
 * - Returns parsed JSON (or null for empty 204 responses)
 *
 * `auth: true` attaches the *student* token via getStoredToken(). Admin API calls pass an explicit
 * `token` (their own admin token) instead -- see src/admin/api/*.js.
 */
export async function apiFetch(path, { method = "GET", body, auth = false, token, signal } = {}) {
  const headers = { "Content-Type": "application/json" };

  const resolvedToken = token ?? (auth ? getStoredToken() : null);
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
    // Distinguish "backend isn't reachable" from a normal HTTP error, since this is the
    // most common failure mode during local development (API not running / wrong port / CORS).
    throw new ApiError(
      `Couldn't reach the Scoram API at ${API_BASE_URL}. Is the backend running? (${networkError.message})`,
      0
    );
  }

  return parseApiResponse(response, resolvedToken, token !== undefined && token !== null);
}

/**
 * Same contract as apiFetch, but sends a FormData body (multipart/form-data) instead of JSON --
 * for the one endpoint that takes a file today: POST /api/admin/exams (exam logo upload).
 * Never set a Content-Type header yourself for this one; the browser sets the multipart boundary.
 */
export async function apiFetchForm(path, { method = "POST", formData, auth = false, token, signal } = {}) {
  const headers = {};
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

  return parseApiResponse(response, resolvedToken, token !== undefined && token !== null);
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


