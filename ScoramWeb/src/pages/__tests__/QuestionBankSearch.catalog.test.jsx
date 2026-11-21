// @vitest-environment jsdom
// Round 4: catalog cache wiring + the filter-change fixes of the Question Bank page. Only the network
// edge (apiFetch) and heavyweight children are faked; the page, useCatalog and the cache are real.
import { StrictMode } from "react";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { resetCatalogForTests } from "../../api/catalogCache";

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const net = vi.hoisted(() => ({ apiFetch: null }));

vi.mock("../../api/client", async (importOriginal) => ({
  ...(await importOriginal()),
  apiFetch: (...args) => net.apiFetch(...args),
}));
vi.mock("../../hooks/useMyExamsScope", () => ({
  useMyExamsScope: () => ({ isScoped: false, ready: true, isEmpty: false, exams: [], examIds: [] }),
}));
vi.mock("../../hooks/useQuestionBrowseProgress", () => ({
  useQuestionBrowseProgress: () => ({
    enabled: false, ready: true, snapshot: null, answers: {}, answeredCount: 0, syncState: "idle",
    selectOption: () => {}, startFresh: async () => {}, reportPosition: () => {},
  }),
}));
vi.mock("../../components/questions/QuestionBankFeedCard", () => ({ default: ({ question }) => <div data-testid="q">{question?.id}</div> }));
vi.mock("../../components/questions/QuestionBankFeedCardSkeleton", () => ({ default: () => <div data-testid="skeleton" /> }));
vi.mock("../../components/questions/QuestionBankSidebar", () => ({ default: () => null }));
vi.mock("../../components/ui/SearchableSelect", () => ({ default: () => null }));
vi.mock("../../components/exams/MyExamsFilter", () => ({ default: () => null }));
vi.mock("../../components/exams/ChooseMyExamsPrompt", () => ({ default: () => null }));

import QuestionBankSearch from "../QuestionBankSearch";
import { ApiError } from "../../api/client";

const observers = [];
class FakeObserver {
  constructor(cb) { this.cb = cb; observers.push(this); }
  observe() {}
  disconnect() {}
}

// A tiny fake backend. `searches` records every search request; `hold(pred)` parks matching ones.
function makeBackend() {
  const calls = [];
  const searches = [];
  const held = [];
  const rules = [];
  const respond = (path) => {
    const url = new URL(path, "http://x");
    if (url.pathname.endsWith("/search")) {
      const q = url.searchParams.get("q") ?? url.searchParams.get("search") ?? "";
      const page = Number(url.searchParams.get("page"));
      const pageSize = Number(url.searchParams.get("pageSize"));
      const filtered = url.searchParams.has("search") && q !== "";
      const totalCount = filtered ? 12 : 25;
      return { items: Array.from({ length: Math.min(pageSize, 3) }, (_, i) => ({ id: `${filtered ? q : "all"}-p${page}-${i}` })), totalCount, page, pageSize };
    }
    if (url.pathname.endsWith("/exams")) return [{ id: "e1", name: "SSC" }, { id: "e2", name: "RRB" }];
    if (url.pathname.endsWith("/years")) return [2024, 2023, 2022];
    if (url.pathname.endsWith("/subjects")) return [{ id: "s1", name: "Math" }];
    if (url.pathname.endsWith("/topics")) return [];
    throw new Error(`unexpected ${path}`);
  };
  const fetch = (path, opts = {}) => {
    const signal = opts.signal;
    calls.push({ path, signal });
    const isSearch = path.includes("/search");
    if (isSearch) searches.push({ path, signal });
    return new Promise((resolve, reject) => {
      const finish = (ignoreAbort) => {
        if (signal?.aborted && !ignoreAbort) return reject(new ApiError("timed out", 0, { timedOut: true }));
        try { resolve(respond(path)); } catch (e) { reject(e); }
      };
      const rule = rules.find((r) => r.match(path));
      if (rule) { held.push({ path, release: () => finish(rule.ignoreAbort), signal }); }
      else Promise.resolve().then(() => finish(false));
      signal?.addEventListener("abort", () => {
        if (!held.some((h) => h.path === path && h.signal === signal)) return;
        if (!rule?.ignoreAbort) reject(new ApiError("timed out", 0, { timedOut: true }));
      });
    });
  };
  return { fetch, calls, searches, held, hold: (match, { ignoreAbort = false } = {}) => rules.push({ match, ignoreAbort }) };
}

function mount(initial = "/question-bank", { strict = false } = {}) {
  const tree = <MemoryRouter initialEntries={[initial]}><QuestionBankSearch /></MemoryRouter>;
  return render(strict ? <StrictMode>{tree}</StrictMode> : tree);
}

const sizeOf = (path) => new URL(path, "http://x").searchParams.get("pageSize");

beforeEach(() => {
  localStorage.clear();
  resetCatalogForTests();
  observers.length = 0;
  vi.stubGlobal("IntersectionObserver", FakeObserver);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("Question Bank catalog loading", () => {
  it("plain open: no pageSize=1 request, total comes from the first page, each catalog list once (StrictMode)", async () => {
    const be = makeBackend();
    net.apiFetch = be.fetch;
    mount("/question-bank", { strict: true });

    await waitFor(() => expect(screen.getAllByTestId("q").length).toBeGreaterThan(0));
    await waitFor(() => expect(screen.getByText("25")).toBeTruthy()); // "Questions" stat from the page's totalCount
    await act(async () => { await new Promise((r) => setTimeout(r, 400)); }); // let any late request show up

    expect(be.searches.some((s) => sizeOf(s.path) === "1")).toBe(false);
    const count = (frag) => be.calls.filter((c) => c.path.includes(frag)).length;
    expect(count("/api/question-bank/exams")).toBe(1);
    expect(count("/api/question-bank/years")).toBe(1);
    expect(count("/api/question-bank/subjects")).toBe(1);
  });

  it("a revisit within 3 minutes makes no catalog request at all", async () => {
    const be = makeBackend();
    net.apiFetch = be.fetch;
    const first = mount();
    await waitFor(() => expect(screen.getByText("25")).toBeTruthy());
    first.unmount();

    const before = be.calls.length;
    mount();
    expect(screen.getByText("25")).toBeTruthy(); // painted synchronously from the cache
    await waitFor(() => expect(screen.getAllByTestId("q").length).toBeGreaterThan(0));
    const catalogCalls = be.calls.slice(before).filter((c) => !c.path.includes("/search"));
    expect(catalogCalls).toEqual([]);
  });

  it("arriving with a filter: one single-row total request, made only after the first page has settled", async () => {
    const be = makeBackend();
    net.apiFetch = be.fetch;
    mount("/question-bank?q=maths");

    await waitFor(() => expect(screen.getAllByTestId("q").length).toBeGreaterThan(0));
    await waitFor(() => expect(screen.getByText("25")).toBeTruthy()); // global total, not the filtered 12
    const singles = be.searches.filter((s) => sizeOf(s.path) === "1");
    expect(singles).toHaveLength(1);
    const pageIdx = be.searches.findIndex((s) => sizeOf(s.path) === "10");
    expect(be.searches.indexOf(singles[0])).toBeGreaterThan(pageIdx);
  });
});

describe("Question Bank filter changes", () => {
  it("a superseded request is aborted and never flashes the error panel", async () => {
    const be = makeBackend();
    be.hold((p) => p.includes("search=a&") || p.endsWith("search=a") || /[?&]search=a(&|$)/.test(p));
    net.apiFetch = be.fetch;
    mount();
    await waitFor(() => expect(screen.getAllByTestId("q").length).toBeGreaterThan(0));

    const user = userEvent.setup();
    const input = screen.getByPlaceholderText(/Search by keyword/);
    await user.type(input, "a");
    await waitFor(() => expect(be.held.length).toBe(1)); // the "a" request is now in flight
    const aSignal = be.held[0].signal;

    await user.type(input, "b"); // filters change while it is still running
    await waitFor(() => expect(aSignal.aborted).toBe(true));
    // The aborted request has just been rejected; the replacement load is still inside its 250 ms
    // debounce. This is the window in which the old code flashed the error panel.
    await act(async () => { await Promise.resolve(); await Promise.resolve(); });
    expect(screen.queryByText(/Something went wrong/)).toBeNull();
    await waitFor(() => expect(screen.getAllByTestId("q")[0].textContent).toMatch(/^ab-p1/));
    expect(screen.queryByText(/Something went wrong/)).toBeNull();
  });

  it("a late 'load more' from the old filters is not appended to the new list", async () => {
    const be = makeBackend();
    // page 2 of the unfiltered list hangs and, when released later, answers even though it was aborted
    be.hold((p) => /[?&]page=2(&|$)/.test(p) && !/[?&]search=/.test(p), { ignoreAbort: true });
    net.apiFetch = be.fetch;
    mount();
    await waitFor(() => expect(screen.getAllByTestId("q").length).toBe(3));

    act(() => { observers[observers.length - 1].cb([{ isIntersecting: true }]); }); // scroll to the end
    await waitFor(() => expect(be.held.length).toBe(1));

    const user = userEvent.setup();
    await user.type(screen.getByPlaceholderText(/Search by keyword/), "z");
    await waitFor(() => expect(screen.getAllByTestId("q")[0].textContent).toMatch(/^z-p1/));

    await act(async () => { be.held[0].release(); await new Promise((r) => setTimeout(r, 20)); });
    const ids = screen.getAllByTestId("q").map((n) => n.textContent);
    expect(ids.every((id) => id.startsWith("z-"))).toBe(true);
    expect(screen.queryByText(/Something went wrong/)).toBeNull();
  });

  it("a genuine failure still shows the error panel", async () => {
    const be = makeBackend();
    net.apiFetch = (path, opts) => (path.includes("/search")
      ? Promise.reject(new ApiError("Server error", 500, null))
      : be.fetch(path, opts));
    mount();
    await waitFor(() => expect(screen.getByText(/Something went wrong/)).toBeTruthy());
  });
});
