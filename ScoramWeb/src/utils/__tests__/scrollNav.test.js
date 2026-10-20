import { describe, expect, it } from "vitest";
import { isExternalHref, isSamePageTopClick, normalizeLandingHref, scrollDecision } from "../scrollNav";

describe("normalizeLandingHref", () => {
  it("maps in-page anchors onto the landing route", () => {
    expect(normalizeLandingHref("#top")).toBe("/");
    expect(normalizeLandingHref("#faq")).toBe("/#faq");
  });
  it("leaves routes and external links alone", () => {
    expect(normalizeLandingHref("/pyq")).toBe("/pyq");
    expect(normalizeLandingHref("mailto:info@scoram.in")).toBe("mailto:info@scoram.in");
  });
});

describe("isExternalHref", () => {
  it("detects links that must not go through the router", () => {
    expect(isExternalHref("mailto:a@b.c")).toBe(true);
    expect(isExternalHref("tel:+911234")).toBe(true);
    expect(isExternalHref("https://x.y")).toBe(true);
    expect(isExternalHref("/pyq")).toBe(false);
    expect(isExternalHref("/#faq")).toBe(false);
  });
});

describe("scrollDecision", () => {
  it("opening a different route starts at the top", () => {
    expect(scrollDecision({ prevPathname: "/", pathname: "/pyq", hash: "", navType: "PUSH" })).toEqual({ type: "top" });
    expect(scrollDecision({ prevPathname: "/tests", pathname: "/", hash: "", navType: "PUSH" })).toEqual({ type: "top" });
  });
  it("anchors scroll to their target, including when arriving from another route", () => {
    expect(scrollDecision({ prevPathname: "/our-team", pathname: "/", hash: "#faq", navType: "PUSH" })).toEqual({ type: "hash", id: "faq" });
    expect(scrollDecision({ prevPathname: "/", pathname: "/", hash: "#features", navType: "PUSH" })).toEqual({ type: "hash", id: "features" });
  });
  it("#top means the top of the page", () => {
    expect(scrollDecision({ prevPathname: "/", pathname: "/", hash: "#top", navType: "PUSH" })).toEqual({ type: "top" });
  });
  it("direct load / refresh without a hash is left to the browser", () => {
    expect(scrollDecision({ prevPathname: null, pathname: "/pyq", hash: "", navType: "POP" })).toEqual({ type: "none" });
  });
  it("direct load with a hash still scrolls to it", () => {
    expect(scrollDecision({ prevPathname: null, pathname: "/", hash: "#faq", navType: "POP" })).toEqual({ type: "hash", id: "faq" });
  });
  it("back/forward keeps the browser's own restoration", () => {
    expect(scrollDecision({ prevPathname: "/pyq", pathname: "/", hash: "", navType: "POP" })).toEqual({ type: "none" });
  });
  it("query-string-only changes (filters) do not jump the page", () => {
    expect(scrollDecision({ prevPathname: "/search", pathname: "/search", hash: "", navType: "REPLACE" })).toEqual({ type: "none" });
    expect(scrollDecision({ prevPathname: "/search", pathname: "/search", hash: "", navType: "PUSH" })).toEqual({ type: "none" });
  });
  it("decodes encoded ids and survives malformed ones", () => {
    expect(scrollDecision({ prevPathname: "/", pathname: "/", hash: "#a%20b", navType: "PUSH" })).toEqual({ type: "hash", id: "a b" });
    expect(scrollDecision({ prevPathname: "/", pathname: "/", hash: "#%E0%A4%A", navType: "PUSH" }).type).toBe("hash");
  });
});

describe("isSamePageTopClick", () => {
  it("is true only for a hash-less link to the current path", () => {
    expect(isSamePageTopClick("/", "/")).toBe(true);
    expect(isSamePageTopClick("/our-team", "/")).toBe(false);
    expect(isSamePageTopClick("/", "/#faq")).toBe(false);
    expect(isSamePageTopClick("/", "/?mode=x")).toBe(false);
  });
});
