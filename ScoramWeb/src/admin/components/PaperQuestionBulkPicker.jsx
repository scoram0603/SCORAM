import { useEffect, useState } from "react";
import { Search, CheckSquare, Square, Loader2, Layers } from "lucide-react";
import { listQuestionBankQuestions } from "../api/questionBank";
import { Button, Alert, friendlyError } from "./AdminUI";

// Companion to TestQuestionPicker (used by Mock/Practice Tests too, deliberately left untouched --
// see that file's own comment on why it's shared). This one is Paper-specific: it pre-filters the
// Question Bank by the paper's OWN Exam+Year (QuestionBankExamMapping already tags questions this
// way -- see QuestionBankController.Search) so an admin building "SSC CGL 2025" immediately sees
// "here are the SSC CGL 2025 questions already sitting in the Question Bank", and lets them
// multi-select + add them all in one call via PapersController.MapQuestionsBulk instead of one at a
// time. Q.No for everything added this way is auto-assigned and NOT the real original position --
// see the notice this renders and PaperQuestionBankLink.IsNumberExact.
export default function PaperQuestionBulkPicker({ token, examId, year, mappedQuestionBankIds, onBulkAdd }) {
  const [search, setSearch] = useState("");
  const [useExamYearFilter, setUseExamYearFilter] = useState(true);
  const [results, setResults] = useState([]);
  const [totalCount, setTotalCount] = useState(null);
  const [loading, setLoading] = useState(false);
  const [selected, setSelected] = useState(new Set());
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState(null);

  useEffect(() => {
    setSelected(new Set());
  }, [examId, year, useExamYearFilter]);

  useEffect(() => {
    const handle = setTimeout(() => {
      setLoading(true);
      const params = { search, page: 1, pageSize: 50 };
      if (useExamYearFilter && examId && year) {
        params.examId = examId;
        params.year = year;
      }
      listQuestionBankQuestions(token, params)
        .then((res) => {
          setResults(res.items);
          setTotalCount(res.totalCount);
        })
        .catch(() => {
          setResults([]);
          setTotalCount(null);
        })
        .finally(() => setLoading(false));
    }, 300);
    return () => clearTimeout(handle);
  }, [search, useExamYearFilter, examId, year, token]);

  const mappedIds = new Set(mappedQuestionBankIds);

  function toggle(id) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  async function handleAddSelected() {
    if (selected.size === 0) return;
    setAdding(true);
    setError(null);
    try {
      await onBulkAdd(Array.from(selected));
      setSelected(new Set());
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setAdding(false);
    }
  }

  return (
    <div>
      {useExamYearFilter && examId && year && (
        <div className="mb-3 flex items-start gap-2 rounded-lg bg-accent-50 p-2.5 text-xs text-accent-600">
          <Layers className="mt-0.5 h-3.5 w-3.5 shrink-0" strokeWidth={2.25} />
          <span>
            Showing Question Bank questions already tagged for this exam/year
            {totalCount != null ? ` (${totalCount} found)` : ""}. Q.No for anything added here is
            auto-assigned, not the exact original position -- students will get these questions in a
            subject-grouped order instead of a numbered sequence.
          </span>
        </div>
      )}

      <div className="flex items-center gap-2">
        <label className="relative block flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" strokeWidth={2} />
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search Question Bank by keyword..."
            className="h-10 w-full rounded-lg border border-primary-100 bg-white pl-9 pr-3 text-sm focus:border-secondary-500"
          />
        </label>
        {examId && year && (
          <button
            type="button"
            onClick={() => setUseExamYearFilter((v) => !v)}
            className={`h-10 shrink-0 whitespace-nowrap rounded-lg border px-3 text-xs font-semibold ${
              useExamYearFilter
                ? "border-accent-500 bg-accent-50 text-accent-600"
                : "border-primary-100 text-ink-400"
            }`}
          >
            This exam/year only
          </button>
        )}
      </div>

      {error && <div className="mt-3"><Alert>{error}</Alert></div>}

      <div className="mt-2 max-h-80 overflow-y-auto rounded-lg border border-primary-100">
        {loading && (
          <div className="flex justify-center py-6 text-ink-400">
            <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.25} />
          </div>
        )}
        {!loading && results.length === 0 && (
          <p className="p-4 text-center text-xs text-ink-400">
            No matching questions found{useExamYearFilter ? " for this exam/year" : ""}.
          </p>
        )}
        {!loading && results.map((q) => {
          const isMapped = mappedIds.has(q.id);
          const isChecked = selected.has(q.id);
          return (
            <button
              key={q.id}
              type="button"
              disabled={isMapped}
              onClick={() => toggle(q.id)}
              className="flex w-full items-start gap-2 border-t border-primary-50 px-3 py-2.5 text-left text-xs first:border-t-0 disabled:opacity-50"
            >
              {isMapped ? (
                <CheckSquare className="mt-0.5 h-3.5 w-3.5 shrink-0 text-mint-500" strokeWidth={2.5} />
              ) : isChecked ? (
                <CheckSquare className="mt-0.5 h-3.5 w-3.5 shrink-0 text-accent-500" strokeWidth={2.5} />
              ) : (
                <Square className="mt-0.5 h-3.5 w-3.5 shrink-0 text-ink-400" strokeWidth={2.25} />
              )}
              <span className="min-w-0 flex-1">
                <span className="line-clamp-2 text-ink-900">{q.questionText}</span>
                <span className="mt-0.5 block text-[11px] text-ink-400">
                  {q.subject} / {q.topic}{isMapped ? " · already mapped" : ""}
                </span>
              </span>
            </button>
          );
        })}
      </div>

      <div className="mt-3 flex items-center gap-3">
        <Button variant="secondary" isLoading={adding} disabled={selected.size === 0} onClick={handleAddSelected}>
          Add {selected.size > 0 ? `${selected.size} ` : ""}Selected
        </Button>
        {selected.size > 0 && (
          <button type="button" className="text-xs font-semibold text-ink-400 hover:text-ink-600" onClick={() => setSelected(new Set())}>
            Clear selection
          </button>
        )}
      </div>
    </div>
  );
}
