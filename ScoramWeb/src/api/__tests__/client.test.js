import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// SINGLE-DEVICE LOGIN -- a second login elsewhere makes ScoramAPI reject the student's next token
// refresh with 401 { code: "SESSION_REVOKED" } (see AuthController.Refresh's RevokedReason branch).
// refreshStudentSession (client.js) reads that code and threads the reason through to the
// scoram:session-expired event's `detail`, which AuthContext.jsx listens for and Login.jsx's banner
// reads (see sessionRevokedElsewhere). These tests exercise client.js's half of that chain in
// isolation: pure-logic in Node, fetch/localStorage/window stubbed -- same style as
// api/__tests__/attachments.test.js.

function emptyResponse(status) {
  return new Response(null, { status });
}

function jsonResponse(body, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

function stubStorage(initial = {}) {
  const store = { ...initial };
  vi.stubGlobal("localStorage", {
    getItem: (k) => (k in store ? store[k] : null),
    setItem: (k, v) => {
      store[k] = String(v);
    },
    removeItem: (k) => {
      delete store[k];
    },
  });
  return store;
}

async function load() {
  vi.resetModules();
  return import("../client.js");
}

let dispatchEvent;

function sessionExpiredDetail() {
  const call = dispatchEvent.mock.calls.find(([event]) => event.type === "scoram:session-expired");
  return call?.[0]?.detail;
}

beforeEach(() => {
  dispatchEvent = vi.fn();
  vi.stubGlobal("window", { dispatchEvent, addEventListener: vi.fn() });
  stubStorage({ scoram_token: "stale-token", scoram_refresh_token: "ref-1" });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("single-device login: SESSION_REVOKED propagation", () => {
  it("reports revokedElsewhere: true when the refresh call comes back SESSION_REVOKED", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation(async (url) => {
        if (String(url).endsWith("/api/auth/refresh")) {
          return jsonResponse({ code: "SESSION_REVOKED", message: "Your account was logged in on another device." }, 401);
        }
        return emptyResponse(401); // the original request, retried never (refresh itself failed)
      })
    );
    const { apiFetch, ApiError } = await load();

    const error = await apiFetch("/api/a", { auth: true }).catch((e) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(401);
    expect(sessionExpiredDetail()).toEqual({ revokedElsewhere: true });
  });

  it("reports revokedElsewhere: false for every OTHER way a refresh token dies (expiry, logout, a bare 401)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(emptyResponse(401)));
    const { apiFetch } = await load();

    await apiFetch("/api/a", { auth: true }).catch(() => {});

    expect(sessionExpiredDetail()).toEqual({ revokedElsewhere: false });
  });

  it("does not leak a SESSION_REVOKED reading into a later, unrelated session-expired event", async () => {
    // Guards the module-level flag this is threaded through: it must be cleared the moment it's
    // read, not persist and misattribute some later, different reason for ending the session.
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation(async (url) => {
        if (String(url).endsWith("/api/auth/refresh")) return jsonResponse({ code: "SESSION_REVOKED" }, 401);
        return emptyResponse(401);
      })
    );
    const { apiFetch, resetSessionExpiredGuard } = await load();

    await apiFetch("/api/a", { auth: true }).catch(() => {});
    expect(dispatchEvent).toHaveBeenCalledTimes(1);
    expect(sessionExpiredDetail()).toEqual({ revokedElsewhere: true });

    // A fresh login re-arms the guard (see AuthContext's applyAuthResponse); a later, ordinary dead
    // token must not still read as "revoked elsewhere".
    resetSessionExpiredGuard(false);
    dispatchEvent.mockClear();
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(emptyResponse(401)));
    await apiFetch("/api/a", { auth: true }).catch(() => {});

    expect(sessionExpiredDetail()).toEqual({ revokedElsewhere: false });
  });

  it("never attaches a revokedElsewhere reason to the ADMIN session-expired event (admins stay multi-device)", async () => {
    stubStorage({}); // no admin refresh token stored -> refreshAdminSession short-circuits to null
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(emptyResponse(401)));
    const { apiFetch } = await load();

    await apiFetch("/api/admin/a", { token: "stale-admin-token" }).catch(() => {});

    const call = dispatchEvent.mock.calls.find(([event]) => event.type === "scoram:admin-session-expired");
    expect(call).toBeTruthy();
    expect(call[0].detail).toBeNull();
  });
});
