import { describe, expect, it } from "vitest";
import { PERMISSIONS, PERMISSION_GROUPS, ROLE_PRESETS } from "../permissions";

describe("permission catalogue", () => {
  it("has unique keys", () => {
    const keys = PERMISSIONS.map((p) => p.key);
    expect(new Set(keys).size).toBe(keys.length);
  });
  it("every permission belongs to a known group", () => {
    PERMISSIONS.forEach((p) => expect(PERMISSION_GROUPS).toContain(p.group));
  });
  it("every preset only references real permissions", () => {
    const keys = new Set(PERMISSIONS.map((p) => p.key));
    ROLE_PRESETS.forEach((preset) => preset.keys.forEach((k) => expect(keys.has(k), `${preset.name}: ${k}`).toBe(true)));
  });
  it("includes the admin-platform permissions", () => {
    const keys = PERMISSIONS.map((p) => p.key);
    ["ViewAnalytics", "ExportReports", "ManageUsers", "ManageAppConfig"].forEach((k) => expect(keys).toContain(k));
  });
});
