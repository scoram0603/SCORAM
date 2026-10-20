import { describe, expect, it } from "vitest";
import { validateImageFile, formatBytes } from "../landingMedia";

const file = (name, size) => ({ name, size });

describe("validateImageFile", () => {
  it("accepts png/jpg/jpeg/webp under the limit (case-insensitive)", () => {
    for (const n of ["a.png", "a.JPG", "a.jpeg", "a.WebP"]) expect(validateImageFile(file(n, 1000))).toBeNull();
  });
  it("rejects other types, svg, double extensions and missing extension", () => {
    for (const n of ["a.svg", "a.gif", "a.exe", "a.png.exe", "noext", "a.pdf"]) expect(validateImageFile(file(n, 1000))).toBeTruthy();
  });
  it("rejects empty and oversized files", () => {
    expect(validateImageFile(file("a.png", 0))).toBeTruthy();
    expect(validateImageFile(file("a.png", 3 * 1024 * 1024 + 1))).toMatch(/maximum/i);
    expect(validateImageFile(file("a.png", 3 * 1024 * 1024))).toBeNull();
  });
  it("requires a file", () => expect(validateImageFile(null)).toBeTruthy());
  it("formats sizes", () => {
    expect(formatBytes(2048)).toBe("2 KB");
    expect(formatBytes(3 * 1024 * 1024)).toBe("3.0 MB");
  });
});
