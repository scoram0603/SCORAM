import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Search, Loader2, ServerCrash, Inbox, X, BookOpen, GraduationCap, Layers,
  CalendarDays, Shuffle, History, LayoutList, Rows3, ChevronLeft, ChevronRight, RotateCcw,
} from "lucide-react";
import { useSearchParams } from "react-router-dom";
import { searchQuestionBank, getQuestionBankSubjects, getQuestionBankTopics, getQuestionBankExams, getQuestionBankYears } from "../api/questionBank";
import QuestionBankFeedCard from "../components/questions/QuestionBankFeedCard";
import QuestionBankFeedCardSkeleton from "../components/questions/QuestionBankFeedCardSkeleton";
import QuestionBankSidebar from "../components/questions/QuestionBankSidebar";
import SearchableSelect from "../components/ui/SearchableSelect";
import MyExamsFilter from "../components/exams/MyExamsFilter";
import ChooseMyExamsPrompt from "../components/exams/ChooseMyExamsPrompt";
import { useMyExamsScope } from "../hooks/useMyExamsScope";
import { useQuestionBrowseProgress } from "../hooks/useQuestionBrowseProgress";
import {
  filtersFromParams, hasAnyFilters, hasBrowseParams, pagesNeeded, paramsFromFilters, qKey, resolveResumeIndex,
} from "../utils/browseProgress";

const PAGE_SIZE = 10;
const RECENT_SEARCHES_KEY = "scoram:qb:recent-searches";
const LANGUAGE_OPTIONS = [
  { value: "Hindi", label: "Hindi" },
  { value: "English", label: "English" },
];

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

// A multi-select filter's values live in the URL as one comma-joined param (?subjectIds=id1,id2)
// rather than repeated keys -- keeps the address bar tidy and is trivial to read back out.
function readListParam(searchParams, key) {
  const raw = searchParams.get(key);
  return raw ? raw.split(",").filter(Boolean) : [];
}

// FEED REDESIGN + PREMIUM UI PASS -- one question per row, everything (practice options, Discuss,
// Like/Dislike, Share) inline on the card (see QuestionBankFeedCard), infinite scroll (or Slide
// mode -- see below), and a premium header with real stat counts + quick actions. Filter/search
// state lives in the URL (searchParams) so a result is shareable/bookmarkable. Every number and
// action here is backed by a real API or a real client-side mechanism (localStorage recent
// searches, a genuinely random question) -- no fabricated "trending topics" or usage analytics the
// backend doesn't track.
//
// Filters (Subject/Topic/Exam/Year/Language) are multi-select + searchable (SearchableSelect) --
// a student can pick just one value (works exactly like the old single-select dropdown) or several
// (e.g. SSC CGL + RRB NTPC together) in the same filter; the backend OR-matches within a filter and
// AND-matches across filters (see QuestionBankController.Search).
//
// Two ways to browse results: Scroll (the original infinite-scroll feed, with its own List/Compact
// density toggle) or Slide (one question full-screen-width at a time, Prev/Next arrow buttons --
// same underlying `items`/pagination, just a different way of moving through them).
export default function QuestionBankSearch() {
  const [searchParams, setSearchParams] = useSearchParams();

  const search = searchParams.get("q") || "";
  const subjectIds = useMemo(() => readListParam(searchParams, "subjectIds"), [searchParams]);
  const topicIds = useMemo(() => readListParam(searchParams, "topicIds"), [searchParams]);
  const examIds = useMemo(() => readListParam(searchParams, "examIds"), [searchParams]);
  const years = useMemo(() => readListParam(searchParams, "years"), [searchParams]);
  const languages = useMemo(() => readListParam(searchParams, "languages"), [searchParams]);
  const browseMode = searchParams.get("mode") === "slide" ? "slide" : "scroll";

  // "MY EXAMS" -- strict scope. The API only returns questions of the student's own exams (and
  // only their exams in the Exam/Year filter data), so the Exam filter below is just an optional
  // NARROWING within My Exams. Wait for the scope before fetching; show the prompt when it's empty.
  const scope = useMyExamsScope();

  // PYQ PROGRESS -- picked options live HERE (by question id), not inside each card: Slide mode remounts a card
  // for every question, which is why a pick used to vanish after Previous/Next. The same hook saves them (plus
  // filters, last question and view mode) to the student's account so the session resumes after leaving/closing.
  // Signed-in students wait ("waiting") until the saved session is known so the very first fetch already uses the
  // restored filters instead of loading everything and then reloading.
  const progress = useQuestionBrowseProgress();
  const [restoreState, setRestoreState] = useState(() => (progress.enabled ? "waiting" : "idle")); // waiting | restoring | idle | done
  const restoreTargetRef = useRef(null);
  const [resumeNote, setResumeNote] = useState(null); // { answered, position } -- shown after a restore
  const [freshBusy, setFreshBusy] = useState(false);
  const [freshError, setFreshError] = useState("");
  const canFetch = scope.ready && !scope.isEmpty && restoreState !== "waiting";

  // Decide whether to restore the saved session (only when the URL doesn't already carry a search/filter --
  // a shared link or an explicit choice always wins).
  useEffect(() => {
    if (restoreState !== "waiting" || !progress.ready) return;
    const snap = progress.snapshot;
    const hasContent = snap && (snap.lastQuestionId || snap.lastPosition > 0 || snap.answeredCount > 0 || hasAnyFilters(snap.filters));
    if (!hasContent || hasBrowseParams(searchParams)) {
      setRestoreState("idle");
      return;
    }
    restoreTargetRef.current = { questionId: snap.lastQuestionId, position: snap.lastPosition };
    setResumeNote({ answered: snap.answeredCount, position: snap.lastPosition });
    setSearchParams(paramsFromFilters(snap.filters, snap.viewMode), { replace: true });
    setRestoreState("restoring");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [restoreState, progress.ready, progress.snapshot]);

  // Drop any ?examIds= value that isn't one of My Exams (e.g. a stale/shared link).
  useEffect(() => {
    if (!scope.isScoped || !scope.ready || scope.isEmpty || examIds.length === 0) return;
    const allowed = examIds.filter((id) => scope.examIds.includes(id));
    if (allowed.length !== examIds.length) updateListParam("examIds", allowed);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scope.isScoped, scope.ready, scope.isEmpty, scope.examIds, examIds.join(",")]);

  const [subjects, setSubjects] = useState([]);
  const [subjectsExamId, setSubjectsExamId] = useState(null); // which exam `subjects` was loaded for
  const [topics, setTopics] = useState([]);
  const [exams, setExams] = useState([]);
  const [allYears, setAllYears] = useState([]);
  const [totalQuestionCount, setTotalQuestionCount] = useState(null);

  const [items, setItems] = useState([]);
  const [totalCount, setTotalCount] = useState(0);
  const [status, setStatus] = useState("loading"); // loading | loading-more | success | error
  const [hasMore, setHasMore] = useState(true);
  const [view, setView] = useState("list"); // list | compact -- density, Scroll mode only
  const [recentSearches, setRecentSearches] = useState(readRecentSearches);
  const [shuffling, setShuffling] = useState(false);
  const [shuffled, setShuffled] = useState(null);
  const [slideIndex, setSlideIndex] = useState(0);

  const pageRef = useRef(1);
  const debounceRef = useRef(null);
  const sentinelRef = useRef(null);
  const searchInputRef = useRef(null);
  const pendingSlideAdvanceRef = useRef(false);

  useEffect(() => {
    if (!canFetch) return;
    getQuestionBankExams().then(setExams).catch(() => {});
    getQuestionBankYears().then(setAllYears).catch(() => {});
    // Real total, unfiltered -- powers the "X Questions" stat card regardless of active filters.
    searchQuestionBank({ page: 1, pageSize: 1 }).then((d) => setTotalQuestionCount(d.totalCount)).catch(() => {});
  }, [canFetch]);

  // EXAM-WISE FILTERS -- when the results are for exactly one exam (picked in the Exam filter, or the student's
  // single My Exam) the Subject/Topic lists are that exam's own, so a subject/topic renamed for one exam shows
  // under its new name only there. Everything else (several exams, no exam, an exam without its own list) is the
  // global list, unchanged. The selected ids stay in the same ?subjectIds=/?topicIds= URL params either way; they
  // are just sent to the API as examSubjectIds/examTopicIds when the list is exam-wise.
  const activeExamId = examIds.length === 1 ? examIds[0] : (scope.isScoped && scope.examIds?.length === 1 ? scope.examIds[0] : null);
  useEffect(() => {
    if (!canFetch) return undefined;
    let cancelled = false;
    getQuestionBankSubjects(activeExamId)
      .then((list) => { if (!cancelled) { setSubjects(list); setSubjectsExamId(activeExamId); } })
      .catch(() => {});
    return () => { cancelled = true; };
  }, [canFetch, activeExamId]);

  const examWise = subjects.length > 0 && subjects.every((s) => s.isExamSpecific === true);
  const subjectFilter = examWise ? { examSubjectIds: subjectIds } : { subjectIds };
  const topicFilter = examWise ? { examTopicIds: topicIds } : { topicIds };

  // Switching between the global list and an exam's own list (or between two exams) makes the previously picked
  // subject/topic ids meaningless, so clear them -- but never on the first load, so shared links keep working.
  const subjectModeRef = useRef(null);
  useEffect(() => {
    if (subjects.length === 0) return;
    const mode = examWise ? `exam:${subjectsExamId}` : "global";
    const prev = subjectModeRef.current;
    subjectModeRef.current = mode;
    if (prev === null || prev === mode) return;
    if (subjectIds.length > 0 || topicIds.length > 0) updateListParam("subjectIds", [], ["topicIds"]);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [subjects]);

  // Topic dropdown depends on the chosen Subject(s) (spec section 18) -- with several subjects
  // selected, the Topic list is the union of each subject's topics.
  useEffect(() => {
    if (subjectIds.length === 0) {
      setTopics([]);
      return;
    }
    Promise.all(subjectIds.map((id) => getQuestionBankTopics(id, examWise ? subjectsExamId : undefined).catch(() => [])))
      .then((lists) => {
        const merged = new Map();
        lists.flat().forEach((t) => merged.set(t.id, t));
        setTopics(Array.from(merged.values()));
      })
      .catch(() => setTopics([]));
  }, [subjectIds.join(","), examWise, subjectsExamId]); // eslint-disable-line react-hooks/exhaustive-deps

  const loadPage = useCallback(
    (pageNum, { append }) => {
      const controller = new AbortController();
      setStatus(append ? "loading-more" : "loading");

      searchQuestionBank(
        { search, ...subjectFilter, ...topicFilter, examIds, years, languages, page: pageNum, pageSize: PAGE_SIZE },
        { signal: controller.signal }
      )
        .then((data) => {
          setItems((prev) => (append ? [...prev, ...data.items] : data.items));
          setTotalCount(data.totalCount);
          setHasMore(pageNum * data.pageSize < data.totalCount);
          pageRef.current = pageNum;
          setStatus("success");
          if (pendingSlideAdvanceRef.current) {
            pendingSlideAdvanceRef.current = false;
            setSlideIndex((i) => i + 1);
          }
        })
        .catch((err) => {
          if (err.name === "AbortError") return;
          setStatus("error");
        });

      return controller;
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [search, subjectIds.join(","), topicIds.join(","), examWise, examIds.join(","), years.join(","), languages.join(",")]
  );

  // Filters/search changed -- start over from page 1.
  useEffect(() => {
    if (!canFetch) return undefined;
    if (debounceRef.current) clearTimeout(debounceRef.current);
    let controller;
    debounceRef.current = setTimeout(() => {
      controller = loadPage(1, { append: false });
      setSlideIndex(0);
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
  }, [loadPage, canFetch]);

  // Back to where the student was: load enough pages to contain the saved position (in parallel, capped), then
  // jump there -- Slide mode shows that question, Scroll mode scrolls it into view.
  useEffect(() => {
    if (restoreState !== "restoring" || status !== "success") return undefined;
    let cancelled = false;
    (async () => {
      const target = restoreTargetRef.current;
      let all = items;
      const needed = pagesNeeded(target?.position ?? 0, PAGE_SIZE);
      if (needed > 1 && hasMore) {
        try {
          const pages = await Promise.all(
            Array.from({ length: needed - 1 }, (_, i) =>
              searchQuestionBank({ search, ...subjectFilter, ...topicFilter, examIds, years, languages, page: i + 2, pageSize: PAGE_SIZE })),
          );
          if (cancelled) return;
          all = [...items, ...pages.flatMap((p) => p.items)];
          setItems(all);
          pageRef.current = needed;
          setHasMore(needed * PAGE_SIZE < pages[pages.length - 1].totalCount);
        } catch {
          // couldn't reach further pages -- resume as far as we got
        }
      }
      if (cancelled) return;
      const idx = resolveResumeIndex(all, target?.questionId, target?.position);
      setResumeNote((note) => (note ? { ...note, position: idx } : note));
      if (browseMode === "slide") setSlideIndex(idx);
      setRestoreState("done");
      if (browseMode !== "slide" && all[idx]) {
        setTimeout(() => document.getElementById(`qb-q-${all[idx].id}`)?.scrollIntoView({ block: "start" }), 80);
      }
    })();
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [restoreState, status]);

  // Save the browsing position (filters, last question, view mode). Not while restoring -- that would overwrite
  // the saved position with "question 1" before we have jumped back to it.
  const filtersObj = useMemo(() => filtersFromParams(searchParams), [searchParams]);
  const filtersKey = JSON.stringify(filtersObj);
  const reportingOn = progress.enabled && (restoreState === "idle" || restoreState === "done") && status === "success";
  const itemsRef = useRef(items);
  const filtersRef = useRef(filtersObj);
  useEffect(() => {
    itemsRef.current = items;
    filtersRef.current = filtersObj;
  });
  const { reportPosition } = progress;
  const baseKeyRef = useRef(null);

  // Slide mode: the current slide is the position.
  useEffect(() => {
    if (!reportingOn || browseMode !== "slide") return;
    const q = items[slideIndex];
    if (!q) return;
    reportPosition({ filters: filtersObj, lastQuestionId: q.id, lastPosition: slideIndex, viewMode: "slide" });
  }, [reportingOn, browseMode, slideIndex, items, filtersObj, reportPosition]);

  // Filters or view mode changed by the student: the old position no longer applies (Scroll mode starts at the top).
  useEffect(() => {
    if (!reportingOn) return;
    const key = `${filtersKey}|${browseMode}`;
    if (baseKeyRef.current === null) { baseKeyRef.current = key; return; }
    if (baseKeyRef.current === key) return;
    baseKeyRef.current = key;
    if (browseMode === "scroll") {
      reportPosition({ filters: filtersRef.current, lastQuestionId: null, lastPosition: 0, viewMode: "scroll" });
    }
  }, [reportingOn, filtersKey, browseMode, reportPosition]);

  // Scroll mode: the first card still (partly) below the sticky header is the position.
  useEffect(() => {
    if (!reportingOn || browseMode !== "scroll") return undefined;
    let timer;
    const onScroll = () => {
      clearTimeout(timer);
      timer = setTimeout(() => {
        const nodes = document.querySelectorAll("[data-qb-index]");
        if (!nodes.length) return;
        let idx = Number(nodes[nodes.length - 1].dataset.qbIndex);
        for (const node of nodes) {
          if (node.getBoundingClientRect().bottom > 120) { idx = Number(node.dataset.qbIndex); break; }
        }
        const q = itemsRef.current[idx];
        if (q) reportPosition({ filters: filtersRef.current, lastQuestionId: q.id, lastPosition: idx, viewMode: "scroll" });
      }, 300);
    };
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => { window.removeEventListener("scroll", onScroll); clearTimeout(timer); };
  }, [reportingOn, browseMode, reportPosition]);

  async function handleStartFresh() {
    if (freshBusy) return;
    if (!window.confirm("Start a fresh PYQ session? Your picked options and position are cleared from this screen (they stay in your archived history).")) return;
    setFreshBusy(true);
    setFreshError("");
    try {
      await progress.startFresh();
      setResumeNote(null);
      baseKeyRef.current = null;
      setSearchParams({}, { replace: false });
      setSlideIndex(0);
      window.scrollTo(0, 0);
    } catch (err) {
      setFreshError(err?.message || "Couldn't start a fresh session. Please try again.");
    } finally {
      setFreshBusy(false);
    }
  }

  // Infinite scroll -- observes a sentinel div just past the last card; fetches the next page once
  // it enters the viewport (rootMargin gives it a head start). Only active in Scroll mode -- Slide
  // mode fetches its next page on demand from handleSlideNext instead.
  useEffect(() => {
    if (browseMode !== "scroll" || !sentinelRef.current) return;
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
  }, [hasMore, status, loadPage, browseMode]);

  function handleQuestionChange(questionId, patch) {
    setItems((prev) => prev.map((q) => (q.id === questionId ? { ...q, ...patch } : q)));
  }

  function setMode(nextMode) {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      if (nextMode === "scroll") next.delete("mode");
      else next.set("mode", nextMode);
      return next;
    });
    setSlideIndex(0);
  }

  function handleSlidePrev() {
    setSlideIndex((i) => Math.max(0, i - 1));
  }

  function handleSlideNext() {
    if (slideIndex + 1 < items.length) {
      setSlideIndex((i) => i + 1);
    } else if (hasMore && status !== "loading-more") {
      pendingSlideAdvanceRef.current = true;
      loadPage(pageRef.current + 1, { append: true });
    }
  }

  // Swipe-to-navigate for Slide mode on touch devices -- the Prev/Next buttons stay as a fallback
  // (desktop, accessibility), but a finger swipe shouldn't require hitting a tiny arrow. Only a
  // mostly-horizontal drag past the threshold counts, so a normal tap on an option/like/discuss
  // button (or a vertical scroll) is never mistaken for a swipe.
  const touchStartRef = useRef(null);
  const SWIPE_THRESHOLD_PX = 60;

  function handleCardTouchStart(e) {
    const t = e.touches[0];
    touchStartRef.current = { x: t.clientX, y: t.clientY };
  }

  function handleCardTouchEnd(e) {
    const start = touchStartRef.current;
    touchStartRef.current = null;
    if (!start) return;
    const t = e.changedTouches[0];
    const dx = t.clientX - start.x;
    const dy = t.clientY - start.y;
    if (Math.abs(dx) < SWIPE_THRESHOLD_PX || Math.abs(dx) < Math.abs(dy) * 1.5) return;
    if (dx < 0) handleSlideNext();
    else handleSlidePrev();
  }

  // Left/Right arrow keys navigate in Slide mode -- matches the on-screen Prev/Next buttons.
  useEffect(() => {
    if (browseMode !== "slide") return;
    function handleKey(e) {
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) return;
      if (e.key === "ArrowRight") handleSlideNext();
      if (e.key === "ArrowLeft") handleSlidePrev();
    }
    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [browseMode, slideIndex, items.length, hasMore, status]);

  function updateParam(key, value, resetDependents = []) {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      if (value) next.set(key, value);
      else next.delete(key);
      resetDependents.forEach((k) => next.delete(k));
      return next;
    });
  }

  // Multi-select filters store their whole array back into one comma-joined param.
  function updateListParam(key, values, resetDependents = []) {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      if (values.length > 0) next.set(key, values.join(","));
      else next.delete(key);
      resetDependents.forEach((k) => next.delete(k));
      return next;
    });
  }

  function clearFilters() {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      ["subjectIds", "topicIds", "examIds", "years", "languages"].forEach((k) => next.delete(k));
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
    subjectIds.forEach((id) => {
      const s = subjects.find((x) => String(x.id) === id);
      if (s) chips.push({ key: `subjectIds:${id}`, label: s.name, onRemove: () => updateListParam("subjectIds", subjectIds.filter((v) => v !== id), id === subjectIds[subjectIds.length - 1] ? [] : []) });
    });
    topicIds.forEach((id) => {
      const t = topics.find((x) => String(x.id) === id);
      if (t) chips.push({ key: `topicIds:${id}`, label: t.name, onRemove: () => updateListParam("topicIds", topicIds.filter((v) => v !== id)) });
    });
    examIds.forEach((id) => {
      const e = exams.find((x) => String(x.id) === id);
      if (e) chips.push({ key: `examIds:${id}`, label: e.name, onRemove: () => updateListParam("examIds", examIds.filter((v) => v !== id)) });
    });
    years.forEach((y) => chips.push({ key: `years:${y}`, label: y, onRemove: () => updateListParam("years", years.filter((v) => v !== y)) }));
    languages.forEach((l) => chips.push({ key: `languages:${l}`, label: l, onRemove: () => updateListParam("languages", languages.filter((v) => v !== l)) }));
    return chips;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [subjectIds, topicIds, examIds, years, languages, subjects, topics, exams]);

  const hasActiveFilters = activeFilterChips.length > 0;
  const currentSlideItem = items[slideIndex];

  // MY EXAMS empty (skipped / cleared): no questions at all, just the prompt -- never every exam's.
  if (scope.isEmpty) {
    return (
      <div className="mx-auto w-full max-w-6xl px-4 pb-8 pt-4 sm:px-6 lg:pt-6">
        <h1 className="text-2xl font-extrabold text-ink-900 sm:text-3xl">PYQs</h1>
        <div className="mt-5">
          <ChooseMyExamsPrompt title="No My Exams selected yet." message="Choose your exams to see relevant PYQs and questions." />
        </div>
      </div>
    );
  }

  return (
    <div className="mx-auto w-full max-w-6xl px-4 pb-8 pt-4 sm:px-6 lg:flex lg:items-start lg:gap-6 lg:pt-6">
      <div className="min-w-0 lg:max-w-3xl lg:flex-1">
      {/* ---------- Header ---------- */}
      <h1 className="text-2xl font-extrabold text-ink-900 sm:text-3xl">PYQs</h1>
      <p className="mt-1 text-sm text-ink-400">
        Search, practice, and understand questions from multiple competitive exams.
      </p>

      {(resumeNote || freshError || progress.syncState === "retrying") && (
        <div role="status" className="mt-4 flex flex-col gap-1.5">
          {resumeNote && (
            <div className="flex items-center gap-2 rounded-xl2 border border-primary-100 bg-primary-50/60 px-3.5 py-2.5 text-xs text-ink-600">
              <History className="h-3.5 w-3.5 shrink-0 text-primary-600" strokeWidth={2.25} />
              <span className="min-w-0 flex-1">
                Resumed from where you left off
                {resumeNote.answered > 0 ? ` · ${resumeNote.answered} answered` : ""}
                {resumeNote.position > 0 ? ` · question ${resumeNote.position + 1}` : ""}
              </span>
              <button type="button" onClick={handleStartFresh} disabled={freshBusy} className="shrink-0 font-bold text-secondary-500 hover:text-secondary-600 disabled:opacity-50">
                Start fresh
              </button>
              <button type="button" onClick={() => setResumeNote(null)} aria-label="Dismiss" className="shrink-0 text-ink-400 hover:text-ink-600">
                <X className="h-3.5 w-3.5" strokeWidth={2.5} />
              </button>
            </div>
          )}
          {progress.syncState === "retrying" && (
            <p className="rounded-xl2 border border-amber-100 bg-amber-50 px-3.5 py-2 text-xs text-amber-700">
              Couldn&apos;t reach the server. Your answers are kept on this device and will sync automatically.
            </p>
          )}
          {freshError && <p className="rounded-xl2 border border-red-100 bg-red-50 px-3.5 py-2 text-xs text-red-600">{freshError}</p>}
        </div>
      )}

      <label className="relative mt-5 block">
        <span className="sr-only">Search PYQs</span>
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
      <div className="mt-5 grid grid-cols-2 gap-2 sm:grid-cols-4">
        <StatCard icon={BookOpen} tint="primary" value={totalQuestionCount} label="Questions" />
        <StatCard icon={GraduationCap} tint="accent" value={exams.length} label="Exams" />
        <StatCard icon={Layers} tint="teal" value={subjects.length} label="Subjects" />
        <StatCard icon={CalendarDays} tint="mint" value={allYears.length} label="Years" />
      </div>

      {/* ---------- Filters -- multi-select + searchable (pick one value, works like before; pick
          several, e.g. two exams at once, and results OR-match within that filter). ---------- */}
      <div className="mt-6 flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-sm font-bold text-ink-900">Filters</h2>
        <div className="flex items-center gap-2">
          {progress.enabled && !resumeNote && (progress.answeredCount > 0 || hasActiveFilters) && (
            <button
              type="button"
              onClick={handleStartFresh}
              disabled={freshBusy}
              className="flex items-center gap-1 text-xs font-semibold text-ink-400 hover:text-primary-600 disabled:opacity-50"
              title="Begin a new browsing session"
            >
              <RotateCcw className="h-3 w-3" strokeWidth={2.5} />
              Start fresh
            </button>
          )}
          {hasActiveFilters && (
            <button type="button" onClick={clearFilters} className="text-xs font-semibold text-secondary-500 hover:text-secondary-600">
              Clear Filters
            </button>
          )}
          {/* Browse mode: Scroll (infinite feed) vs Slide (one question, Prev/Next arrows). */}
          <div className="flex items-center rounded-lg bg-primary-50 p-0.5">
            <button
              type="button"
              onClick={() => setMode("scroll")}
              className={`flex items-center gap-1 rounded-md px-2 py-1 text-[11px] font-semibold transition-colors ${
                browseMode === "scroll" ? "bg-white text-primary-600 shadow-sm" : "text-ink-400"
              }`}
              title="Scroll through all results"
            >
              <Rows3 className="h-3.5 w-3.5" strokeWidth={2.25} />
              Scroll
            </button>
            <button
              type="button"
              onClick={() => setMode("slide")}
              className={`flex items-center gap-1 rounded-md px-2 py-1 text-[11px] font-semibold transition-colors ${
                browseMode === "slide" ? "bg-white text-primary-600 shadow-sm" : "text-ink-400"
              }`}
              title="One question at a time, with Next/Previous"
            >
              <ChevronLeft className="h-3 w-3 -mr-1" strokeWidth={2.5} />
              <ChevronRight className="h-3 w-3" strokeWidth={2.5} />
              Slide
            </button>
          </div>
          {browseMode === "scroll" && (
            <button
              type="button"
              onClick={() => setView((v) => (v === "list" ? "compact" : "list"))}
              className="flex items-center gap-1.5 rounded-lg bg-primary-50 px-2.5 py-1.5 text-[11px] font-semibold text-primary-600 hover:bg-primary-100"
              title={view === "list" ? "Switch to compact view" : "Switch to list view"}
            >
              {view === "list" ? <Rows3 className="h-3.5 w-3.5" strokeWidth={2.25} /> : <LayoutList className="h-3.5 w-3.5" strokeWidth={2.25} />}
              {view === "list" ? "Compact" : "List"}
            </button>
          )}
        </div>
      </div>

      <div className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-5">
        <SearchableSelect
          label="Subject"
          placeholder="Any subject"
          options={subjects.map((s) => ({ value: s.id, label: s.name }))}
          selected={subjectIds}
          onChange={(v) => updateListParam("subjectIds", v, ["topicIds"])}
        />
        <SearchableSelect
          label="Topic"
          placeholder="Any topic"
          options={topics.map((t) => ({ value: t.id, label: t.name }))}
          selected={topicIds}
          onChange={(v) => updateListParam("topicIds", v)}
          disabled={subjectIds.length === 0}
        />
        <MyExamsFilter
          label="Exam"
          selected={examIds}
          onChange={(v) => updateListParam("examIds", v)}
        />
        <SearchableSelect
          label="Year"
          placeholder="Any year"
          options={allYears.map((y) => ({ value: String(y), label: String(y) }))}
          selected={years}
          onChange={(v) => updateListParam("years", v)}
        />
        <SearchableSelect
          label="Medium"
          placeholder="Any language"
          options={LANGUAGE_OPTIONS}
          selected={languages}
          onChange={(v) => updateListParam("languages", v)}
        />
      </div>

      {activeFilterChips.length > 0 && (
        <div className="mt-3 flex flex-wrap gap-1.5">
          {activeFilterChips.map((chip) => (
            <button
              key={chip.key}
              type="button"
              onClick={chip.onRemove}
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
          <QuestionBankFeedCard
            question={shuffled}
            onQuestionChange={(id, patch) => setShuffled((prev) => ({ ...prev, ...patch }))}
            compact={view === "compact"}
            selectedOption={progress.answers[qKey(shuffled.id)] ?? null}
            onSelectOption={progress.selectOption}
          />
        </div>
      )}

      {/* ---------- Results ---------- */}
      <div className="mt-5">
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

        {items.length > 0 && browseMode === "scroll" && (
          <>
            <p className="text-xs font-medium text-ink-400">
              {totalCount} question{totalCount === 1 ? "" : "s"} found
            </p>
            <div className="mt-2 flex flex-col gap-3">
              {items.map((q, i) => (
                <div key={q.id} id={`qb-q-${q.id}`} data-qb-index={i}>
                  <QuestionBankFeedCard
                    question={q}
                    onQuestionChange={handleQuestionChange}
                    compact={view === "compact"}
                    selectedOption={progress.answers[qKey(q.id)] ?? null}
                    onSelectOption={progress.selectOption}
                  />
                </div>
              ))}
            </div>

            <div ref={sentinelRef} className="flex justify-center py-6">
              {status === "loading-more" && <Loader2 className="h-5 w-5 animate-spin text-ink-400" strokeWidth={2.25} />}
              {!hasMore && <p className="text-xs text-ink-400">You've reached the end.</p>}
            </div>
          </>
        )}

        {items.length > 0 && browseMode === "slide" && currentSlideItem && (
          <>
            <p className="text-xs font-medium text-ink-400">
              Question {slideIndex + 1} of {totalCount}
            </p>
            <div className="mt-2" onTouchStart={handleCardTouchStart} onTouchEnd={handleCardTouchEnd}>
              <QuestionBankFeedCard
                key={currentSlideItem.id}
                question={currentSlideItem}
                onQuestionChange={handleQuestionChange}
                selectedOption={progress.answers[qKey(currentSlideItem.id)] ?? null}
                onSelectOption={progress.selectOption}
              />
            </div>
            <div className="mt-3 flex items-center justify-center gap-1.5">
              <button
                type="button"
                onClick={handleSlidePrev}
                disabled={slideIndex === 0}
                className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary-50 text-primary-600 hover:bg-primary-100 disabled:cursor-not-allowed disabled:opacity-40"
                aria-label="Previous question"
              >
                <ChevronLeft className="h-4 w-4" strokeWidth={2.5} />
              </button>
              <button
                type="button"
                onClick={handleSlideNext}
                disabled={slideIndex + 1 >= totalCount || (slideIndex + 1 >= items.length && !hasMore)}
                className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary-50 text-primary-600 hover:bg-primary-100 disabled:cursor-not-allowed disabled:opacity-40"
                aria-label="Next question"
              >
                {status === "loading-more" ? (
                  <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.25} />
                ) : (
                  <ChevronRight className="h-4 w-4" strokeWidth={2.5} />
                )}
              </button>
            </div>
            <p className="mt-1 text-center text-[11px] text-ink-300 sm:hidden">Swipe left or right to change question</p>
          </>
        )}
      </div>
      </div>

      <QuestionBankSidebar
        subjects={subjects}
        onPickSubject={(id) => updateListParam("subjectIds", [id], ["topicIds"])}
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
    <div className="flex items-center gap-2 rounded-xl2 border border-primary-100 bg-white p-2.5 shadow-card">
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
