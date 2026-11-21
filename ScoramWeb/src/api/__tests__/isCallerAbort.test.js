import { describe, expect, it } from "vitest";
import { ApiError, isCallerAbort } from "../client";

// apiFetch turns a fetch AbortError into an ApiError(timedOut), so the error's own name says nothing
// about whether the CALLER cancelled -- the caller's signal does.
describe("isCallerAbort", () => {
  it("is true for the ApiError apiFetch produces when the caller's signal was aborted", () => {
    const controller = new AbortController();
    controller.abort();
    expect(isCallerAbort(new ApiError("timed out", 0, { timedOut: true }), controller.signal)).toBe(true);
  });

  it("is false for a real failure while the signal is still live", () => {
    const controller = new AbortController();
    expect(isCallerAbort(new ApiError("Server error", 500, null), controller.signal)).toBe(false);
    expect(isCallerAbort(new ApiError("timed out", 0, { timedOut: true }), controller.signal)).toBe(false);
  });

  it("recognises a raw AbortError (e.g. from apiFetchBlob) and tolerates a missing signal", () => {
    const abort = Object.assign(new Error("aborted"), { name: "AbortError" });
    expect(isCallerAbort(abort, undefined)).toBe(true);
    expect(isCallerAbort(new Error("x"), undefined)).toBe(false);
    expect(isCallerAbort(undefined, undefined)).toBe(false);
  });
});
