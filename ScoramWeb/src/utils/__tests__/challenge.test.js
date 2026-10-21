import { describe, expect, it } from "vitest";
import {
  continuePath, durationOf, formatMinutes, parseDuration, parseQuestions, progressCaption, sides, splitMinutes, timeLeft,
} from "../challenge";

describe("challenge helpers", () => {
  it("formats minutes", () => {
    expect(formatMinutes(1)).toBe("1 min");
    expect(formatMinutes(90)).toBe("1 h 30 min");
    expect(formatMinutes(120)).toBe("2 h");
    expect(formatMinutes(1440)).toBe("1 day");
    expect(formatMinutes(14400)).toBe("10 days");
    expect(formatMinutes(1500)).toBe("1 day 1 h");
  });

  it("accepts 1 minute to 30 days and nothing outside", () => {
    expect(parseDuration("1", "minutes")).toEqual({ minutes: 1 });
    expect(parseDuration("1", "hours")).toEqual({ minutes: 60 });
    expect(parseDuration("10", "days")).toEqual({ minutes: 14400 });
    expect(parseDuration("30", "days")).toEqual({ minutes: 43200 });
    expect(parseDuration("31", "days").error).toBeTruthy();
    expect(parseDuration("720", "hours")).toEqual({ minutes: 43200 });
    expect(parseDuration("721", "hours").error).toBeTruthy();
    expect(parseDuration("0", "minutes").error).toBeTruthy();
    expect(parseDuration("", "minutes").error).toBeTruthy();
    expect(parseDuration("1.5", "hours").error).toBeTruthy();
    expect(parseDuration("-3", "days").error).toBeTruthy();
  });

  it("validates question counts", () => {
    expect(parseQuestions("1")).toEqual({ count: 1 });
    expect(parseQuestions("1000")).toEqual({ count: 1000 });
    expect(parseQuestions("0").error).toBeTruthy();
    expect(parseQuestions("1001").error).toBeTruthy();
    expect(parseQuestions("abc").error).toBeTruthy();
  });

  it("round-trips minutes for a rematch", () => {
    expect(splitMinutes(14400)).toEqual({ value: "10", unit: "days" });
    expect(splitMinutes(180)).toEqual({ value: "3", unit: "hours" });
    expect(splitMinutes(45)).toEqual({ value: "45", unit: "minutes" });
  });

  it("falls back to days for old challenges", () => {
    expect(durationOf({ durationDays: 7 })).toBe(10080);
    expect(durationOf({ durationMinutes: 15, durationDays: 1 })).toBe(15);
  });

  it("time left", () => {
    const now = Date.parse("2026-10-21T10:00:00Z");
    expect(timeLeft("2026-10-21T12:10:00Z", now)).toBe("2 h 10 min left");
    expect(timeLeft("2026-10-21T10:00:30Z", now)).toBe("1 min left");
    expect(timeLeft("2026-10-21T09:00:00Z", now)).toBe("Ended");
    expect(timeLeft(null, now)).toBe("");
  });

  it("picks my side and captions", () => {
    const c = { iAmCreator: false, type: "Practice", questionCount: 50, creator: { fullName: "A" }, partner: { fullName: "B" },
      creatorProgress: { answered: 10 }, partnerProgress: { answered: 32 } };
    expect(sides(c).mine.answered).toBe(32);
    expect(sides(c).other.fullName).toBe("A");
    expect(progressCaption(c, sides(c).mine)).toBe("32/50 questions");
    expect(progressCaption({ type: "Streak", targetDays: 5 }, { activeDays: 3 })).toBe("3/5 days");
    expect(progressCaption({ type: "Speed", questionCount: 40 }, { bestAttemptAnswered: 12, fastestSeconds: null })).toBe("best 12/40 in one attempt");
    expect(progressCaption(c, null)).toBe("Not started");
  });

  it("continue path", () => {
    expect(continuePath({ exam: { id: "abc" } })).toBe("/tests/practice?examId=abc");
    expect(continuePath({})).toBe("/tests");
  });
});
