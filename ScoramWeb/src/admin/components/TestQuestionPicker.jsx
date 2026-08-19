import { useEffect, useState } from "react";
import { Search, Plus, Check, Loader2 } from "lucide-react";
import { listQuestionBankQuestions } from "../api/questionBank";

// Question Bank is the recommended source for assembling a test paper (spec's own architecture:
// "Question Bank remains the MASTER source for reusable questions... if Practice/Mock Tests need
// PYQ questions, use Question Bank references" -- so legacy Papers aren't searched here; import
// them into the Question Bank first if they need to appear in a test).
export default function TestQuestionPicker({ token, selectedRefs, onAdd }) {
  const [search, setSearch] = useState("");
  const [results, setResults] = useState([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    const handle = setTimeout(() => {
      setLoading(true);
      listQuestionBankQuestions(token, { search, page: 1, pageSize: 15 })
        .then((res) => setResults(res.items))
        .catch(() => setResults([]))
        .finally(() => setLoading(false));
    }, 300);
    return () => clearTimeout(handle);
  }, [search, token]);

  const selectedIds = new Set(selectedRefs.map((r) => r.questionBankQuestionId).filter(Boolean));

  return (
    <div>
      <label className="relative block">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" strokeWidth={2} />
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search Question Bank by keyword..."
          className="h-10 w-full rounded-lg border border-primary-100 bg-white pl-9 pr-3 text-sm focus:border-secondary-500"
        />
      </label>

      <div className="mt-2 max-h-72 overflow-y-auto rounded-lg border border-primary-100">
        {loading && (
          <div className="flex justify-center py-6 text-ink-400">
            <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.25} />
          </div>
        )}
        {!loading && results.length === 0 && <p className="p-4 text-center text-xs text-ink-400">No questions found.</p>}
        {!loading && results.map((q) => {
          const isSelected = selectedIds.has(q.id);
          return (
            <button
              key={q.id}
              type="button"
              disabled={isSelected}
              onClick={() => onAdd({ questionBankQuestionId: q.id })}
              className="flex w-full items-start gap-2 border-t border-primary-50 px-3 py-2.5 text-left text-xs first:border-t-0 disabled:opacity-50"
            >
              {isSelected ? <Check className="mt-0.5 h-3.5 w-3.5 shrink-0 text-mint-500" strokeWidth={2.5} /> : <Plus className="mt-0.5 h-3.5 w-3.5 shrink-0 text-secondary-500" strokeWidth={2.25} />}
              <span className="min-w-0 flex-1">
                <span className="line-clamp-2 text-ink-900">{q.questionText}</span>
                <span className="mt-0.5 block text-[11px] text-ink-400">{q.subject} / {q.topic}</span>
              </span>
            </button>
          );
        })}
      </div>

      {selectedRefs.length > 0 && (
        <div className="mt-3">
          <p className="text-xs font-semibold text-ink-600">{selectedRefs.length} question(s) selected</p>
        </div>
      )}
    </div>
  );
}
