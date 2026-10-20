import { describe, expect, it } from "vitest";
import { DEFAULT_SMART_PROGRESS, isSafeLink, resolveSmartProgress } from "../smartProgressDefaults";

describe("resolveSmartProgress", () => {
  it("uses safe defaults when nothing is configured or the data is malformed", () => {
    expect(resolveSmartProgress(null)).toBe(DEFAULT_SMART_PROGRESS);
    expect(resolveSmartProgress(undefined)).toBe(DEFAULT_SMART_PROGRESS);
    expect(resolveSmartProgress("x")).toBe(DEFAULT_SMART_PROGRESS);
    expect(resolveSmartProgress({ heading: "", description: "d" })).toBe(DEFAULT_SMART_PROGRESS);
    expect(resolveSmartProgress({ heading: "h" })).toBe(DEFAULT_SMART_PROGRESS);
  });

  it("returns null only when an admin hides a valid section", () => {
    expect(resolveSmartProgress({ published: false, heading: "h", description: "d" })).toBeNull();
    expect(resolveSmartProgress({ published: true, heading: "h", description: "d" })).not.toBeNull();
  });

  it("keeps admin text, trims and caps highlights, drops blanks", () => {
    const out = resolveSmartProgress({
      published: true, heading: " Head ", description: " Desc ",
      highlights: ["a", " ", "b", 3, "c", "d", "e", "f", "g"], ctaText: "Go", ctaLink: "/login?mode=register",
    });
    expect(out.heading).toBe("Head");
    expect(out.highlights).toEqual(["a", "b", "c", "d", "e", "f"]);
    expect(out.ctaText).toBe("Go");
  });

  it("drops an unsafe or half-configured CTA instead of rendering it", () => {
    const base = { published: true, heading: "h", description: "d" };
    expect(resolveSmartProgress({ ...base, ctaText: "Go", ctaLink: "javascript:alert(1)" }).ctaText).toBe("");
    expect(resolveSmartProgress({ ...base, ctaText: "", ctaLink: "/x" }).ctaLink).toBe("");
    expect(resolveSmartProgress({ ...base, ctaText: "Go", ctaLink: "" }).ctaText).toBe("");
  });

  it("default copy makes no outcome guarantees", () => {
    const text = JSON.stringify(DEFAULT_SMART_PROGRESS).toLowerCase();
    for (const word of ["guarantee", "rank 1", "100%", "assured", "selection"]) expect(text).not.toContain(word);
  });
});

describe("isSafeLink", () => {
  it("allows site paths and https only", () => {
    expect(isSafeLink("/login?mode=register")).toBe(true);
    expect(isSafeLink("https://scoram.in/x")).toBe(true);
    expect(isSafeLink("http://scoram.in")).toBe(false);
    expect(isSafeLink("//evil.test")).toBe(false);
    expect(isSafeLink("javascript:alert(1)")).toBe(false);
    expect(isSafeLink("data:text/html,x")).toBe(false);
    expect(isSafeLink("/a b")).toBe(false);
    expect(isSafeLink("")).toBe(false);
  });
});
