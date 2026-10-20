import { describe, expect, it } from "vitest";
import { filterPublicReviews, formatReviewDate, reviewInitials, validateReview, MAX_TEXT } from "../reviews";

describe("validateReview", () => {
  it("accepts a valid review and returns cleaned text", () => {
    const r = validateReview({ rating: 4, text: "  Great practice papers.  " });
    expect(r.valid).toBe(true);
    expect(r.text).toBe("Great practice papers.");
  });
  it("rejects ratings outside the integers 1-5", () => {
    for (const rating of [0, 6, 2.5, NaN, null, undefined, "5"]) {
      expect(validateReview({ rating, text: "long enough text" }).errors.rating).toBeTruthy();
    }
    for (const rating of [1, 2, 3, 4, 5]) expect(validateReview({ rating, text: "long enough text" }).errors.rating).toBeUndefined();
  });
  it("enforces text length", () => {
    expect(validateReview({ rating: 5, text: "short" }).errors.text).toBeTruthy();
    expect(validateReview({ rating: 5, text: "   " }).errors.text).toBeTruthy();
    expect(validateReview({ rating: 5, text: "x".repeat(MAX_TEXT + 1) }).errors.text).toBeTruthy();
    expect(validateReview({ rating: 5, text: "x".repeat(MAX_TEXT) }).valid).toBe(true);
  });
});

describe("filterPublicReviews (presentation safeguard)", () => {
  const ok = { id: "1", rating: 5, text: "Nice", displayName: "A", createdAt: "2026-01-01T00:00:00Z" };
  it("drops anything under 3 stars, malformed rows and duplicates", () => {
    const out = filterPublicReviews([
      ok,
      { ...ok, id: "2", rating: 2 },
      { ...ok, id: "3", rating: 1 },
      { ...ok, id: "4", rating: 3 },
      { ...ok, id: "5", rating: "5" },
      { ...ok, id: "6", text: "  " },
      { ...ok, id: 7 },
      { ...ok, id: "1" },
      null,
    ]);
    expect(out.map((r) => r.id)).toEqual(["1", "4"]);
  });
  it("handles non-arrays", () => {
    expect(filterPublicReviews(undefined)).toEqual([]);
    expect(filterPublicReviews({})).toEqual([]);
  });
});

describe("display helpers", () => {
  it("initials", () => {
    expect(reviewInitials("Priya S.")).toBe("PS");
    expect(reviewInitials("Priya")).toBe("P");
    expect(reviewInitials("SCORAM Student")).toBe("S");
    expect(reviewInitials("")).toBe("S");
  });
  it("date formatting tolerates bad input", () => {
    expect(formatReviewDate("nonsense")).toBe("");
    expect(formatReviewDate("2026-03-05T00:00:00Z")).toMatch(/2026/);
  });
});
