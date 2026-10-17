import { describe, expect, it } from "vitest";
import { formatKpiValue } from "../format";

describe("formatKpiValue", () => {
  it("shows a dash for missing values (never a fake zero)", () => {
    expect(formatKpiValue(null, "count")).toBe("—");
    expect(formatKpiValue(undefined, "percent")).toBe("—");
  });
  it("formats counts with Indian grouping", () => expect(formatKpiValue(1234567, "count")).toBe("12,34,567"));
  it("formats percentages to one decimal", () => expect(formatKpiValue(61.234, "percent")).toBe("61.2%"));
  it("formats minutes, hours and seconds", () => {
    expect(formatKpiValue(0.5, "minutes")).toBe("30 s");
    expect(formatKpiValue(12.4, "minutes")).toBe("12 min");
    expect(formatKpiValue(3.25, "minutes")).toBe("3.3 min");
    expect(formatKpiValue(150, "minutes")).toBe("2.5 h");
  });
});
