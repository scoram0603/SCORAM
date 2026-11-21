// @vitest-environment jsdom
// Catalog cache <-> contexts: My Exams changes and logout must drop the cached, My-Exams-scoped catalog.
import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { catalogKey, peekCatalog, resetCatalogForTests, setCatalog } from "../../api/catalogCache";

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const api = vi.hoisted(() => ({
  getMyExams: vi.fn(), setMyExams: vi.fn(), addMyExam: vi.fn(), removeMyExam: vi.fn(), setPrimaryExam: vi.fn(),
  logout: vi.fn(), deleteAccount: vi.fn(), getMe: vi.fn(),
}));
vi.mock("../../api/myExams", () => api);
vi.mock("../../api/auth", () => ({ ...api, default: api }));
vi.mock("../AuthContext", async (importOriginal) => ({
  ...(await importOriginal()),
  useAuth: () => ({ isAuthenticated: true, user: { userId: "u1" } }),
}));

import { MyExamsProvider, useMyExams } from "../MyExamsContext";

const exam = (id) => ({ examId: id, examName: id, examLogoUrl: null });
const KEY = () => catalogKey("qb:exams");

beforeEach(() => {
  localStorage.clear();
  resetCatalogForTests();
  Object.values(api).forEach((f) => f.mockReset());
  api.logout.mockResolvedValue(null);
});
afterEach(() => cleanup());

async function mountMyExams(initial) {
  api.getMyExams.mockResolvedValue({ exams: initial, primaryExamId: initial[0]?.examId ?? null });
  const hook = renderHook(() => useMyExams(), { wrapper: MyExamsProvider });
  await waitFor(() => expect(hook.result.current.hasLoaded).toBe(true));
  return hook;
}

describe("My Exams -> catalog cache", () => {
  it("the first load only records the baseline (does not drop what screens already cached)", async () => {
    setCatalog(KEY(), ["cached-before-load"]);
    await mountMyExams([exam("a")]);
    expect(peekCatalog(KEY())).toEqual(["cached-before-load"]);
  });

  it("refresh() drops the catalog only when the exam set really changed", async () => {
    const { result } = await mountMyExams([exam("a")]);
    setCatalog(KEY(), ["x"]);

    api.getMyExams.mockResolvedValue({ exams: [exam("a")], primaryExamId: "a" });
    await act(async () => { await result.current.refresh(); });
    expect(peekCatalog(KEY())).toEqual(["x"]); // same set -> kept

    api.getMyExams.mockResolvedValue({ exams: [exam("a"), exam("b")], primaryExamId: "a" });
    await act(async () => { await result.current.refresh(); });
    expect(peekCatalog(KEY())).toBeUndefined();
  });

  it.each([
    ["save", (r) => { api.setMyExams.mockResolvedValue({ exams: [exam("b")], primaryExamId: "b" }); return r.save({ examIds: ["b"], primaryExamId: "b" }); }],
    ["addExam", (r) => { api.addMyExam.mockResolvedValue({ exams: [exam("a"), exam("b")], primaryExamId: "a" }); return r.addExam("b"); }],
    ["setPrimary", (r) => { api.setPrimaryExam.mockResolvedValue({ exams: [exam("a")], primaryExamId: "a" }); return r.setPrimary("a"); }],
  ])("%s drops the catalog", async (_name, run) => {
    const { result } = await mountMyExams([exam("a")]);
    setCatalog(KEY(), ["x"]);
    await act(async () => { await run(result.current); });
    expect(peekCatalog(KEY())).toBeUndefined();
  });

  it("removeExam (which refreshes) drops the catalog when the set shrinks", async () => {
    const { result } = await mountMyExams([exam("a"), exam("b")]);
    setCatalog(KEY(), ["x"]);
    api.removeMyExam.mockResolvedValue(null);
    api.getMyExams.mockResolvedValue({ exams: [exam("a")], primaryExamId: "a" });
    await act(async () => { await result.current.removeExam("b"); });
    expect(peekCatalog(KEY())).toBeUndefined();
  });
});
