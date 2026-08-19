import { useEffect, useRef, useState } from "react";
import { Search, Loader2, ServerCrash, Inbox, ChevronLeft, ChevronRight, ChevronDown } from "lucide-react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { searchQuestionBank, getQuestionBankSubjects, getQuestionBankTopics, getQuestionBankExams, getQuestionBankYears } from "../api/questionBank";
import QuestionBankCard from "../components/questions/QuestionBankCard";

const PAGE_SIZE = 10;

// Spec section 2-3: search alone, filters alone, or both together -- all state lives in the URL
// (searchParams) so a result is shareable/bookmarkable and survives a back-navigation from the
// detail page, the same pattern SearchQuestions.jsx already uses for the Paper-based search.
export default function QuestionBankSearch() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();

  const search = searchParams.get("q") || "";
  const subjectId = searchParams.get("subjectId") || "";
  const topicId = searchParams.get("topicId") || "";
  const examId = searchParams.get("examId") || "";
  const year = searchParams.get("year") || "";
  const page = Number(searchParams.get("page") || "1");

  const [subjects, setSubjects] = useState([]);
  const [topics, setTopics] = useState([]);
  const [exams, setExams] = useState([]);
  const [years, setYears] = useState([]);

  const [result, setResult] = useState(null);
  const [status, setStatus] = useState("loading");
  const debounceRef = useRef(null);

  useEffect(() => {
    getQuestionBankSubjects().then(setSubjects).catch(() => {});
    getQuestionBankExams().then(setExams).catch(() => {});
    getQuestionBankYears().then(setYears).catch(() => {});
  }, []);

  // Topic dropdown depends on the chosen Subject (spec section 18).
  useEffect(() => {
    if (!subjectId) {
      setTopics([]);
      return;
    }
    getQuestionBankTopics(subjectId).then(setTopics).catch(() => setTopics([]));
  }, [subjectId]);

  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    setStatus("loading");
    const controller = new AbortController();

    debounceRef.current = setTimeout(() => {
      searchQuestionBank({ search, subjectId, topicId, examId, year, page, pageSize: PAGE_SIZE }, { signal: controller.signal })
        .then((data) => {
          setResult(data);
          setStatus("success");
        })
        .catch((err) => {
          if (err.name === "AbortError") return;
          setStatus("error");
        });
    }, 250);

    return () => {
      clearTimeout(debounceRef.current);
      controller.abort();
    };
  }, [search, subjectId, topicId, examId, year, page]);

  function updateParam(key, value, resetDependents = []) {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      if (value) next.set(key, value);
      else next.delete(key);
      resetDependents.forEach((k) => next.delete(k));
      next.delete("page");
      return next;
    });
  }

  function setPage(nextPage) {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      next.set("page", String(nextPage));
      return next;
    });
  }

  function clearFilters() {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      ["subjectId", "topicId", "examId", "year", "page"].forEach((k) => next.delete(k));
      return next;
    });
  }

  const hasActiveFilters = Boolean(subjectId || topicId || examId || year);
  const totalPages = result ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : 1;

  return (
    <div className="px-4 pb-8 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <h1 className="text-xl font-extrabold text-ink-900 sm:text-2xl">Question Bank</h1>
      <p className="mt-1 text-sm text-ink-400">
        Search any Previous Year Question directly — type a keyword or paste the full question.
      </p>

      <label className="relative mt-5 block">
        <span className="sr-only">Search Question Bank</span>
        <Search className="pointer-events-none absolute left-3.5 top-1/2 h-[18px] w-[18px] -translate-y-1/2 text-ink-400" strokeWidth={2} />
        <input
          type="search"
          value={search}
          onChange={(e) => updateParam("q", e.target.value)}
          placeholder="Search a keyword, or paste the full question..."
          className="h-12 w-full rounded-xl2 border border-primary-100 bg-white pl-10 pr-3 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500"
        />
      </label>

      <div className="mt-4 flex items-center justify-between">
        <h2 className="text-sm font-bold text-ink-900">Filters</h2>
        {hasActiveFilters && (
          <button type="button" onClick={clearFilters} className="text-xs font-semibold text-secondary-500 hover:text-secondary-600">
            Clear Filters
          </button>
        )}
      </div>

      <div className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-4">
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
      </div>

      <div className="mt-6">
        {status === "loading" && (
          <div className="flex flex-col items-center gap-2 py-16 text-ink-400">
            <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
            <p className="text-sm">Searching…</p>
          </div>
        )}

        {status === "error" && (
          <div className="flex flex-col items-center gap-2 rounded-xl2 border border-red-100 bg-red-50 py-10 text-center text-red-600">
            <ServerCrash className="h-7 w-7" strokeWidth={2} />
            <p className="max-w-sm px-6 text-sm font-medium">Couldn't load the Question Bank right now.</p>
          </div>
        )}

        {status === "success" && result.items.length === 0 && (
          <div className="flex flex-col items-center gap-2 py-16 text-ink-400">
            <Inbox className="h-7 w-7" strokeWidth={1.75} />
            <p className="text-sm">
              {search || hasActiveFilters ? "No questions match your search." : "Start typing to find a question."}
            </p>
          </div>
        )}

        {status === "success" && result.items.length > 0 && (
          <>
            <p className="text-xs font-medium text-ink-400">
              {result.totalCount} question{result.totalCount === 1 ? "" : "s"}
            </p>
            <div className="mt-2 grid grid-cols-1 gap-3 lg:grid-cols-2">
              {result.items.map((q) => (
                <button key={q.id} type="button" onClick={() => navigate(`/question-bank/${q.id}`)} className="text-left">
                  <QuestionBankCard question={q} />
                </button>
              ))}
            </div>

            {totalPages > 1 && (
              <div className="mt-6 flex items-center justify-center gap-3">
                <button
                  type="button"
                  disabled={page <= 1}
                  onClick={() => setPage(Math.max(1, page - 1))}
                  className="flex h-9 w-9 items-center justify-center rounded-lg border border-primary-100 text-primary-600 disabled:opacity-40"
                >
                  <ChevronLeft className="h-4 w-4" strokeWidth={2.5} />
                </button>
                <span className="text-sm font-medium text-ink-600">Page {page} of {totalPages}</span>
                <button
                  type="button"
                  disabled={page >= totalPages}
                  onClick={() => setPage(Math.min(totalPages, page + 1))}
                  className="flex h-9 w-9 items-center justify-center rounded-lg border border-primary-100 text-primary-600 disabled:opacity-40"
                >
                  <ChevronRight className="h-4 w-4" strokeWidth={2.5} />
                </button>
              </div>
            )}
          </>
        )}
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
