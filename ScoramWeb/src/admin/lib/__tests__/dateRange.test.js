import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { resolvePreset, toIsoDate, validateCustom, describeRange } from "../dateRange";

describe("dateRange presets", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 9, 16, 22, 30)); // 16 Oct 2026, local evening
  });
  afterEach(() => vi.useRealTimers());

  it("today / yesterday are single local days", () => {
    expect(resolvePreset("today")).toMatchObject({ from: "2026-10-16", to: "2026-10-16" });
    expect(resolvePreset("yesterday")).toMatchObject({ from: "2026-10-15", to: "2026-10-15" });
  });

  it("last N days is inclusive of today", () => {
    expect(resolvePreset("7d")).toMatchObject({ from: "2026-10-10", to: "2026-10-16" });
    expect(resolvePreset("30d")).toMatchObject({ from: "2026-09-17", to: "2026-10-16" });
    expect(resolvePreset("90d")).toMatchObject({ from: "2026-07-19", to: "2026-10-16" });
  });

  it("this month and previous month", () => {
    expect(resolvePreset("thisMonth")).toMatchObject({ from: "2026-10-01", to: "2026-10-16" });
    expect(resolvePreset("prevMonth")).toMatchObject({ from: "2026-09-01", to: "2026-09-30" });
  });

  it("previous month works across a year boundary", () => {
    vi.setSystemTime(new Date(2027, 0, 5));
    expect(resolvePreset("prevMonth")).toMatchObject({ from: "2026-12-01", to: "2026-12-31" });
  });

  it("sends the offset in minutes AHEAD of UTC (sign flipped from getTimezoneOffset)", () => {
    const spy = vi.spyOn(Date.prototype, "getTimezoneOffset").mockReturnValue(-330);
    expect(resolvePreset("today").tzOffsetMinutes).toBe(330);
    spy.mockRestore();
  });

  it("custom keeps the supplied dates", () => {
    expect(resolvePreset("custom", { from: "2026-01-01", to: "2026-01-31" })).toMatchObject({ key: "custom", from: "2026-01-01", to: "2026-01-31" });
  });
});

describe("validateCustom", () => {
  beforeEach(() => { vi.useFakeTimers(); vi.setSystemTime(new Date(2026, 9, 16)); });
  afterEach(() => vi.useRealTimers());

  it("accepts a normal range", () => expect(validateCustom("2026-10-01", "2026-10-16")).toBeNull());
  it("requires both dates", () => expect(validateCustom("", "2026-10-16")).toMatch(/both/i));
  it("rejects reversed ranges", () => expect(validateCustom("2026-10-16", "2026-10-01")).toMatch(/on or before/i));
  it("rejects the future", () => expect(validateCustom("2026-10-01", "2026-10-17")).toMatch(/future/i));
  it("rejects ranges over a year", () => expect(validateCustom("2025-01-01", "2026-10-16")).toMatch(/at most/i));
});

describe("helpers", () => {
  it("toIsoDate pads", () => expect(toIsoDate(new Date(2026, 0, 5))).toBe("2026-01-05"));
  it("describeRange collapses a single day", () => {
    expect(describeRange({ from: "2026-10-16", to: "2026-10-16" })).toBe("2026-10-16");
    expect(describeRange({ from: "2026-10-01", to: "2026-10-16" })).toBe("2026-10-01 → 2026-10-16");
  });
});
