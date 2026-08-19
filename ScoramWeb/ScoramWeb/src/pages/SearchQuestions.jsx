import { useEffect, useRef, useState } from "react";
import {
  Search, Loader2, ServerCrash, Inbox, ChevronLeft, ChevronRight, ChevronDown, X, ArrowRight,
} from "lucide-react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { searchQuestions, instantSearch, getQuestionById } from "../api/questions";
import { listExams } from "../api/exams";
import { getPaperYears, getPaperLanguages } from "../api/papers";
import { ApiError, API_BASE_URL } from "../api/client";
import QuestionCard from "../components/questions/QuestionCard";

const PAGE_SIZE = 10;

function logoSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

export default function SearchQuestions() {
  const [searchParams, setSearchParams] = useSearchParams();
  const keyword = searchParams.get("q") || "";
  const examId = searchParams.get("examId") || "";
  const isSearching = keyword.trim().length > 0;

  function handleKeywordChange(value) {
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value) next.set("q", value);
        else next.delete("q");
        return next;
      },
      { replace: true }
    );
  }

  return (
    <div className="px-4 pb-8 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <h1 className="text-xl font-extrabold text-ink-900 sm:text-2xl">Find PYQs</h1>
      <p className="mt-1 text-sm text-ink-400">
        Search any question instantly, or filter by exam, year, subject, and difficulty.
      </p>

      <label className="relative mt-5 block">
        <span className="sr-only">Search questions</span>
        <Search className="pointer-events-none absolute left-3.5 top-1/2 h-[18px] w-[18px] -translate-y-1/2 text-ink-400" strokeWidth={2} />
        <input
          type="search"
          value={keyword}
          onChange={(e) => handleKeywordChange(e.target.value)}
          placeholder="Search question text... e.g. 'Harappan civilization'"
          className="h-12 w-full rounded-xl2 border border-primary-100 bg-white pl-10 pr-3 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500"
        />
      </label>

      <div className="mt-5">
        {isSearching ? <InstantSearchResults keyword={keyword} /> : <BrowseByExam presetExamId={examId} />}
      </div>
    </div>
  );
}

// ---------- Instant search (Meilisearch-backed) ----------
function InstantSearchResults({ keyword }) {
  const [results, setResults] = useState([]);
  const [status, setStatus] = useState("loading");
  const [errorMessage, setErrorMessage] = useState("");
  const [expandedId, setExpandedId] = useState(null);
  const debounceRef = useRef(null);

  const [filterExam, setFilterExam] = useState("");
  const [filterSubject, setFilterSubject] = useState("");
  const [filterYear, setFilterYear] = useState("");
  const [filterLanguage, setFilterLanguage] = useState("");

  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    setStatus("loading");
    setFilterExam("");
    setFilterSubject("");
    setFilterYear("");
    setFilterLanguage("");

    const controller = new AbortController();
    debounceRef.current = setTimeout(() => {
      instantSearch(keyword, { signal: controller.signal })
        .then((data) => {
          setResults(data);
          setStatus("success");
        })
        .catch((err) => {
          if (err.name === "AbortError") return;
          setErrorMessage(err instanceof ApiError ? err.message : "Search is temporarily unavailable.");
          setStatus("error");
        });
    }, 250);

    return () => {
      clearTimeout(debounceRef.current);
      controller.abort();
    };
  }, [keyword]);

  if (status === "loading") {
    return (
      <div className="flex flex-col items-center gap-2 py-16 text-ink-400">
        <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
        <p className="text-sm">Searching…</p>
      </div>
    );
  }

  if (status === "error") {
    return (
      <div className="flex flex-col items-center gap-2 rounded-xl2 border border-red-100 bg-red-50 py-10 text-center text-red-600">
        <ServerCrash className="h-7 w-7" strokeWidth={2} />
        <p className="max-w-sm px-6 text-sm font-medium">{errorMessage}</p>
      </div>
    );
  }

  if (results.length === 0) {
    return (
      <div className="flex flex-col items-center gap-2 py-16 text-ink-400">
        <Inbox className="h-7 w-7" strokeWidth={1.75} />
        <p className="text-sm">No questions matched "{keyword}".</p>
      </div>
    );
  }

  // Narrowing dropdowns, built from whatever's actually in this result set -- filtered client-side
  // (no extra API call): these fields are already in every result card. Shown even when a dropdown
  // would only have one option, so it's always obvious filtering is available here, not just when
  // results happen to be mixed.
  const exams = [...new Set(results.map((r) => r.examName))].sort();
  const subjects = [...new Set(results.map((r) => r.subject))].sort();
  const years = [...new Set(results.map((r) => r.year))].sort((a, b) => b - a);
  const languages = [...new Set(results.map((r) => r.language))].sort();

  const filtered = results.filter(
    (r) =>
      (!filterExam || r.examName === filterExam) &&
      (!filterSubject || r.subject === filterSubject) &&
      (!filterYear || r.year === Number(filterYear)) &&
      (!filterLanguage || r.language === filterLanguage)
  );

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <MiniFilter label="Exam" value={filterExam} onChange={setFilterExam} options={exams} />
        <MiniFilter label="Subject" value={filterSubject} onChange={setFilterSubject} options={subjects} />
        <MiniFilter label="Year" value={filterYear} onChange={setFilterYear} options={years} />
        <MiniFilter label="Language" value={filterLanguage} onChange={setFilterLanguage} options={languages} />
        {(filterExam || filterSubject || filterYear || filterLanguage) && (
          <button
            type="button"
            onClick={() => {
              setFilterExam("");
              setFilterSubject("");
              setFilterYear("");
              setFilterLanguage("");
            }}
            className="text-xs font-semibold text-secondary-500 hover:underline"
          >
            Clear
          </button>
        )}
      </div>

      <p className="text-xs font-medium text-ink-400">
        {filtered.length === results.length
          ? `${results.length} result${results.length === 1 ? "" : "s"}`
          : `${filtered.length} of ${results.length} results`}
      </p>
      {filtered.map((r) =>
        expandedId === r.questionId ? (
          <ExpandedQuestion key={r.questionId} questionId={r.questionId} onCollapse={() => setExpandedId(null)} />
        ) : (
          <button key={r.questionId} type="button" onClick={() => setExpandedId(r.questionId)} className="text-left">
            <div className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card transition-shadow hover:shadow-cardHover">
              <div className="flex flex-wrap items-center gap-1.5">
                {logoSrc(r.examLogoUrl) && <img src={logoSrc(r.examLogoUrl)} alt="" className="h-5 w-5 rounded object-cover" />}
                <span className="rounded-md bg-secondary-50 px-2 py-1 text-[11px] font-semibold text-secondary-500">
                  {r.examName} {r.year}
                </span>
                <span className="rounded-md bg-violet-50 px-2 py-1 text-[11px] font-semibold text-violet-500">{r.subject}</span>
              </div>
              <p className="mt-2 text-[15px] font-semibold leading-snug text-ink-900">{r.questionText}</p>
              {logoSrc(r.questionImageUrl) && (
                <img src={logoSrc(r.questionImageUrl)} alt="" className="mt-2 max-h-32 rounded-lg border border-primary-100" />
              )}
            </div>
          </button>
        )
      )}
    </div>
  );
}

function ExpandedQuestion({ questionId, onCollapse }) {
  const navigate = useNavigate();
  const [question, setQuestion] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    getQuestionById(questionId).then(setQuestion).finally(() => setLoading(false));
  }, [questionId]);

  return (
    <div className="rounded-xl2 border-2 border-secondary-500 bg-white p-4 shadow-card">
      <div className="mb-2 flex items-center justify-between">
        <button type="button" onClick={onCollapse} className="flex items-center gap-1 text-xs font-semibold text-ink-400 hover:text-ink-600">
          <X className="h-3.5 w-3.5" strokeWidth={2.5} />
          Collapse
        </button>
        <button
          type="button"
          onClick={() => navigate(`/questions/${questionId}`)}
          className="flex items-center gap-1 text-xs font-semibold text-secondary-500 hover:text-secondary-600"
        >
          Answer & discussion
          <ArrowRight className="h-3.5 w-3.5" strokeWidth={2.5} />
        </button>
      </div>
      {loading ? (
        <p className="py-6 text-center text-sm text-ink-400">Loading…</p>
      ) : question ? (
        <QuestionCard question={question} />
      ) : (
        <p className="py-6 text-center text-sm text-red-600">Couldn't load this question.</p>
      )}
    </div>
  );
}

// ---------- Browse by Exam (live filters -- not a step-by-step wizard) ----------
// Every filter below is independent and combinable via GET /api/questions
// (QuestionSearchQuery: ExamId/Year/Language/Subject/DifficultyLevel all optional, no PaperId
// required). Results appear as soon as an Exam is picked; Year/Language/Subject/Difficulty just
// narrow them further, in any order, at any time.
const DIFFICULTIES = ["Easy", "Medium", "Hard"];

function BrowseByExam({ presetExamId }) {
  const [exams, setExams] = useState([]);
  const [examId, setExamId] = useState(presetExamId || "");

  const [years, setYears] = useState([]);
  const [year, setYear] = useState("");

  const [languages, setLanguages] = useState([]);
  const [language, setLanguage] = useState("");

  const [difficulty, setDifficulty] = useState("");
  const [subject, setSubject] = useState("");
  const [paperCode, setPaperCode] = useState("");
  const [questionNumber, setQuestionNumber] = useState("");
  const [page, setPage] = useState(1);

  const [result, setResult] = useState(null);
  const [status, setStatus] = useState("idle");

  useEffect(() => {
    listExams().then(setExams).catch(() => setExams([]));
  }, []);

  // Adopt ?examId= from the URL -- e.g. a Popular Exam card on Home, or a shared link.
  useEffect(() => {
    if (presetExamId) setExamId(presetExamId);
  }, [presetExamId]);

  // Year options only need examId. Language options need examId+year too -- but that's just to
  // populate valid *choices* in the dropdown, it never blocks results.
  useEffect(() => {
    setYears([]); setYear("");
    setLanguages([]); setLanguage("");
    if (!examId) return;
    getPaperYears(examId).then(setYears).catch(() => setYears([]));
  }, [examId]);

  useEffect(() => {
    setLanguages([]); setLanguage("");
    if (!examId || !year) return;
    getPaperLanguages(examId, year).then(setLanguages).catch(() => setLanguages([]));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [examId, year]);

  useEffect(() => {
    setPage(1);
  }, [examId, year, language, subject, difficulty, paperCode, questionNumber]);

  useEffect(() => {
    if (!examId) {
      setResult(null);
      setStatus("idle");
      return;
    }
    const controller = new AbortController();
    setStatus("loading");
    searchQuestions(
      {
        examId,
        year: year || undefined,
        language: language || undefined,
        subject: subject || undefined,
        difficultyLevel: difficulty || undefined,
        paperCode: paperCode || undefined,
        questionNumber: questionNumber || undefined,
        page,
        pageSize: PAGE_SIZE,
      },
      { signal: controller.signal }
    )
      .then((data) => {
        setResult(data);
        setStatus("success");
      })
      .catch((err) => {
        if (err.name !== "AbortError") setStatus("error");
      });
    return () => controller.abort();
  }, [examId, year, language, subject, difficulty, paperCode, questionNumber, page]);

  const totalPages = result ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : 1;
  const subjectsInView = result ? [...new Set(result.items.map((q) => q.subject))] : [];
  const hasActiveNarrowing = year || language || subject || difficulty || paperCode || questionNumber;

  function clearNarrowing() {
    setYear("");
    setLanguage("");
    setSubject("");
    setDifficulty("");
    setPaperCode("");
    setQuestionNumber("");
  }

  return (
    <div>
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-bold text-ink-900">Filters</h2>
        {hasActiveNarrowing && (
          <button type="button" onClick={clearNarrowing} className="text-xs font-semibold text-secondary-500 hover:text-secondary-600">
            Clear filters
          </button>
        )}
      </div>

      <div className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-3">
        <Dropdown label="Exam" value={examId} onChange={setExamId} placeholder="Choose exam">
          {exams.map((e) => <option key={e.id} value={e.id}>{e.name}</option>)}
        </Dropdown>

        <Dropdown label="Year" value={year} onChange={setYear} placeholder="Any year" disabled={!examId || years.length === 0}>
          {years.map((y) => <option key={y} value={y}>{y}</option>)}
        </Dropdown>

        <Dropdown label="Language" value={language} onChange={setLanguage} placeholder="Any language" disabled={!year || languages.length === 0}>
          {languages.map((l) => <option key={l} value={l}>{l}</option>)}
        </Dropdown>
      </div>

      <div className="mt-4 flex flex-wrap items-center gap-2">
        <span className="text-xs font-semibold text-ink-600">Difficulty:</span>
        <FilterChip active={!difficulty} onClick={() => setDifficulty("")}>Any</FilterChip>
        {DIFFICULTIES.map((d) => (
          <FilterChip key={d} active={difficulty === d} onClick={() => setDifficulty(d)}>{d}</FilterChip>
        ))}
      </div>

      <div className="mt-3 grid grid-cols-2 gap-3 sm:w-1/2">
        <label className="block">
          <span className="mb-1 block text-xs font-semibold text-ink-600">Paper code</span>
          <input
            type="text"
            value={paperCode}
            onChange={(e) => setPaperCode(e.target.value)}
            placeholder="e.g. SETA"
            className="h-11 w-full rounded-xl2 border border-primary-100 bg-white px-3 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500"
          />
        </label>
        <label className="block">
          <span className="mb-1 block text-xs font-semibold text-ink-600">Question no.</span>
          <input
            type="number"
            min="1"
            value={questionNumber}
            onChange={(e) => setQuestionNumber(e.target.value)}
            placeholder="e.g. 25"
            className="h-11 w-full rounded-xl2 border border-primary-100 bg-white px-3 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500"
          />
        </label>
      </div>

      {subjectsInView.length > 0 && (
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <span className="text-xs font-semibold text-ink-600">Subject:</span>
          <FilterChip active={!subject} onClick={() => setSubject("")}>All</FilterChip>
          {subjectsInView.map((s) => (
            <FilterChip key={s} active={subject === s} onClick={() => setSubject(s)}>{s}</FilterChip>
          ))}
        </div>
      )}

      {!examId && (
        <p className="mt-8 rounded-xl2 border border-primary-100 bg-white py-10 text-center text-sm text-ink-400">
          Pick an exam above to start browsing its questions.
        </p>
      )}

      {examId && status === "loading" && (
        <div className="mt-8 flex justify-center py-10 text-ink-400">
          <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
        </div>
      )}

      {examId && status === "error" && (
        <p className="mt-8 rounded-xl2 border border-red-100 bg-red-50 p-4 text-center text-sm text-red-600">
          Couldn't load questions right now.
        </p>
      )}

      {examId && status === "success" && result.items.length === 0 && (
        <p className="mt-8 rounded-xl2 border border-primary-100 bg-white py-10 text-center text-sm text-ink-400">
          {hasActiveNarrowing ? "No questions match these filters \u2014 try loosening one." : "No questions for this exam yet."}
        </p>
      )}

      {examId && status === "success" && result.items.length > 0 && (
        <>
          <p className="mt-6 text-xs font-medium text-ink-400">
            {result.totalCount} question{result.totalCount === 1 ? "" : "s"}
          </p>
          <div className="mt-2 grid grid-cols-1 gap-3 lg:grid-cols-2">
            {result.items.map((q) => (
              <QuestionLink key={q.id} question={q} />
            ))}
          </div>

          {totalPages > 1 && (
            <div className="mt-6 flex items-center justify-center gap-3">
              <button
                type="button"
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="flex h-9 w-9 items-center justify-center rounded-lg border border-primary-100 text-primary-600 disabled:opacity-40"
              >
                <ChevronLeft className="h-4 w-4" strokeWidth={2.5} />
              </button>
              <span className="text-sm font-medium text-ink-600">Page {page} of {totalPages}</span>
              <button
                type="button"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                className="flex h-9 w-9 items-center justify-center rounded-lg border border-primary-100 text-primary-600 disabled:opacity-40"
              >
                <ChevronRight className="h-4 w-4" strokeWidth={2.5} />
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function FilterChip({ active, onClick, children }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded-full px-3 py-1 text-xs font-semibold transition-colors ${
        active ? "bg-primary-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"
      }`}
    >
      {children}
    </button>
  );
}

// Compact inline select for narrowing instant-search results -- unlike Dropdown below, no label
// stacked above it, since these sit in a single-line filter row rather than a form grid.
function MiniFilter({ label, value, onChange, options }) {
  return (
    <select
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className={`h-8 rounded-full border px-2.5 text-xs font-semibold focus:border-secondary-500 ${
        value ? "border-primary-600 bg-primary-50 text-primary-600" : "border-primary-100 bg-white text-ink-600"
      }`}
    >
      <option value="">{label}: Any</option>
      {options.map((opt) => (
        <option key={opt} value={opt}>{opt}</option>
      ))}
    </select>
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

function QuestionLink({ question }) {
  const navigate = useNavigate();
  return (
    <button type="button" onClick={() => navigate(`/questions/${question.id}`)} className="text-left">
      <QuestionCard question={question} />
    </button>
  );
}
