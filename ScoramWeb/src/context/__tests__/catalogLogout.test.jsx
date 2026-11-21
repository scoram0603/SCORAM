// @vitest-environment jsdom
// Logout / account deletion must drop the student's cached, My-Exams-scoped catalog.
import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { catalogKey, peekCatalog, resetCatalogForTests, setCatalog } from "../../api/catalogCache";

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const api = vi.hoisted(() => ({ logout: vi.fn(), deleteAccount: vi.fn(), getMe: vi.fn() }));
vi.mock("../../api/auth", () => ({ ...api, default: api }));

import { AuthProvider, useAuth } from "../AuthContext";

const KEY = () => catalogKey("qb:exams");

beforeEach(() => {
  localStorage.clear();
  resetCatalogForTests();
  Object.values(api).forEach((f) => f.mockReset());
  api.logout.mockResolvedValue(null);
  api.getMe.mockResolvedValue({});
});
afterEach(() => cleanup());

describe("logout -> catalog cache", () => {
  it("logout clears the cached catalog", async () => {
    const { result } = renderHook(() => useAuth(), { wrapper: AuthProvider });
    setCatalog(KEY(), ["student-1"]);
    await act(async () => { result.current.logout(); });
    expect(peekCatalog(KEY())).toBeUndefined();
  });

  it("deleteAccount clears the cached catalog", async () => {
    api.deleteAccount.mockResolvedValue(null);
    const { result } = renderHook(() => useAuth(), { wrapper: AuthProvider });
    setCatalog(KEY(), ["student-1"]);
    await act(async () => { await result.current.deleteAccount({ password: "x" }); });
    expect(peekCatalog(KEY())).toBeUndefined();
  });
});
