// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  CATALOG_MAX_AGE_MS, catalogKey, clearCatalog, currentCatalogIdentity, getCatalogEntry, invalidateCatalog,
  loadCatalog, peekCatalog, resetCatalogForTests, setCatalog, subscribeCatalog,
} from "../catalogCache";

function b64url(obj) {
  return btoa(JSON.stringify(obj)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
function jwt(claims, tail = "sig") {
  return `${b64url({ alg: "HS256" })}.${b64url(claims)}.${tail}`;
}
function signIn(sub, tail) {
  localStorage.setItem("scoram_token", jwt(sub ? { sub } : {}, tail));
}
// A promise whose resolution the test controls.
function deferred() {
  let resolve; let reject;
  const promise = new Promise((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}

beforeEach(() => {
  localStorage.clear();
  resetCatalogForTests();
  vi.useFakeTimers();
});
afterEach(() => {
  vi.useRealTimers();
});

describe("keys and identity", () => {
  it("is 'anon' without a token and the JWT sub when signed in", () => {
    expect(currentCatalogIdentity()).toBe("anon");
    signIn("user-1");
    expect(currentCatalogIdentity()).toBe("user-1");
    expect(catalogKey("qb:exams")).toBe("u:user-1|qb:exams|");
  });

  it("never lets two students share a key", () => {
    signIn("user-1");
    const a = catalogKey("qb:exams");
    signIn("user-2");
    const b = catalogKey("qb:exams");
    expect(a).not.toBe(b);
  });

  it("falls back to a token-specific identity when the sub claim cannot be read", () => {
    signIn(null, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-one");
    const a = catalogKey("qb:exams");
    signIn(null, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb-two");
    const b = catalogKey("qb:exams");
    expect(a).not.toBe(b);
    expect(a.startsWith("u:t:")).toBe(true);
    localStorage.setItem("scoram_token", "not-a-jwt-at-all");
    expect(catalogKey("x")).toMatch(/^u:t:/);
  });

  it("keeps public keys identical for everyone", () => {
    signIn("user-1");
    const a = catalogKey("exams:list", "", { scope: "public" });
    signIn("user-2");
    expect(catalogKey("exams:list", "", { scope: "public" })).toBe(a);
  });
});

describe("loadCatalog", () => {
  it("serves a fresh entry without calling the fetcher again", async () => {
    const fetcher = vi.fn().mockResolvedValue([1, 2]);
    const key = catalogKey("qb:exams");
    expect(await loadCatalog(key, fetcher)).toEqual([1, 2]);
    vi.advanceTimersByTime(CATALOG_MAX_AGE_MS - 1000);
    expect(await loadCatalog(key, fetcher)).toEqual([1, 2]);
    expect(fetcher).toHaveBeenCalledTimes(1);
  });

  it("shares one request between concurrent callers", async () => {
    const d = deferred();
    const fetcher = vi.fn(() => d.promise);
    const key = catalogKey("qb:years");
    const p1 = loadCatalog(key, fetcher);
    const p2 = loadCatalog(key, fetcher);
    d.resolve([2024]);
    expect(await p1).toEqual([2024]);
    expect(await p2).toEqual([2024]);
    expect(fetcher).toHaveBeenCalledTimes(1);
  });

  it("does not hand the fetcher anything (no caller AbortSignal)", async () => {
    const fetcher = vi.fn().mockResolvedValue("x");
    await loadCatalog(catalogKey("a"), fetcher);
    expect(fetcher.mock.calls[0]).toEqual([]);
  });

  it("stale-while-revalidate: returns the old value at once, refreshes once in the background and notifies", async () => {
    const key = catalogKey("qb:exams");
    await loadCatalog(key, vi.fn().mockResolvedValue(["old"]));
    vi.advanceTimersByTime(CATALOG_MAX_AGE_MS + 1);

    const d = deferred();
    const fetcher = vi.fn(() => d.promise);
    const listener = vi.fn();
    subscribeCatalog(key, listener);

    expect(await loadCatalog(key, fetcher)).toEqual(["old"]); // immediate, stale
    await loadCatalog(key, fetcher); // a second caller does not start a second refresh
    expect(fetcher).toHaveBeenCalledTimes(1);
    expect(peekCatalog(key)).toEqual(["old"]);

    d.resolve(["new"]);
    await vi.waitFor(() => expect(peekCatalog(key)).toEqual(["new"]));
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it("keeps the stale value when the background refresh fails", async () => {
    const key = catalogKey("qb:exams");
    await loadCatalog(key, vi.fn().mockResolvedValue(["old"]));
    vi.advanceTimersByTime(CATALOG_MAX_AGE_MS + 1);
    const failing = vi.fn().mockRejectedValue(new Error("boom"));
    expect(await loadCatalog(key, failing)).toEqual(["old"]);
    await Promise.resolve();
    await Promise.resolve();
    expect(peekCatalog(key)).toEqual(["old"]);
  });

  it("does not cache a failure and lets the next call retry", async () => {
    const key = catalogKey("qb:exams");
    const fetcher = vi.fn().mockRejectedValueOnce(new Error("down")).mockResolvedValueOnce(["ok"]);
    await expect(loadCatalog(key, fetcher)).rejects.toThrow("down");
    expect(getCatalogEntry(key)).toBeUndefined();
    expect(await loadCatalog(key, fetcher)).toEqual(["ok"]);
    expect(fetcher).toHaveBeenCalledTimes(2);
  });

  it("treats an entry dated in the future (clock moved back) as stale", async () => {
    const key = catalogKey("qb:exams");
    vi.setSystemTime(new Date("2026-01-02T00:00:00Z"));
    await loadCatalog(key, vi.fn().mockResolvedValue(["a"]));
    vi.setSystemTime(new Date("2026-01-01T00:00:00Z"));
    const fetcher = vi.fn().mockResolvedValue(["b"]);
    await loadCatalog(key, fetcher);
    expect(fetcher).toHaveBeenCalledTimes(1);
  });

  it("setCatalog seeds an entry (and notifies) so no request is needed", async () => {
    const key = catalogKey("qb:total");
    const listener = vi.fn();
    subscribeCatalog(key, listener);
    setCatalog(key, 1234);
    expect(listener).toHaveBeenCalledTimes(1);
    const fetcher = vi.fn();
    expect(await loadCatalog(key, fetcher)).toBe(1234);
    expect(fetcher).not.toHaveBeenCalled();
  });
});

describe("clearing", () => {
  it("clearCatalog drops this student's entries but keeps the public list", async () => {
    signIn("user-1");
    const userKey = catalogKey("qb:exams");
    const pubKey = catalogKey("exams:list", "", { scope: "public" });
    setCatalog(userKey, ["x"]);
    setCatalog(pubKey, ["p"]);
    clearCatalog();
    expect(peekCatalog(userKey)).toBeUndefined();
    expect(peekCatalog(pubKey)).toEqual(["p"]);
  });

  it("a request that was in flight when the cache was cleared cannot repopulate it (logout -> next login)", async () => {
    signIn("user-1");
    const key = catalogKey("qb:exams");
    const d = deferred();
    const pending = loadCatalog(key, () => d.promise);
    clearCatalog();
    d.resolve(["student-1-data"]);
    await pending;
    expect(peekCatalog(key)).toBeUndefined();
  });

  it("after a clear, the next call starts a fresh request instead of joining the old one", async () => {
    const key = catalogKey("qb:exams");
    const d = deferred();
    loadCatalog(key, () => d.promise).catch(() => {});
    invalidateCatalog();
    const fetcher = vi.fn().mockResolvedValue(["fresh"]);
    expect(await loadCatalog(key, fetcher)).toEqual(["fresh"]);
    expect(fetcher).toHaveBeenCalledTimes(1);
    d.resolve(["old"]);
    await Promise.resolve();
    expect(peekCatalog(key)).toEqual(["fresh"]);
  });

  it("invalidateCatalog notifies subscribers of dropped entries", () => {
    const key = catalogKey("qb:exams");
    setCatalog(key, ["x"]);
    const listener = vi.fn();
    subscribeCatalog(key, listener);
    invalidateCatalog();
    expect(listener).toHaveBeenCalledTimes(1);
    expect(getCatalogEntry(key)).toBeUndefined();
  });

  it("the session-expired event clears the student's catalog", () => {
    const key = catalogKey("qb:exams");
    setCatalog(key, ["x"]);
    window.dispatchEvent(new CustomEvent("scoram:session-expired", { detail: { revokedElsewhere: true } }));
    expect(peekCatalog(key)).toBeUndefined();
  });

  it("unsubscribe stops notifications", () => {
    const key = catalogKey("qb:exams");
    const listener = vi.fn();
    const off = subscribeCatalog(key, listener);
    off();
    setCatalog(key, 1);
    expect(listener).not.toHaveBeenCalled();
  });
});
