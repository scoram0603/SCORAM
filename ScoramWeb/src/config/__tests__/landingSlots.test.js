import { describe, expect, it } from "vitest";
import { LANDING_SLOTS, isOwnMediaUrl, resolveSlotSources, toAbsoluteMediaUrl } from "../landingSlots";

const API = "https://api.example.test";

describe("isOwnMediaUrl", () => {
  it("accepts only upload-feature URLs", () => {
    expect(isOwnMediaUrl("/uploads/landing/abc.png")).toBe(true);
    expect(isOwnMediaUrl("/uploads/question-images/abc.png")).toBe(false);
    expect(isOwnMediaUrl("https://evil.test/x.png")).toBe(false);
    expect(isOwnMediaUrl("/uploads/landing/../secret.png")).toBe(false);
    expect(isOwnMediaUrl("/uploads/landing/a/b.png")).toBe(false);
    expect(isOwnMediaUrl("")).toBe(false);
    expect(isOwnMediaUrl(null)).toBe(false);
  });
});

describe("resolveSlotSources", () => {
  it("returns only the bundled default when nothing is configured", () => {
    expect(resolveSlotSources("pyp-preview", {}, { apiBase: API })).toEqual(["/assets/landing/pyp-preview.png"]);
    expect(resolveSlotSources("pyp-preview", undefined, { apiBase: API })).toEqual(["/assets/landing/pyp-preview.png"]);
    expect(resolveSlotSources("pyp-preview", null, { apiBase: API })).toEqual(["/assets/landing/pyp-preview.png"]);
  });

  it("puts the admin upload first and the slot's own default second", () => {
    const media = { "founder-sarvoch": "/uploads/landing/new.jpg" };
    expect(resolveSlotSources("founder-sarvoch", media, { apiBase: API })).toEqual([`${API}/uploads/landing/new.jpg`, "/assets/team/sarvoch.jpg"]);
  });

  it("never uses another slot's configuration or default (founders / logo stay separate)", () => {
    const media = { "founder-durgesh": "/uploads/landing/durgesh-new.jpg" };
    const sarvoch = resolveSlotSources("founder-sarvoch", media, { apiBase: API });
    expect(sarvoch).toEqual(["/assets/team/sarvoch.jpg"]);
    expect(resolveSlotSources("logo", media, { apiBase: API })).toEqual([LANDING_SLOTS.logo.default]);
  });

  it("ignores empty or invalid configured values and falls back to the default", () => {
    for (const bad of ["", "   ", "javascript:alert(1)", "https://other.test/x.png", "/uploads/other/x.png", 42, null]) {
      expect(resolveSlotSources("hero-preview", { "hero-preview": bad }, { apiBase: API })).toEqual(["/assets/landing/scoram-app-preview.png"]);
    }
  });

  it("has no candidates for the optional Smart Progress image until one is configured", () => {
    expect(resolveSlotSources("smart-progress-image", {}, { apiBase: API })).toEqual([]);
    expect(resolveSlotSources("smart-progress-image", { "smart-progress-image": "/uploads/landing/s.png" }, { apiBase: API })).toEqual([`${API}/uploads/landing/s.png`]);
  });

  it("unknown slots use the caller's fallback, if any", () => {
    expect(resolveSlotSources("nope", {}, { apiBase: API })).toEqual([]);
    expect(resolveSlotSources("nope", {}, { apiBase: API, fallbackSrc: "/x.png" })).toEqual(["/x.png"]);
  });

  it("every registered slot except the smart-progress image has a bundled default", () => {
    for (const [key, slot] of Object.entries(LANDING_SLOTS)) {
      if (key === "smart-progress-image") expect(slot.default).toBeNull();
      else expect(slot.default).toBeTruthy();
    }
  });
});

describe("toAbsoluteMediaUrl", () => {
  it("prefixes the API origin to relative paths only", () => {
    expect(toAbsoluteMediaUrl("/uploads/landing/a.png", API)).toBe(`${API}/uploads/landing/a.png`);
    expect(toAbsoluteMediaUrl("https://cdn.test/a.png", API)).toBe("https://cdn.test/a.png");
  });
});
