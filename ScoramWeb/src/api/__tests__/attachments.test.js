import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Private chat / DM attachment loading (src/api/attachments.js + apiFetchBlob in client.js).
// Pure-logic tests in Node: fetch, localStorage, window and document are stubbed, so no browser or API is needed.

const MESSAGE_ID = "11111111-2222-3333-4444-555555555555";
const CHAT_PATH = `/api/chat/messages/${MESSAGE_ID}/attachment`;
const DM_PATH = `/api/directmessages/messages/${MESSAGE_ID}/attachment`;

function fileResponse(body = "bytes", type = "image/png", status = 200) {
  return new Response(new Blob([body], { type }), { status, headers: { "content-type": type } });
}

function emptyResponse(status) {
  return new Response(null, { status });
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
  return import("../attachments.js");
}

let createSpy;
let revokeSpy;

beforeEach(() => {
  vi.useFakeTimers();
  let n = 0;
  createSpy = vi.spyOn(URL, "createObjectURL").mockImplementation(() => `blob:test/${++n}`);
  revokeSpy = vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});
  stubStorage({ scoram_token: "access-token" });
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("toPrivateAttachmentPath", () => {
  it("accepts the authenticated chat and DM attachment paths", async () => {
    const { toPrivateAttachmentPath } = await load();
    expect(toPrivateAttachmentPath(CHAT_PATH)).toBe(CHAT_PATH);
    expect(toPrivateAttachmentPath(DM_PATH)).toBe(DM_PATH);
  });

  it("accepts an absolute URL on our own API origin and returns just the path", async () => {
    const { toPrivateAttachmentPath } = await load();
    const { API_BASE_URL } = await import("../client.js");
    expect(toPrivateAttachmentPath(`${API_BASE_URL}${CHAT_PATH}`)).toBe(CHAT_PATH);
  });

  it.each([
    ["legacy public chat URL", "/uploads/chat-attachments/11111111-2222-3333-4444-555555555555.png"],
    ["legacy public DM URL", "/uploads/dm-attachments/11111111-2222-3333-4444-555555555555.webm"],
    ["another host", `https://evil.example.com${CHAT_PATH}`],
    ["malformed id", "/api/chat/messages/not-a-guid/attachment"],
    ["traversal", `/api/chat/messages/${MESSAGE_ID}/attachment/../../x`],
    ["query string", `${CHAT_PATH}?token=abc`],
    ["other API route", `/api/chat/messages/${MESSAGE_ID}`],
    ["empty", ""],
    [null, null],
    [undefined, undefined],
    [42, 42],
  ])("rejects %s", async (_label, value) => {
    const { toPrivateAttachmentPath } = await load();
    expect(toPrivateAttachmentPath(value)).toBeNull();
  });
});

describe("describeAttachmentError", () => {
  it("maps statuses to user-facing kinds", async () => {
    const { describeAttachmentError } = await load();
    expect(describeAttachmentError({ status: 401 }).kind).toBe("unauthorized");
    expect(describeAttachmentError({ status: 403 }).kind).toBe("unauthorized");
    expect(describeAttachmentError({ status: 404 })).toMatchObject({ kind: "gone", retryable: false });
    expect(describeAttachmentError({ status: 0 })).toMatchObject({ kind: "network", retryable: true });
    expect(describeAttachmentError(new Error("boom"))).toMatchObject({ kind: "network", retryable: true });
    expect(describeAttachmentError({ status: 502 })).toMatchObject({ kind: "server", retryable: true });
  });

  it("never echoes raw server or storage details", async () => {
    const { describeAttachmentError } = await load();
    const { message } = describeAttachmentError({ status: 502, message: "AccountKey=secret" });
    expect(message).not.toMatch(/AccountKey|secret/);
  });
});

describe("acquireAttachment", () => {
  it("fetches with the Bearer token in the header only, and returns an object URL", async () => {
    const fetchMock = vi.fn().mockResolvedValue(fileResponse());
    vi.stubGlobal("fetch", fetchMock);
    const { acquireAttachment } = await load();

    const result = await acquireAttachment(CHAT_PATH);

    expect(result.url).toBe("blob:test/1");
    const [url, init] = fetchMock.mock.calls[0];
    expect(url.endsWith(CHAT_PATH)).toBe(true);
    expect(url).not.toMatch(/token|access/i); // never in the URL
    expect(init.headers.Authorization).toBe("Bearer access-token");
    expect(init.method).toBe("GET");
  });

  it("shares one download between components showing the same attachment", async () => {
    const fetchMock = vi.fn().mockImplementation(async () => fileResponse());
    vi.stubGlobal("fetch", fetchMock);
    const { acquireAttachment } = await load();

    const [a, b, c] = await Promise.all([acquireAttachment(DM_PATH), acquireAttachment(DM_PATH), acquireAttachment(DM_PATH)]);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(a.url).toBe(b.url);
    expect(b.url).toBe(c.url);
    expect(createSpy).toHaveBeenCalledTimes(1);
  });

  it("revokes the object URL only after the LAST user releases it plus the grace period", async () => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => fileResponse()));
    const { acquireAttachment, RELEASE_GRACE_MS } = await load();
    const first = await acquireAttachment(CHAT_PATH);
    const second = await acquireAttachment(CHAT_PATH);

    first.release();
    first.release(); // idempotent
    vi.advanceTimersByTime(RELEASE_GRACE_MS + 1);
    expect(revokeSpy).not.toHaveBeenCalled(); // still in use by the second

    second.release();
    vi.advanceTimersByTime(RELEASE_GRACE_MS - 1);
    expect(revokeSpy).not.toHaveBeenCalled(); // inside the grace period
    vi.advanceTimersByTime(2);
    expect(revokeSpy).toHaveBeenCalledTimes(1);
    expect(revokeSpy).toHaveBeenCalledWith("blob:test/1");
  });

  it("reuses the download when a component re-mounts inside the grace period (no refetch)", async () => {
    const fetchMock = vi.fn().mockImplementation(async () => fileResponse());
    vi.stubGlobal("fetch", fetchMock);
    const { acquireAttachment, RELEASE_GRACE_MS } = await load();

    const first = await acquireAttachment(CHAT_PATH);
    first.release();
    vi.advanceTimersByTime(RELEASE_GRACE_MS / 2);
    const again = await acquireAttachment(CHAT_PATH);
    vi.advanceTimersByTime(RELEASE_GRACE_MS * 2);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(again.url).toBe(first.url);
    expect(revokeSpy).not.toHaveBeenCalled(); // re-acquired, so the pending disposal was cancelled
  });

  it("surfaces a 404 as an ApiError and does not cache the failure", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(emptyResponse(404)).mockResolvedValueOnce(fileResponse());
    vi.stubGlobal("fetch", fetchMock);
    const { acquireAttachment, describeAttachmentError } = await load();

    const error = await acquireAttachment(CHAT_PATH).catch((e) => e);
    expect(error.status).toBe(404);
    expect(describeAttachmentError(error).kind).toBe("gone");

    const retry = await acquireAttachment(CHAT_PATH); // a retry starts a fresh download
    expect(retry.url).toBe("blob:test/1");
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("reports a network failure without throwing a raw error type", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));
    const { acquireAttachment, describeAttachmentError } = await load();

    const error = await acquireAttachment(CHAT_PATH).catch((e) => e);

    expect(error.status).toBe(0);
    expect(describeAttachmentError(error).kind).toBe("network");
  });

  it("refreshes the session once on a 401 and retries with the new token", async () => {
    stubStorage({ scoram_token: "old-token", scoram_refresh_token: "refresh-1" });
    vi.stubGlobal("window", { dispatchEvent: vi.fn(), addEventListener: vi.fn() });
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(emptyResponse(401))
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ token: "new-token", refreshToken: "refresh-2" }), {
          status: 200,
          headers: { "content-type": "application/json" },
        })
      )
      .mockResolvedValueOnce(fileResponse());
    vi.stubGlobal("fetch", fetchMock);
    const { acquireAttachment } = await load();

    const result = await acquireAttachment(CHAT_PATH);

    expect(result.url).toBe("blob:test/1");
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(fetchMock.mock.calls[1][0]).toMatch(/\/api\/auth\/refresh$/);
    expect(fetchMock.mock.calls[2][1].headers.Authorization).toBe("Bearer new-token");
  });

  it("ends in an unauthorized error (and signals session expiry) when the refresh is rejected", async () => {
    stubStorage({ scoram_token: "old-token", scoram_refresh_token: "dead" });
    const dispatchEvent = vi.fn();
    vi.stubGlobal("window", { dispatchEvent, addEventListener: vi.fn() });
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(emptyResponse(401)) // attachment
        .mockResolvedValueOnce(emptyResponse(401)) // refresh rejected
        .mockResolvedValueOnce(emptyResponse(401)) // never reached, defensive
    );
    const { acquireAttachment, describeAttachmentError } = await load();

    const error = await acquireAttachment(CHAT_PATH).catch((e) => e);

    expect(error.status).toBe(401);
    expect(describeAttachmentError(error).kind).toBe("unauthorized");
    expect(dispatchEvent).toHaveBeenCalled();
  });

  it("aborts a download nobody wants any more", async () => {
    let signal;
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((_url, init) => {
        signal = init.signal;
        return new Promise((_resolve, reject) => {
          init.signal.addEventListener("abort", () => reject(Object.assign(new Error("aborted"), { name: "AbortError" })));
        });
      })
    );
    const { acquireAttachment, RELEASE_GRACE_MS, clearAttachmentCache } = await load();

    const pending = acquireAttachment(CHAT_PATH).catch((e) => e);
    clearAttachmentCache();
    const error = await pending;

    expect(signal.aborted).toBe(true);
    expect(error.name).toBe("AbortError");
    vi.advanceTimersByTime(RELEASE_GRACE_MS + 1);
    expect(createSpy).not.toHaveBeenCalled(); // no object URL is ever created for a discarded download
  });
});

describe("clearAttachmentCache and the session-expired signal", () => {
  it("revokes every cached object URL immediately", async () => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => fileResponse()));
    const { acquireAttachment, clearAttachmentCache } = await load();
    await acquireAttachment(CHAT_PATH);
    await acquireAttachment(DM_PATH);

    clearAttachmentCache();

    expect(revokeSpy).toHaveBeenCalledTimes(2);
  });

  it("is wired to the expired-session event", async () => {
    const addEventListener = vi.fn();
    vi.stubGlobal("window", { dispatchEvent: vi.fn(), addEventListener });
    vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => fileResponse()));
    const { acquireAttachment } = await load();
    await acquireAttachment(CHAT_PATH);

    const registered = addEventListener.mock.calls.find(([name]) => name === "scoram:session-expired");
    expect(registered).toBeTruthy();
    registered[1]();

    expect(revokeSpy).toHaveBeenCalledTimes(1);
  });
});

describe("downloads", () => {
  function stubDocument() {
    const link = { click: vi.fn(), remove: vi.fn(), set href(v) { this._href = v; }, get href() { return this._href; } };
    vi.stubGlobal("document", { createElement: vi.fn(() => link), body: { appendChild: vi.fn() } });
    return link;
  }

  it("saves a document through an anchor with a neutral name and the right extension", async () => {
    const link = stubDocument();
    vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => fileResponse("%PDF", "application/pdf")));
    const { downloadAttachment } = await load();

    await downloadAttachment(CHAT_PATH);

    expect(link.download).toBe("attachment.pdf");
    expect(link.href).toBe("blob:test/1");
    expect(link.click).toHaveBeenCalledTimes(1);
  });

  it("propagates a missing file instead of failing silently", async () => {
    stubDocument();
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(emptyResponse(404)));
    const { downloadAttachment } = await load();

    await expect(downloadAttachment(CHAT_PATH)).rejects.toMatchObject({ status: 404 });
  });

  it("opens a PDF in the tab that was opened during the click, and downloads other types", async () => {
    const tab = { opener: {}, location: { href: "" }, close: vi.fn() };
    vi.stubGlobal("window", { open: vi.fn(() => tab), dispatchEvent: vi.fn(), addEventListener: vi.fn() });
    vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => fileResponse("%PDF", "application/pdf")));
    const { openDocumentAttachment } = await load();

    await openDocumentAttachment(CHAT_PATH);

    expect(window.open).toHaveBeenCalledWith("about:blank", "_blank");
    expect(tab.opener).toBeNull();
    expect(tab.location.href).toBe("blob:test/1");
    expect(tab.close).not.toHaveBeenCalled();
  });

  it("closes the pre-opened tab when the document cannot be loaded", async () => {
    const tab = { opener: {}, location: { href: "" }, close: vi.fn() };
    vi.stubGlobal("window", { open: vi.fn(() => tab), dispatchEvent: vi.fn(), addEventListener: vi.fn() });
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(emptyResponse(404)));
    const { openDocumentAttachment } = await load();

    await expect(openDocumentAttachment(CHAT_PATH)).rejects.toMatchObject({ status: 404 });
    expect(tab.close).toHaveBeenCalled();
  });
});

describe("extensionForContentType", () => {
  it("maps known types and ignores parameters", async () => {
    const { extensionForContentType } = await load();
    expect(extensionForContentType("application/pdf")).toBe(".pdf");
    expect(extensionForContentType("audio/webm;codecs=opus")).toBe(".webm");
    expect(extensionForContentType("application/x-unknown")).toBe("");
    expect(extensionForContentType(undefined)).toBe("");
  });
});
