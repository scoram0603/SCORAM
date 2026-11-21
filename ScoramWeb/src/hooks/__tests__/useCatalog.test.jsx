// @vitest-environment jsdom
import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { catalogKey, clearCatalog, invalidateCatalog, resetCatalogForTests, setCatalog } from "../../api/catalogCache";
import { useCatalog } from "../useCatalog";

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

function deferred() {
  let resolve; let reject;
  const promise = new Promise((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}

beforeEach(() => {
  localStorage.clear();
  resetCatalogForTests();
});
afterEach(() => cleanup());

describe("useCatalog", () => {
  it("paints a cached value on the very first render, with no request", () => {
    const key = catalogKey("qb:exams");
    setCatalog(key, ["cached"]);
    const fetcher = vi.fn().mockResolvedValue(["network"]);
    const { result } = renderHook(() => useCatalog(key, fetcher));
    expect(result.current.data).toEqual(["cached"]);
    expect(result.current.loading).toBe(false);
    expect(fetcher).not.toHaveBeenCalled();
  });

  it("loads once on a miss, then serves the value", async () => {
    const key = catalogKey("qb:years");
    const fetcher = vi.fn().mockResolvedValue([2024]);
    const { result } = renderHook(() => useCatalog(key, fetcher));
    expect(result.current.loading).toBe(true);
    expect(result.current.data).toBeUndefined();
    await waitFor(() => expect(result.current.data).toEqual([2024]));
    expect(result.current.loading).toBe(false);
    expect(fetcher).toHaveBeenCalledTimes(1);
  });

  it("two components with the same key share one request", async () => {
    const key = catalogKey("qb:exams");
    const d = deferred();
    const fetcher = vi.fn(() => d.promise);
    const a = renderHook(() => useCatalog(key, fetcher));
    const b = renderHook(() => useCatalog(key, fetcher));
    await act(async () => { d.resolve(["shared"]); });
    expect(a.result.current.data).toEqual(["shared"]);
    expect(b.result.current.data).toEqual(["shared"]);
    expect(fetcher).toHaveBeenCalledTimes(1);
  });

  it("does not fetch while disabled, and starts when enabled", async () => {
    const key = catalogKey("qb:exams");
    const fetcher = vi.fn().mockResolvedValue(["x"]);
    const { result, rerender } = renderHook(({ enabled }) => useCatalog(key, fetcher, { enabled }), { initialProps: { enabled: false } });
    expect(result.current.loading).toBe(false);
    expect(fetcher).not.toHaveBeenCalled();
    rerender({ enabled: true });
    await waitFor(() => expect(result.current.data).toEqual(["x"]));
    expect(fetcher).toHaveBeenCalledTimes(1);
  });

  it("reports a failure without caching it, and reload() retries", async () => {
    const key = catalogKey("qb:exams");
    const fetcher = vi.fn().mockRejectedValueOnce(new Error("down")).mockResolvedValueOnce(["ok"]);
    const { result } = renderHook(() => useCatalog(key, fetcher));
    await waitFor(() => expect(result.current.error).toBeTruthy());
    expect(result.current.loading).toBe(false);
    expect(result.current.data).toBeUndefined();
    act(() => result.current.reload());
    await waitFor(() => expect(result.current.data).toEqual(["ok"]));
    expect(result.current.error).toBeNull();
  });

  it("a late result for a previous key never shows under the new key", async () => {
    const keyA = catalogKey("qb:subjects", "A");
    const keyB = catalogKey("qb:subjects", "B");
    const dA = deferred();
    const dB = deferred();
    const fetchers = { A: vi.fn(() => dA.promise), B: vi.fn(() => dB.promise) };
    const { result, rerender } = renderHook(({ id }) => useCatalog(id === "A" ? keyA : keyB, fetchers[id]), { initialProps: { id: "A" } });
    rerender({ id: "B" });
    await act(async () => { dA.resolve(["from-A"]); });
    expect(result.current.data).toBeUndefined(); // B has nothing yet; A's answer is not shown
    await act(async () => { dB.resolve(["from-B"]); });
    expect(result.current.data).toEqual(["from-B"]);
  });

  it("refetches by itself when the entry is invalidated (My Exams changed)", async () => {
    const key = catalogKey("qb:exams");
    const fetcher = vi.fn().mockResolvedValueOnce(["scope-1"]).mockResolvedValueOnce(["scope-2"]);
    const { result } = renderHook(() => useCatalog(key, fetcher));
    await waitFor(() => expect(result.current.data).toEqual(["scope-1"]));
    act(() => invalidateCatalog());
    await waitFor(() => expect(result.current.data).toEqual(["scope-2"]));
    expect(fetcher).toHaveBeenCalledTimes(2);
  });

  it("recovers when the cache is cleared while its own request is still in flight", async () => {
    const key = catalogKey("qb:exams");
    const d = deferred();
    const fetcher = vi.fn().mockImplementationOnce(() => d.promise).mockResolvedValueOnce(["second"]);
    const { result } = renderHook(() => useCatalog(key, fetcher));
    act(() => clearCatalog());
    await act(async () => { d.resolve(["discarded"]); });
    await waitFor(() => expect(result.current.data).toEqual(["second"]));
  });

  it("stops updating after unmount", async () => {
    const key = catalogKey("qb:exams");
    const d = deferred();
    const { unmount } = renderHook(() => useCatalog(key, () => d.promise));
    unmount();
    await expect(Promise.resolve().then(() => d.resolve(["late"]))).resolves.toBeUndefined();
  });
});
