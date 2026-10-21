import { describe, expect, it } from "vitest";
import {
  capAnswers, dequeueSent, enqueueAnswer, filtersFromParams, hasBrowseParams, isDefaultPosition, mergeAnswers,
  normalizeOption, pagesNeeded, paramsFromFilters, qKey, resolveResumeIndex, retryDelayMs, sanitizeFilters,
} from "../browseProgress";

describe("normalizeOption / qKey", () => {
  it("accepts only A-D", () => {
    expect(normalizeOption(" b ")).toBe("B");
    expect(normalizeOption("E")).toBeNull();
    expect(normalizeOption("AB")).toBeNull();
    expect(normalizeOption(null)).toBeNull();
  });
  it("compares ids case-insensitively", () => {
    expect(qKey("ABC-1")).toBe(qKey("abc-1"));
  });
});

describe("filters <-> URL", () => {
  it("round-trips filters and view mode", () => {
    const params = new URLSearchParams("q=plastic&examIds=e1,e2&years=2019&mode=slide");
    const filters = filtersFromParams(params);
    expect(filters).toEqual({ q: "plastic", examIds: ["e1", "e2"], years: ["2019"] });
    expect(paramsFromFilters(filters, "slide").toString()).toBe("q=plastic&examIds=e1%2Ce2&years=2019&mode=slide");
  });
  it("mode alone is not a filter, so a saved session may still be restored", () => {
    expect(hasBrowseParams(new URLSearchParams("mode=slide"))).toBe(false);
    expect(hasBrowseParams(new URLSearchParams("subjectIds=s1"))).toBe(true);
  });
  it("drops unknown keys and bad values coming from the server", () => {
    const clean = sanitizeFilters({ q: "x", evil: "<script>", examIds: ["ok", 5, { a: 1 }, "a,b", ""], years: "2019" });
    expect(clean).toEqual({ q: "x", examIds: ["ok", "5"] });
    expect(sanitizeFilters("nope")).toEqual({});
    expect(sanitizeFilters([1])).toEqual({});
  });
});

describe("resume position", () => {
  const items = [{ id: "A" }, { id: "B" }, { id: "C" }];
  it("computes how many pages are needed", () => {
    expect(pagesNeeded(0, 10)).toBe(1);
    expect(pagesNeeded(9, 10)).toBe(1);
    expect(pagesNeeded(10, 10)).toBe(2);
    expect(pagesNeeded(999999, 10)).toBe(30);
    expect(pagesNeeded(-3, 10)).toBe(1);
  });
  it("prefers the saved question id, then the clamped index", () => {
    expect(resolveResumeIndex(items, "b", 0)).toBe(1);
    expect(resolveResumeIndex(items, "gone", 2)).toBe(2);
    expect(resolveResumeIndex(items, null, 99)).toBe(2);
    expect(resolveResumeIndex([], "b", 4)).toBe(0);
  });
});

describe("answers", () => {
  it("unsynced taps override what the server had", () => {
    const merged = mergeAnswers([{ questionId: "Q1", selectedOption: "B" }, { questionId: "Q2", selectedOption: "x" }], [{ questionId: "q1", option: "C" }]);
    expect(merged).toEqual({ q1: "C" });
  });
  it("keeps only the latest tap per question in the queue", () => {
    let q = enqueueAnswer([], { questionId: "Q1", option: "B", at: "t1" });
    q = enqueueAnswer(q, { questionId: "q1", option: "C", at: "t2" });
    expect(q).toEqual([{ questionId: "q1", option: "C", at: "t2" }]);
  });
  it("a newer tap made while a send was in flight is not dropped", () => {
    const sent = [{ questionId: "q1", option: "B", at: "t1" }];
    const queueNow = [{ questionId: "q1", option: "C", at: "t2" }];
    expect(dequeueSent(queueNow, sent)).toEqual(queueNow);
    expect(dequeueSent(sent, sent)).toEqual([]);
  });
  it("backs off between retries", () => {
    expect([0, 1, 2, 3, 10].map(retryDelayMs)).toEqual([5000, 10000, 20000, 40000, 60000]);
  });
  it("caps the local mirror", () => {
    const many = Object.fromEntries(Array.from({ length: 5 }, (_, i) => [`q${i}`, "A"]));
    expect(Object.keys(capAnswers(many, 3))).toEqual(["q2", "q3", "q4"]);
  });
  it("detects a position that isn't worth saving", () => {
    expect(isDefaultPosition({ filters: {}, lastPosition: 0 })).toBe(true);
    expect(isDefaultPosition({ filters: { q: "x" }, lastPosition: 0 })).toBe(false);
    expect(isDefaultPosition({ filters: {}, lastPosition: 3 })).toBe(false);
  });
});
