import { useCallback, useEffect, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import {
  ChevronDown, Clock, FileQuestion, Grid2x2, Info, List as ListIcon, Loader2,
  MinusCircle, Play, RotateCcw, Search, Sparkles, X, Eye,
} from "lucide-react";
import { listExams } from "../api/exams";
import { browsePapers, getPaperFilterOptions, getPaperYears, getMyPaperAttempts } from "../api/papers";
import { useAuth } from "../context/AuthContext";
import { timeAgo } from "../utils/format";

// MASTER PROMPT -- Previous Year Paper Practice: replaces the old "PYQ Bank" nav destination.
// Where "Find PYQs" (/search) lets a student browse individual questions, this page assembles the
// FULL original paper (legacy PYQ-upload questions + any Question Bank questions an admin mapped
// onto it, merged server-side -- see StudentPapersController.Browse/Start) and lets them attempt it
// as one real timed paper, reusing the exact same attempt engine as Mock/Practice Tests.
//
// Filters are dynamic/exam-aware (spec section 4) -- Tier/Date/Shift/Paper only render once
// StudentPapersController.GetFilterOptions says there's actually more than one value to choose
// between for the current Exam(+Year), instead of a fixed set of dropdowns forced onto every exam.
const PAGE_SIZE = 12;
const SORT_OPTIONS = [
  { value: "newest", label: "Newest First" },
  { value: "oldest", label: "Oldest First" },
  { value: "questions", label: "Most Questions" },
];
const CARD_ACCENTS = ["bg-primary-50 text-primary-600", "bg-secondary-50 text-secondary-500", "bg-accent-50 text-accent-600", "bg-mint-50 text-mint-500"];

export default function PreviousYearPapers() {
  const navigate = useNavigate();
  const { isAuthenticated } = useAuth();
  const [searchParams] = useSearchParams();
  const presetExamId = searchParams.get("examId") || "";

  const [exams, setExams] = useState([]);
  const [examId, setExamId] = useState(presetExamId);
  const [year, setYear] = useState("");
  const [tier, setTier] = useState("");
  const [examDate, setExamDate] = useState("");
  const [shift, setShift] = useState("");
  const [paperLabel, setPaperLabel] = useState("");
  const [language, setLanguage] = useState("");

  const [years, setYears] = useState([]);
  const [filterOptions, setFilterOptions] = useState({ tiers: [], examDates: [], shifts: [], paperLabels: [], languages: [] });

  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState("newest");
  const [view, setView] = useState("grid");

  const [papers, setPapers] = useState([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [papersStatus, setPapersStatus] = useState("loading");
  const [loadingMore, setLoadingMore] = useState(false);

  const [startingId, setStartingId] = useState(null);
  const [startError, setStartError] = useState("");

  const hasActiveFilters = examId || year || tier || examDate || shift || paperLabel || language || search;

  // ---------- Reference data ----------
  useEffect(() => {
    listExams().then(setExams).catch(() => setExams([]));
  }, []);

  useEffect(() => {
    if (!examId) { setYears([]); return; }
    getPaperYears(examId).then(setYears).catch(() => setYears([]));
  }, [examId]);

  useEffect(() => {
    getPaperFilterOptions({ examId: examId || undefined, year: year || undefined })
      .then(setFilterOptions)
      .catch(() => setFilterOptions({ tiers: [], examDates: [], shifts: [], paperLabels: [], languages: [] }));
  }, [examId, year]);

  // Debounce the free-text search box -- everything else re-fetches immediately on change.
  useEffect(() => {
    const handle = setTimeout(() => setSearch(searchInput), 350);
    return () => clearTimeout(handle);
  }, [searchInput]);

  // ---------- Paper grid ----------
  const fetchPapers = useCallback((pageToLoad, append) => {
    if (append) setLoadingMore(true); else setPapersStatus("loading");
    browsePapers({
      examId: examId || undefined, year: year || undefined, tier: tier || undefined,
      examDate: examDate || undefined, shift: shift || undefined, paperLabel: paperLabel || undefined,
      language: language || undefined, search: search || undefined, sort, page: pageToLoad, pageSize: PAGE_SIZE,
    })
      .then((res) => {
        setPapers((prev) => (append ? [...prev, ...res.items] : res.items));
        setTotalCount(res.totalCount);
        setPage(pageToLoad);
        setPapersStatus("success");
      })
      .catch(() => setPapersStatus("error"))
      .finally(() => setLoadingMore(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [examId, year, tier, examDate, shift, paperLabel, language, search, sort]);

  useEffect(() => {
    fetchPapers(1, false);
  }, [fetchPapers]);

  function handleClearFilters() {
    setExamId(""); setYear(""); setTier(""); setExamDate(""); setShift(""); setPaperLabel(""); setLanguage("");
    setSearchInput(""); setSearch("");
  }

  function handleStart(paper) {
    if (!isAuthenticated) {
      navigate(`/login?redirect=${encodeURIComponent("/pyq")}`);
      return;
    }
    navigate(`/tests/instructions/paper/${paper.id}`);
  }

  return (
    <div className="px-4 pb-10 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-extrabold text-ink-900 sm:text-2xl">Previous Year Paper Practice</h1>
          <p className="mt-1 text-sm text-ink-400">Attempt real previous-year papers in an exam-like environment.</p>
        </div>
        {isAuthenticated && (
          <button
            type="button"
            onClick={() => navigate("/tests/my")}
            className="flex h-10 shrink-0 items-center gap-1.5 rounded-xl2 border border-primary-100 bg-white px-3.5 text-sm font-semibold text-ink-600 shadow-card hover:border-primary-300"
          >
            My Attempts
          </button>
        )}
      </div>

      {/* ---------- Filters ---------- */}
      <div className="mt-5 rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          <Dropdown label="Exam" value={examId} onChange={(v) => { setExamId(v); setYear(""); }} placeholder="All exams">
            {exams.map((e) => <option key={e.id} value={e.id}>{e.name}</option>)}
          </Dropdown>
          <Dropdown label="Year" value={year} onChange={setYear} placeholder="All years" disabled={!examId}>
            {years.map((y) => <option key={y} value={y}>{y}</option>)}
          </Dropdown>
          {filterOptions.tiers.length > 1 && (
            <Dropdown label="Tier" value={tier} onChange={setTier} placeholder="All tiers">
              {filterOptions.tiers.map((t) => <option key={t} value={t}>{t}</option>)}
            </Dropdown>
          )}
          {filterOptions.examDates.length > 1 && (
            <Dropdown label="Date" value={examDate} onChange={setExamDate} placeholder="All dates">
              {filterOptions.examDates.map((d) => <option key={d} value={d}>{new Date(d).toLocaleDateString("en-IN", { day: "numeric", month: "short", year: "numeric" })}</option>)}
            </Dropdown>
          )}
          {filterOptions.shifts.length > 1 && (
            <Dropdown label="Shift" value={shift} onChange={setShift} placeholder="All shifts">
              {filterOptions.shifts.map((s) => <option key={s} value={s}>{s}</option>)}
            </Dropdown>
          )}
          {filterOptions.paperLabels.length > 1 && (
            <Dropdown label="Paper" value={paperLabel} onChange={setPaperLabel} placeholder="All papers">
              {filterOptions.paperLabels.map((l) => <option key={l} value={l}>{l}</option>)}
            </Dropdown>
          )}
          {filterOptions.languages.length > 1 && (
            <Dropdown label="Language" value={language} onChange={setLanguage} placeholder="All languages">
              {filterOptions.languages.map((l) => <option key={l} value={l}>{l}</option>)}
            </Dropdown>
          )}
          <label className="block">
            <span className="mb-1 block text-xs font-semibold text-ink-600">Search</span>
            <span className="relative block">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" strokeWidth={2} />
              <input
                type="text"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                placeholder="Exam, tier, paper..."
                className="h-11 w-full rounded-xl2 border border-primary-100 bg-white pl-9 pr-3 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500"
              />
            </span>
          </label>
        </div>
        {hasActiveFilters && (
          <button
            type="button"
            onClick={handleClearFilters}
            className="mt-3 flex items-center gap-1 text-xs font-semibold text-secondary-500 hover:text-secondary-600"
          >
            <X className="h-3.5 w-3.5" strokeWidth={2.5} />
            Clear filters
          </button>
        )}
      </div>

      {/* ---------- Info banner ---------- */}
      <div className="mt-4 flex items-start gap-3 rounded-xl2 border border-secondary-100 bg-secondary-50 p-4">
        <Info className="mt-0.5 h-4 w-4 shrink-0 text-secondary-500" strokeWidth={2.25} />
        <div>
          <p className="text-sm font-bold text-ink-900">What is PYP Practice?</p>
          <p className="mt-0.5 text-xs text-ink-600">
            PYP Practice lets you attempt real previous-year papers exactly as they were in the actual exam.
            You get real exam experience, time management practice, and detailed performance analysis.
          </p>
        </div>
      </div>

      {startError && (
        <p className="mt-4 rounded-xl2 border border-red-200 bg-red-50 px-4 py-3 text-sm font-medium text-red-600">
          {startError}
        </p>
      )}

      {/* ---------- Available Papers ---------- */}
      <div className="mt-6 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-base font-bold text-ink-900">
          Available Papers {papersStatus === "success" && `(${totalCount})`}
        </h2>
        <div className="flex items-center gap-2">
          <span className="relative block">
            <select
              value={sort}
              onChange={(e) => setSort(e.target.value)}
              className="h-9 appearance-none rounded-xl2 border border-primary-100 bg-white pl-3 pr-8 text-xs font-semibold text-ink-600 focus:border-secondary-500"
            >
              {SORT_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>
            <ChevronDown className="pointer-events-none absolute right-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-ink-400" strokeWidth={2} />
          </span>
          <div className="flex rounded-xl2 border border-primary-100 bg-white p-0.5">
            <button type="button" onClick={() => setView("grid")} className={`rounded-lg p-1.5 ${view === "grid" ? "bg-primary-50 text-primary-600" : "text-ink-400"}`} title="Grid view">
              <Grid2x2 className="h-4 w-4" strokeWidth={2.25} />
            </button>
            <button type="button" onClick={() => setView("list")} className={`rounded-lg p-1.5 ${view === "list" ? "bg-primary-50 text-primary-600" : "text-ink-400"}`} title="List view">
              <ListIcon className="h-4 w-4" strokeWidth={2.25} />
            </button>
          </div>
        </div>
      </div>

      <div className="mt-4">
        {papersStatus === "loading" && (
          <div className="flex items-center justify-center py-16 text-ink-400">
            <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
          </div>
        )}
        {papersStatus === "error" && (
          <EmptyState text="Couldn't load papers right now -- please try again." />
        )}
        {papersStatus === "success" && papers.length === 0 && (
          <EmptyState text="No papers match these filters yet. Try widening your search." />
        )}

        {papersStatus === "success" && papers.length > 0 && (
          <div className={view === "grid" ? "grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4" : "flex flex-col gap-3"}>
            {papers.map((paper, i) => (
              <PaperCard
                key={paper.id}
                paper={paper}
                accent={CARD_ACCENTS[i % CARD_ACCENTS.length]}
                view={view}
                starting={startingId === paper.id}
                onStart={() => handleStart(paper)}
              />
            ))}
          </div>
        )}

        {papersStatus === "success" && papers.length < totalCount && (
          <div className="mt-6 flex justify-center">
            <button
              type="button"
              onClick={() => fetchPapers(page + 1, true)}
              disabled={loadingMore}
              className="flex items-center gap-1.5 rounded-xl2 border border-primary-100 bg-white px-4 py-2.5 text-sm font-semibold text-ink-600 shadow-card hover:border-primary-300 disabled:opacity-60"
            >
              {loadingMore && <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.25} />}
              Load More Papers
              <ChevronDown className="h-4 w-4" strokeWidth={2.25} />
            </button>
          </div>
        )}
      </div>

      {isAuthenticated && <MyAttemptsSection />}

      <button
        type="button"
        onClick={() => navigate("/search")}
        className="mt-8 flex items-center gap-1.5 text-sm font-semibold text-secondary-500 hover:text-secondary-600"
      >
        <Search className="h-4 w-4" strokeWidth={2.25} />
        Looking for a specific question instead? Search individual PYQs
      </button>
    </div>
  );
}

function PaperCard({ paper, accent, view, starting, onStart }) {
  const title = [paper.examName, paper.year].filter(Boolean).join(" ");
  const subtitle = [paper.tier, paper.paperLabel].filter(Boolean).join(" \u00b7 ");
  const dateLine = [
    paper.examDate ? new Date(paper.examDate).toLocaleDateString("en-IN", { day: "numeric", month: "short", year: "numeric" }) : null,
    paper.shift,
  ].filter(Boolean).join(" \u00b7 ");
  const attemptable = paper.isConfiguredForPractice && paper.isComplete;
  const isNew = paper.publishedAt && (Date.now() - new Date(paper.publishedAt).getTime()) < 14 * 24 * 60 * 60 * 1000;

  if (view === "list") {
    return (
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
        <div className="flex min-w-0 items-center gap-3">
          <span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full ${accent}`}>
            <FileQuestion className="h-4 w-4" strokeWidth={2.25} />
          </span>
          <div className="min-w-0">
            <p className="truncate text-sm font-bold text-ink-900">
              {title}{subtitle && <span className="font-medium text-ink-400"> · {subtitle}</span>}
            </p>
            <p className="mt-0.5 text-xs text-ink-400">
              {dateLine}{dateLine && " · "}{paper.questionCount} Q · {paper.durationMinutes ? `${paper.durationMinutes} min` : "--"}
            </p>
          </div>
        </div>
        <StartButton attemptable={attemptable} configured={paper.isConfiguredForPractice} starting={starting} onStart={onStart} compact />
      </div>
    );
  }

  return (
    <div className="relative flex flex-col rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
      {isNew && (
        <span className="absolute left-4 top-4 flex items-center gap-1 rounded-full bg-mint-50 px-2 py-0.5 text-[10px] font-bold text-mint-600">
          <Sparkles className="h-2.5 w-2.5" strokeWidth={2.5} />
          New
        </span>
      )}
      <div className={`flex h-11 w-11 items-center justify-center rounded-full ${accent} ${isNew ? "mt-6" : ""}`}>
        <FileQuestion className="h-5 w-5" strokeWidth={2.25} />
      </div>

      <p className="mt-3 text-sm font-bold text-ink-900">{title}</p>
      {subtitle && <p className="text-xs font-semibold text-ink-400">{subtitle}</p>}
      {dateLine && <p className="mt-1 text-xs text-ink-400">{dateLine}</p>}

      <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1.5 text-xs font-medium text-ink-400">
        <span className="flex items-center gap-1"><FileQuestion className="h-3.5 w-3.5" strokeWidth={2.25} />{paper.questionCount} Questions</span>
        {paper.durationMinutes && (
          <span className="flex items-center gap-1"><Clock className="h-3.5 w-3.5" strokeWidth={2.25} />{paper.durationMinutes} min</span>
        )}
        {paper.negativeMarkingRatio != null && (
          <span className="flex items-center gap-1"><MinusCircle className="h-3.5 w-3.5" strokeWidth={2.25} />-{paper.negativeMarkingRatio} per wrong</span>
        )}
      </div>

      <div className="mt-4 flex-1" />
      <StartButton attemptable={attemptable} configured={paper.isConfiguredForPractice} starting={starting} onStart={onStart} />
    </div>
  );
}

function StartButton({ attemptable, configured, starting, onStart, compact }) {
  if (!configured) {
    return (
      <p className={`rounded-xl bg-primary-50 text-center text-xs font-medium text-ink-400 ${compact ? "px-3 py-2" : "px-3 py-2.5"}`}>
        Not set up for timed practice yet -- browse via Find PYQs instead.
      </p>
    );
  }
  if (!attemptable) {
    return (
      <p className={`rounded-xl bg-primary-50 text-center text-xs font-medium text-ink-400 ${compact ? "px-3 py-2" : "px-3 py-2.5"}`}>
        Paper is currently unavailable because all questions have not been added yet.
      </p>
    );
  }
  return (
    <button
      type="button"
      onClick={onStart}
      disabled={starting}
      className={`flex items-center justify-center gap-1.5 rounded-xl bg-primary-600 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60 ${compact ? "h-9 px-4" : "h-10"}`}
    >
      {starting ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.25} /> : <Play className="h-4 w-4" strokeWidth={2.25} />}
      Start Paper
    </button>
  );
}

// ---------- Continue Attempting / Completed Papers ----------
function MyAttemptsSection() {
  const navigate = useNavigate();
  const [tab, setTab] = useState("InProgress");
  const [data, setData] = useState({ InProgress: null, Completed: null });
  const [status, setStatus] = useState("loading");

  useEffect(() => {
    setStatus("loading");
    Promise.all([getMyPaperAttempts("InProgress"), getMyPaperAttempts("Completed")])
      .then(([inProgress, completed]) => {
        setData({ InProgress: inProgress, Completed: completed });
        setStatus("success");
      })
      .catch(() => setStatus("error"));
  }, []);

  function handleOpen(a) {
    if (a.canResume) navigate(`/tests/attempt/${a.attemptId}`);
    else navigate(`/tests/result/${a.attemptId}`);
  }

  const attempts = data[tab]?.items ?? [];

  return (
    <div className="mt-8">
      <div className="flex gap-2 border-b border-primary-100">
        {[{ key: "InProgress", label: "Continue Attempting" }, { key: "Completed", label: "Completed Papers" }].map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setTab(t.key)}
            className={`-mb-px border-b-2 px-3 py-2.5 text-sm font-semibold transition-colors ${
              tab === t.key ? "border-secondary-500 text-secondary-500" : "border-transparent text-ink-400 hover:text-ink-600"
            }`}
          >
            {t.label}{data[t.key] ? ` (${data[t.key].totalCount})` : ""}
          </button>
        ))}
      </div>

      <div className="mt-4">
        {status === "loading" && (
          <div className="flex justify-center py-10 text-ink-400">
            <Loader2 className="h-5 w-5 animate-spin" strokeWidth={2.25} />
          </div>
        )}
        {status === "success" && attempts.length === 0 && (
          <p className="rounded-xl2 border border-dashed border-primary-100 py-10 text-center text-sm text-ink-400">
            {tab === "InProgress" ? "No papers in progress right now." : "You haven't completed a paper yet."}
          </p>
        )}
        <div className="flex flex-col gap-3">
          {attempts.map((a) => (
            <button
              key={a.attemptId}
              type="button"
              onClick={() => handleOpen(a)}
              className="flex flex-wrap items-center justify-between gap-3 rounded-xl2 border border-primary-100 bg-white p-4 text-left shadow-card transition-colors hover:border-primary-300"
            >
              <div className="min-w-0">
                <p className="text-sm font-bold text-ink-900">
                  {a.examName} {a.year}{a.tier ? ` \u00b7 ${a.tier}` : ""}
                  {a.shift && <span className="ml-2 rounded-full bg-secondary-50 px-2 py-0.5 text-[11px] font-bold text-secondary-600">{a.shift}</span>}
                </p>
                <p className="mt-0.5 text-xs text-ink-400">
                  {a.examDate && `${new Date(a.examDate).toLocaleDateString("en-IN", { day: "numeric", month: "short", year: "numeric" })} \u00b7 `}
                  {a.totalQuestions} Questions{a.durationMinutes ? ` \u00b7 ${a.durationMinutes} min` : ""}
                </p>
                {a.canResume ? (
                  <p className="mt-1 text-xs font-semibold text-secondary-500">Attempted {a.answeredCount} / {a.totalQuestions}</p>
                ) : (
                  <p className="mt-1 text-xs font-semibold text-mint-600">Score: {a.score ?? 0}</p>
                )}
              </div>
              <div className="flex shrink-0 items-center gap-3">
                <span className="text-right text-xs text-ink-400">
                  Last Attempted<br />{timeAgo(a.lastActivityAt)}
                </span>
                <span className="flex h-9 items-center gap-1.5 rounded-xl bg-primary-600 px-3.5 text-xs font-semibold text-white">
                  {a.canResume ? <Play className="h-3.5 w-3.5" strokeWidth={2.25} /> : <Eye className="h-3.5 w-3.5" strokeWidth={2.25} />}
                  {a.canResume ? "Continue" : "View Result"}
                </span>
              </div>
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}

function EmptyState({ text }) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 rounded-xl2 border border-dashed border-primary-100 py-16 text-center">
      <RotateCcw className="h-6 w-6 text-ink-300" strokeWidth={2} />
      <p className="max-w-xs text-sm text-ink-400">{text}</p>
    </div>
  );
}

function Dropdown({ label, value, onChange, placeholder, disabled, children }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-semibold text-ink-600">{label}</span>
      <span className="relative block">
        <select
          value={value}
          disabled={disabled}
          onChange={(e) => onChange(e.target.value)}
          className="h-11 w-full appearance-none rounded-xl2 border border-primary-100 bg-white px-3 pr-8 text-sm text-ink-900 focus:border-secondary-500 disabled:bg-primary-50 disabled:text-ink-400"
        >
          <option value="">{placeholder}</option>
          {children}
        </select>
        <ChevronDown className="pointer-events-none absolute right-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" strokeWidth={2} />
      </span>
    </label>
  );
}
