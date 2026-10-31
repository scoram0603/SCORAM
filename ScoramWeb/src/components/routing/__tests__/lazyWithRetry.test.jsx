// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { Suspense } from "react";
import { isChunkLoadError, lazyWithRetry, reloadOnceForStaleChunk } from "../lazyWithRetry";
import ChunkErrorBoundary from "../ChunkErrorBoundary";

beforeEach(() => sessionStorage.clear());
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("isChunkLoadError", () => {
  it.each([
    "Failed to fetch dynamically imported module: https://scoram.in/assets/Progress-abc.js",
    "error loading dynamically imported module",
    "Importing a module script failed.",
    "Loading chunk 12 failed.",
    "Unable to preload CSS for /assets/x.css",
  ])("recognises %s", (message) => expect(isChunkLoadError(new Error(message))).toBe(true));

  it("ignores ordinary errors", () => {
    expect(isChunkLoadError(new Error("x is not a function"))).toBe(false);
    expect(isChunkLoadError(null)).toBe(false);
  });
});

describe("reloadOnceForStaleChunk", () => {
  it("reloads the first time but not again inside the guard window (no reload loop)", () => {
    const reload = vi.fn();
    expect(reloadOnceForStaleChunk(1_000, reload)).toBe(true);
    expect(reloadOnceForStaleChunk(2_000, reload)).toBe(false);
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it("allows another reload once the window has passed (a later deployment)", () => {
    const reload = vi.fn();
    reloadOnceForStaleChunk(1_000, reload);
    expect(reloadOnceForStaleChunk(1_000 + 60_000, reload)).toBe(true);
    expect(reload).toHaveBeenCalledTimes(2);
  });
});

describe("lazyWithRetry + ChunkErrorBoundary", () => {
  it("renders the lazily loaded component", async () => {
    const Page = lazyWithRetry(() => Promise.resolve({ default: () => <p>real page</p> }));
    render(
      <ChunkErrorBoundary>
        <Suspense fallback={<p>loading</p>}>
          <Page />
        </Suspense>
      </ChunkErrorBoundary>
    );
    expect(await screen.findByText("real page")).toBeTruthy();
  });

  it("after the one allowed reload has been used, a failing chunk shows a retry instead of a blank screen", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    sessionStorage.setItem("scoram:chunk-reload-at", String(Date.now())); // reload already attempted
    const Page = lazyWithRetry(() => Promise.reject(new Error("Failed to fetch dynamically imported module: /x.js")));
    render(
      <ChunkErrorBoundary>
        <Suspense fallback={<p>loading</p>}>
          <Page />
        </Suspense>
      </ChunkErrorBoundary>
    );
    expect(await screen.findByText("Couldn't load this page")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Reload" })).toBeTruthy();
  });

  it("non-chunk render errors get the generic message", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    const Page = lazyWithRetry(() => Promise.resolve({ default: () => { throw new Error("boom"); } }));
    render(
      <ChunkErrorBoundary>
        <Suspense fallback={null}>
          <Page />
        </Suspense>
      </ChunkErrorBoundary>
    );
    expect(await screen.findByText("Something went wrong")).toBeTruthy();
  });
});
