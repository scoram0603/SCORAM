import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Search, Loader2, ServerCrash, Inbox, ChevronDown, X, BookOpen, GraduationCap, Layers,
  CalendarDays, Shuffle, History, LayoutList, Rows3,
} from "lucide-react";
import { useSearchParams } from "react-router-dom";
import { searchQuestionBank, getQuestionBankSubjects, getQuestionBankTopics, getQuestionBankExams, getQuestionBankYears } from "../api/questionBank";
import QuestionBankFeedCard from "../components/questions/QuestionBankFeedCard";
import QuestionBankFeedCardSkeleton from "../components/questions/QuestionBankFeedCardSkeleton";
import QuestionBankSidebar from "../components/questions/QuestionBankSidebar";

const PAGE_SIZE = 10;
const RECENT_SEARCHES_KEY = "scoram:qb:recent-searches";

function readRecentSearches() {
  try {
    return JSON.parse(localStorage.getItem(RECENT_SEARCHES_KEY) || "[]");
  } catch {
    return [];
  }
}

function pushRecentSearch(term) {
  const clean = term.trim();
  if (!clean) return;
  try {
    const existing = readRecentSearches().filter((t) => t.toLowerCase() !== clean.toLowerCase());
    localStorage.setItem(RECENT_SEARCHES_KEY, JSON.stringify([clean, ...existing].slice(0, 6)));
  } catch {
    // localStorage unavailable (private browsing, quota) -- recent searches just won't persist
  }
}

// FEED REDESIGN + PREMIUM UI PASS -- one question per row, everything (View Answer, Discuss,
// Like/Dislike, Share) inline on the card (see QuestionBankFeedCard), infinite scroll, and a
// premium header with real stat counts + quick actions. Filter/search state lives in the URL
// (searchParams) so a result is shareable/bookmarkable. Every number and action here is backed by
// a real API or a real client-side mechanism (localStorage recent searches, a genuinely random
// question) -- no fabricated "trending topics" or usage analytics the backend doesn't track.
export default function QuestionBankSearch() {
  const [searchParams, setSearchParams] = useSearchParams();

  const search = searchParams.get("q") || "";
  const subjectId = searchParams.get("subjectId") || "";
  const topicId = searchParams.get("topicId") || "";
  const examId = searchParams.get("examId") || "";
  const year = searchParams.get("year") || "";
  const language = searchParams.get("language") || "";

  const [subjects, setSubjects] = useState([]);
  const [topics, setTopics] = useState([]);
  const [exams, setExams] = useState([]);
  const [years, setYears] = useState([]);
  const [totalQuestionCount, setTotalQuestionCount] = useState(null);

  const [items, setItems] = useState([]);
  const [totalCount, setTotalCount] = useState(0);
  const [status, setStatus] = useState("loading"); // loading | loading-more | success | error
  const [hasMore, setHasMore] = useState(true);
  const [view, setView] = useState("list"); // list | compact
  const [recentSearches, setRecentSearches] = useState(readRecentSearches);
  const [shuffling, setShuffling] = useState(false);
  const [shuffled, setShuffled] = useState(null);

  const pageRef = useRef(1);
  const debounceRef = useRef(null);
  const sentinelRef = useRef(null);
  const searchInputRef = useRef(null);

  useEffect(() => {
    getQuestionBankSubjects().then(setSubjects).catch(() => {});
    getQuestionBankExams().then(setExams).catch(() => {});
    getQuestionBankYears().then(setYears).catch(() => {});
    // Real total, unfiltered -- powers the "X Questions" stat card regardless of active filters.
    searchQuestionBank({ page: 1, pageSize: 1 }).then((d) => setTotalQuestionCount(d.totalCount)).catch(() => {});
  }, []);

  // Topic dropdown depends on the chosen Subject (spec section 18).
  useEffect(() => {
    if (!subjectId) {
      setTopics([]);
      return;
    }
    getQuestionBankTopics(subjectId).then(setTopics).catch(() => setTopics([]));
  }, [subjectId]);

  const loadPage = useCallback(
    (pageNum, { append }) => {
      const controller = new AbortController();
      setStatus(append ? "loading-more" : "loading");

      searchQuestionBank({ search, subjectId, topicId, examId, year, language, page: pageNum, pageSize: PAGE_SIZE }, { signal: controller.signal })
        .then((data) => {
          setItems((prev) => (append ? [...prev, ...data.items] : data.items));
          setTotalCount(data.totalCount);
          setHasMore(pageNum * data.pageSize < data.totalCount);
          pageRef.current = pageNum;
          setStatus("success");
        })
        .catch((err) => {
          if (err.name === "AbortError") return;
          setStatus("error");
        });

      return controller;
    },
    [search, subjectId, topicId, examId, year, language]
  );

  // Filters/search changed -- start over from page 1.
  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    let controller;
    debounceRef.current = setTimeout(() => {
      controller = loadPage(1, { append: false });
      if (search.trim()) {
        pushRecentSearch(search);
        setRecentSearches(readRecentSearches());
      }
    }, 250);

    return () => {
      clearTimeout(debounceRef.current);
      controller?.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadPage]);

  // Infinite scroll -- observes a sentinel div just past the last card; fetches the next page once
  // it enters the viewport (rootMargin gives it a head start).
  useEffect(() => {
    if (!sentinelRef.current) return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0].isIntersecting && hasMore && status === "success") {
          loadPage(pageRef.current + 1, { append: true });
        }
      },
      { rootMargin: "400px" }
    );
    observer.observe(sentinelRef.current);
    return () => observer.disconnect();
  }, [hasMore, status, loadPage]);

  function handleQuestionChange(questionId, patch) {
    setItems((prev) => prev.map((q) => (q.id === questionId ? { ...q, ...patch } : q)));
  }

  function updateParam(key, value, resetDependents = []) {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      if (value) next.set(key, value);
      else next.delete(key);
      resetDependents.forEach((k) => next.delete(k));
      return next;
    });
  }

  function clearFilters() {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      ["subjectId", "topicId", "examId", "year", "language"].forEach((k) => next.delete(k));
      return next;
    });
  }

  function runRecentSearch(term) {
    updateParam("q", term);
    searchInputRef.current?.focus();
  }

  // "Surprise Me" -- a genuinely random question, picked by asking the real search endpoint for a
  // random offset within the current total count (not a fabricated "trending" pick).
  async function handleSurpriseMe() {
    if (!totalQuestionCount) return;
    setShuffling(true);
    try {
      const randomIndex = Math.floor(Math.random() * totalQuestionCount);
      const data = await searchQuestionBank({ page: randomIndex + 1, pageSize: 1 });
      if (data.items[0]) setShuffled(data.items[0]);
    } catch {
      // stays on whatever was showing -- the button just does nothing this click
    } finally {
      setShuffling(false);
    }
  }

  const activeFilterChips = useMemo(() => {
    const chips = [];
    if (subjectId) {
      const s = subjects.find((x) => x.id === subjectId);
      if (s) chips.push({ key: "subjectId", label: s.name, clears: ["subjectId", "topicId"] });
    }
    if (topicId) {
      const t = topics.find((x) => x.id === topicId);
      if (t) chips.push({ key: "topicId", label: t.name, clears: ["topicId"] });
    }
    if (examId) {
      const e = exams.find((x) => x.id === examId);
      if (e) chips.push({ key: "examId", label: e.name, clears: ["examId"] });
    }
    if (year) chips.push({ key: "year", label: year, clears: ["year"] });
    if (language) chips.push({ key: "language", label: language, clears: ["language"] });
    return chips;
  }, [subjectId, topicId, examId, year, language, subjects, topics, exams]);

  const hasActiveFilters = activeFilterChips.length > 0;

  return (
    <div className="mx-auto w-full max-w-6xl px-4 pb-8 pt-4 sm:px-6 lg:flex lg:items-start lg:gap-6 lg:pt-6">
      <div className="min-w-0 lg:max-w-2xl lg:flex-1">
      {/* ---------- Header ---------- */}
      <h1 className="text-2xl font-extrabold text-ink-900 sm:text-3xl">Question Bank</h1>
      <p className="mt-1 text-sm text-ink-400">
        Search, practice, and understand questions from multiple competitive exams.
      </p>

      <label className="relative mt-5 block">
        <span className="sr-only">Search Question Bank</span>
        <Search className="pointer-events-none absolute left-4 top-1/2 h-[18px] w-[18px] -translate-y-1/2 text-ink-400" strokeWidth={2} />
        <input
          ref={searchInputRef}
          type="search"
          value={search}
          onChange={(e) => updateParam("q", e.target.value)}
          placeholder="Search by keyword or paste the complete question..."
          className="w-full rounded-xl2 border border-primary-100 bg-white py-3.5 pl-11 pr-4 text-sm text-ink-900 placeholder:text-ink-400 shadow-card transition-shadow focus:border-primary-300 focus:shadow-cardHover focus:outline-none"
        />
      </label>

      {/* Recent searches: shown here on mobile/tablet only -- desktop gets the same list inside
          the Quick Actions panel in the sidebar, so it isn't shown twice on wide screens. */}
      {!search && recentSearches.length > 0 && (
        <div className="mt-2.5 flex flex-wrap items-center gap-1.5 lg:hidden">
          <History className="h-3.5 w-3.5 text-ink-300" strokeWidth={2} />
          {recentSearches.map((term) => (
            <button
              key={term}
              type="button"
              onClick={() => runRecentSearch(term)}
              className="rounded-full bg-primary-50 px-2.5 py-1 text-[11px] font-medium text-primary-600 hover:bg-primary-100"
            >
              {term}
            </button>
          ))}
        </div>
      )}

      {/* ---------- Stat bar -- every number here is real, computed from actual API data ---------- */}
      <div className="mt-5 grid grid-cols-2 gap-2.5 sm:grid-cols-4">
        <StatCard icon={BookOpen} tint="primary" value={totalQuestionCount} label="Questions" />
        <StatCard icon={GraduationCap} tint="accent" value={exams.length} label="Exams" />
        <StatCard icon={Layers} tint="teal" value={subjects.length} label="Subjects" />
        <StatCard icon={CalendarDays} tint="mint" value={years.length} label="Years" />
      </div>

      {/* ---------- Filters ---------- */}
      <div className="mt-6 flex items-center justify-between">
        <h2 className="text-sm font-bold text-ink-900">Filters</h2>
        <div className="flex items-center gap-3">
          {hasActiveFilters && (
            <button type="button" onClick={clearFilters} className="text-xs font-semibold text-secondary-500 hover:text-secondary-600">
              Clear Filters
            </button>
          )}
          <button
            type="button"
            onClick={() => setView((v) => (v === "list" ? "compact" : "list"))}
            className="flex items-center gap-1.5 rounded-lg bg-primary-50 px-2.5 py-1.5 text-[11px] font-semibold text-primary-600 hover:bg-primary-100"
            title={view === "list" ? "Switch to compact view" : "Switch to list view"}
          >
            {view === "list" ? <Rows3 className="h-3.5 w-3.5" strokeWidth={2.25} /> : <LayoutList className="h-3.5 w-3.5" strokeWidth={2.25} />}
            {view === "list" ? "Compact" : "List"}
          </button>
        </div>
      </div>

      <div className="mt-3 grid grid-cols-2 gap-2.5 sm:grid-cols-5">
        <Dropdown label="Subject" value={subjectId} onChange={(v) => updateParam("subjectId", v, ["topicId"])} placeholder="Any subject">
          {subjects.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
        </Dropdown>
        <Dropdown label="Topic" value={topicId} onChange={(v) => updateParam("topicId", v)} placeholder="Any topic" disabled={!subjectId}>
          {topics.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
        </Dropdown>
        <Dropdown label="Exam" value={examId} onChange={(v) => updateParam("examId", v)} placeholder="Any exam">
          {exams.map((e) => <option key={e.id} value={e.id}>{e.name}</option>)}
        </Dropdown>
        <Dropdown label="Year" value={year} onChange={(v) => updateParam("year", v)} placeholder="Any year">
          {years.map((y) => <option key={y} value={y}>{y}</option>)}
        </Dropdown>
        <Dropdown label="Language" value={language} onChange={(v) => updateParam("language", v)} placeholder="Any language">
          <option value="Hindi">Hindi</option>
          <option value="English">English</option>
        </Dropdown>
      </div>

      {activeFilterChips.length > 0 && (
        <div className="mt-3 flex flex-wrap gap-1.5">
          {activeFilterChips.map((chip) => (
            <button
              key={chip.key}
              type="button"
              onClick={() => updateParam(chip.key, "", chip.clears)}
              className="flex items-center gap-1 rounded-full bg-secondary-50 py-1 pl-3 pr-1.5 text-xs font-semibold text-secondary-600 hover:bg-secondary-100"
            >
              {chip.label}
              <X className="h-3 w-3" strokeWidth={2.5} />
            </button>
          ))}
        </div>
      )}

      {/* ---------- Surprise Me -- real, random within the current total. Mobile/tablet only here;
          desktop triggers the same handler from the sidebar's Quick Actions panel. ---------- */}
      {totalQuestionCount > 0 && (
        <button
          type="button"
          onClick={handleSurpriseMe}
          disabled={shuffling}
          className="mt-4 flex items-center gap-1.5 rounded-xl2 border border-dashed border-primary-200 px-3.5 py-2 text-xs font-semibold text-primary-600 hover:bg-primary-50 disabled:opacity-60 lg:hidden"
        >
          {shuffling ? <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.25} /> : <Shuffle className="h-3.5 w-3.5" strokeWidth={2.25} />}
          Surprise me with a random question
        </button>
      )}

      {shuffled && (
        <div className="mt-3">
          <QuestionBankFeedCard question={shuffled} onQuestionChange={(id, patch) => setShuffled((prev) => ({ ...prev, ...patch }))} compact={view === "compact"} />
        </div>
      )}

      {/* ---------- Results ---------- */}
      <div className="mt-6">
        {status === "loading" && (
          <div className="flex flex-col gap-3">
            <QuestionBankFeedCardSkeleton />
            <QuestionBankFeedCardSkeleton />
            <QuestionBankFeedCardSkeleton />
          </div>
        )}

        {status === "error" && (
          <div className="flex flex-col items-center gap-2 rounded-xl2 border border-red-100 bg-red-50 py-10 text-center text-red-600">
            <ServerCrash className="h-7 w-7" strokeWidth={2} />
            <p className="max-w-sm px-6 text-sm font-medium">Something went wrong while loading questions.</p>
            <button type="button" onClick={() => loadPage(1, { append: false })} className="mt-1 rounded-lg bg-red-600 px-3.5 py-1.5 text-xs font-semibold text-white hover:bg-red-700">
              Try Again
            </button>
          </div>
        )}

        {status !== "loading" && status !== "error" && items.length === 0 && (
          <div className="flex flex-col items-center gap-2 py-16 text-ink-400">
            <Inbox className="h-7 w-7" strokeWidth={1.75} />
            <p className="text-sm">
              {search || hasActiveFilters ? "No questions found." : "Start typing to find a question."}
            </p>
            {(search || hasActiveFilters) && (
              <>
                <p className="max-w-xs text-center text-xs">Try changing your filters or searching with another keyword.</p>
                <button type="button" onClick={clearFilters} className="mt-1 rounded-lg bg-primary-50 px-3.5 py-1.5 text-xs font-semibold text-primary-600 hover:bg-primary-100">
                  Clear Filters
                </button>
              </>
            )}
          </div>
        )}

        {items.length > 0 && (
          <>
            <p className="text-xs font-medium text-ink-400">
              {totalCount} question{totalCount === 1 ? "" : "s"} found
            </p>
            <div className="mt-2 flex flex-col gap-3">
              {items.map((q) => (
                <QuestionBankFeedCard key={q.id} question={q} onQuestionChange={handleQuestionChange} compact={view === "compact"} />
              ))}
            </div>

            <div ref={sentinelRef} className="flex justify-center py-6">
              {status === "loading-more" && <Loader2 className="h-5 w-5 animate-spin text-ink-400" strokeWidth={2.25} />}
              {!hasMore && <p className="text-xs text-ink-400">You've reached the end.</p>}
            </div>
          </>
        )}
      </div>
      </div>

      <QuestionBankSidebar
        subjects={subjects}
        onPickSubject={(id) => updateParam("subjectId", id, ["topicId"])}
        recentSearches={recentSearches}
        onRunRecentSearch={runRecentSearch}
        onSurpriseMe={handleSurpriseMe}
        surpriseLoading={shuffling}
        surpriseDisabled={shuffling || !totalQuestionCount}
      />
    </div>
  );
}

function StatCard({ icon: Icon, tint, value, label }) {
  const tints = {
    primary: "bg-primary-50 text-primary-600",
    accent: "bg-accent-50 text-accent-500",
    teal: "bg-teal-50 text-teal-500",
    mint: "bg-mint-50 text-mint-500",
  };
  return (
    <div className="flex items-center gap-2.5 rounded-xl2 border border-primary-100 bg-white p-3 shadow-card">
      <span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-xl ${tints[tint]}`}>
        <Icon className="h-4 w-4" strokeWidth={2.25} />
      </span>
      <div className="min-w-0">
        <p className="text-base font-extrabold leading-none text-ink-900">{value == null ? "—" : value.toLocaleString()}</p>
        <p className="mt-0.5 truncate text-[11px] text-ink-400">{label}</p>
      </div>
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
