import { useEffect, useMemo, useState } from "react";
import { AlertTriangle, X, Trash2 } from "lucide-react";
import { getExamDeleteOptions, deleteExamCascade } from "../api/exams";
import { Card, Button, FormField, TextInput, Alert, friendlyError } from "./AdminUI";

const USAGE_LABELS = {
  studentAnswers: "student answers",
  bookmarks: "bookmarks",
  mockTestUsages: "mock test entries",
  quizUsages: "quiz entries",
  practiceTestUsages: "practice test entries",
  comments: "comments",
  votes: "votes/likes",
  reports: "reports",
  solutions: "alternative solutions",
};

function paperLabel(p) {
  const parts = [`${p.year}`];
  if (p.tier) parts.push(p.tier);
  if (p.shift) parts.push(p.shift);
  if (p.examDate) parts.push(p.examDate);
  if (p.paperLabel) parts.push(p.paperLabel);
  if (p.paperCode) parts.push(`(${p.paperCode})`);
  parts.push(`· ${p.language}`);
  return parts.join(" — ");
}

// Manage Exam's "delete this exam" flow -- deliberately NOT the plain deleteExam() used elsewhere
// (which the server refuses unless the exam is already empty). This is the force-delete: it can
// remove real questions and real student activity (answers, bookmarks, test entries, comments,
// votes, reports) right along with the exam, so the entire screen is built around section 10's
// spirit -- strict warning first (always visible, can't be scrolled past without seeing it), then
// an explicit choice of what to delete, then a typed-name confirmation as the final gate -- rather
// than a single window.confirm() the way the safe delete gets away with.
export default function ExamDeleteModal({ exam, token, onClose, onDeleted }) {
  const [options, setOptions] = useState(null);
  const [loadError, setLoadError] = useState(null);
  const [mode, setMode] = useState("all"); // "all" | "selective"
  const [selectedPaperIds, setSelectedPaperIds] = useState(new Set());
  const [selectedYears, setSelectedYears] = useState(new Set());
  const [confirmText, setConfirmText] = useState("");
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState(null);
  const [result, setResult] = useState(null);

  useEffect(() => {
    getExamDeleteOptions(token, exam.id)
      .then(setOptions)
      .catch((err) => setLoadError(friendlyError(err)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [exam.id]);

  const usageEntries = useMemo(() => {
    if (!options) return [];
    return Object.entries(USAGE_LABELS)
      .map(([key, label]) => [label, options.usage?.[key] || 0])
      .filter(([, count]) => count > 0);
  }, [options]);

  const isEmpty = options && options.papers.length === 0 && options.pyqYears.length === 0 && options.legacyQuestionCount === 0;

  function togglePaper(id) {
    setSelectedPaperIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleYear(year) {
    setSelectedYears((prev) => {
      const next = new Set(prev);
      if (next.has(year)) next.delete(year);
      else next.add(year);
      return next;
    });
  }

  const nameMatches = confirmText.trim().toLowerCase() === exam.name.trim().toLowerCase();
  const hasSelection = mode === "all" || selectedPaperIds.size > 0 || selectedYears.size > 0;
  const canDelete = !isEmpty ? nameMatches && hasSelection : nameMatches;

  async function handleDelete() {
    setDeleting(true);
    setError(null);
    try {
      const res = await deleteExamCascade(token, exam.id, {
        confirmExamName: confirmText,
        deleteAll: mode === "all" || isEmpty,
        paperIds: mode === "selective" ? Array.from(selectedPaperIds) : null,
        pyqYears: mode === "selective" ? Array.from(selectedYears) : null,
      });
      setResult(res);
      onDeleted?.(exam.id, res);
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setDeleting(false);
    }
  }

  return (
    <Card className="mx-auto max-w-2xl border-2 border-red-200">
      <div className="flex items-center justify-between">
        <h3 className="flex items-center gap-1.5 text-sm font-bold text-red-600">
          <Trash2 className="h-4 w-4" strokeWidth={2.5} />
          Delete "{exam.name}"
        </h3>
        <button type="button" onClick={onClose} className="text-ink-400 hover:text-ink-600">
          <X className="h-4 w-4" strokeWidth={2.25} />
        </button>
      </div>

      {loadError && <div className="mt-3"><Alert>{loadError}</Alert></div>}

      {!options && !loadError && <p className="mt-3 text-sm text-ink-400">Loading what's on this exam…</p>}

      {result && (
        <div className="mt-3">
          <Alert type="success">
            Deleted {result.papersDeleted} paper(s), {result.pypQuestionsDeleted} PYP question(s), and{" "}
            {result.pyqQuestionsDeleted} PYQ question(s) ({result.pyqMappingsRemoved} more PYQ mapping(s) removed
            without deleting the question, since it's still tagged to another exam).{" "}
            {result.examDeleted
              ? "The exam itself has been removed."
              : "The exam itself was kept -- it still has other content on it."}
          </Alert>
          <div className="mt-3">
            <Button variant="secondary" onClick={onClose}>Close</Button>
          </div>
        </div>
      )}

      {options && !result && (
        <div className="mt-3 flex flex-col gap-4">
          {/* ---------- Strict warning -- always visible, before any control ---------- */}
          <div className="flex flex-col gap-1.5 rounded-xl2 border border-red-200 bg-red-50 p-3">
            <div className="flex items-center gap-1.5 text-sm font-bold text-red-600">
              <AlertTriangle className="h-4 w-4" strokeWidth={2.5} />
              This cannot be undone.
            </div>
            {isEmpty ? (
              <p className="text-xs text-red-600">
                This exam has no papers, PYQ questions, or legacy content. Deleting it will only remove the exam
                entry itself.
              </p>
            ) : (
              <>
                <p className="text-xs text-red-600">
                  Deleting will permanently remove the papers/PYQs you choose below, and every real question under
                  them -- {options.papers.length} paper(s), {options.pyqYears.reduce((s, y) => s + y.questionCount, 0)}{" "}
                  PYQ question(s) across {options.pyqYears.length} year(s)
                  {options.legacyQuestionCount > 0 ? `, ${options.legacyQuestionCount} legacy question(s)` : ""}.
                </p>
                {usageEntries.length > 0 && (
                  <p className="text-xs text-red-600">
                    It will also permanently delete real student activity attached to that content:{" "}
                    {usageEntries.map(([label, count], i) => (
                      <span key={label}>
                        {i > 0 ? ", " : ""}
                        <strong>{count}</strong> {label}
                      </span>
                    ))}
                    .
                  </p>
                )}
              </>
            )}
          </div>

          {/* ---------- What to delete ---------- */}
          {!isEmpty && (
            <div className="flex flex-col gap-3">
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={() => setMode("all")}
                  className={`rounded-xl2 px-3.5 py-2 text-xs font-semibold transition-colors ${mode === "all" ? "bg-red-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"}`}
                >
                  Delete everything on this exam
                </button>
                <button
                  type="button"
                  onClick={() => setMode("selective")}
                  className={`rounded-xl2 px-3.5 py-2 text-xs font-semibold transition-colors ${mode === "selective" ? "bg-red-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"}`}
                >
                  Choose specific papers / PYQ years
                </button>
              </div>

              {mode === "selective" && (
                <div className="flex flex-col gap-3">
                  {options.papers.length > 0 && (
                    <div>
                      <p className="mb-1 text-xs font-semibold text-ink-600">Papers</p>
                      <div className="flex max-h-40 flex-col gap-1 overflow-y-auto rounded-lg border border-primary-100 p-2">
                        {options.papers.map((p) => (
                          <label key={p.paperId} className="flex items-center gap-2 text-xs text-ink-700">
                            <input
                              type="checkbox"
                              checked={selectedPaperIds.has(p.paperId)}
                              onChange={() => togglePaper(p.paperId)}
                            />
                            {paperLabel(p)} — {p.questionCount} question(s) — {p.status}
                          </label>
                        ))}
                      </div>
                    </div>
                  )}
                  {options.pyqYears.length > 0 && (
                    <div>
                      <p className="mb-1 text-xs font-semibold text-ink-600">PYQ (Question Bank) years</p>
                      <div className="flex max-h-40 flex-col gap-1 overflow-y-auto rounded-lg border border-primary-100 p-2">
                        {options.pyqYears.map((y) => (
                          <label key={y.year} className="flex items-center gap-2 text-xs text-ink-700">
                            <input
                              type="checkbox"
                              checked={selectedYears.has(y.year)}
                              onChange={() => toggleYear(y.year)}
                            />
                            {y.year} — {y.questionCount} question(s)
                          </label>
                        ))}
                      </div>
                    </div>
                  )}
                  {!hasSelection && (
                    <p className="text-xs text-accent-600">Select at least one paper or PYQ year above.</p>
                  )}
                </div>
              )}
            </div>
          )}

          {/* ---------- Final confirmation gate ---------- */}
          <FormField label={<>Type the exam name (<strong>{exam.name}</strong>) to confirm</>}>
            <TextInput value={confirmText} onChange={(e) => setConfirmText(e.target.value)} placeholder={exam.name} autoComplete="off" />
          </FormField>

          {error && <Alert>{error}</Alert>}

          <div className="flex gap-2">
            <Button variant="danger" onClick={handleDelete} disabled={!canDelete} isLoading={deleting}>
              <Trash2 className="h-4 w-4" strokeWidth={2.25} />
              Permanently delete
            </Button>
            <Button variant="ghost" onClick={onClose}>Cancel</Button>
          </div>
        </div>
      )}
    </Card>
  );
}
