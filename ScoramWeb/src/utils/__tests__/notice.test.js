import { describe, expect, it } from "vitest";
import { formatNoticeTime, isNotice, noticeDestination, parseServerDate } from "../notice";

describe("notice helpers", () => {
  it("recognises admin notices only", () => {
    expect(isNotice({ type: "SystemAnnouncement" })).toBe(true);
    expect(isNotice({ type: "DirectMessage" })).toBe(false);
    expect(isNotice(null)).toBe(false);
  });

  it("maps the whitelisted destination key to a real website route", () => {
    expect(noticeDestination({ type: "SystemAnnouncement", entityType: "Screen", entityId: "MockTests" }).path).toBe("/tests/mock");
    expect(noticeDestination({ type: "SystemAnnouncement", entityType: "Screen", entityId: "Pyp" }).path).toBe("/pyq");
  });

  it("offers no action for Notifications / unknown keys (never a made-up route)", () => {
    expect(noticeDestination({ type: "SystemAnnouncement", entityType: "Screen", entityId: "Notifications" })).toBeNull();
    expect(noticeDestination({ type: "SystemAnnouncement", entityType: "Screen", entityId: "../admin" })).toBeNull();
    expect(noticeDestination({ type: "SystemAnnouncement", entityType: "", entityId: "" })).toBeNull();
  });

  it("treats zone-less server timestamps as UTC", () => {
    expect(parseServerDate("2026-10-18T10:00:00").toISOString()).toBe("2026-10-18T10:00:00.000Z");
    expect(parseServerDate("2026-10-18T10:00:00Z").toISOString()).toBe("2026-10-18T10:00:00.000Z");
    expect(parseServerDate("not a date")).toBeNull();
    expect(formatNoticeTime(undefined)).toBe("");
  });
});
