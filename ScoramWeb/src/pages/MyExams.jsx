import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, Loader2, Plus, Star, Trash2, AlertCircle, X } from "lucide-react";
import OrganizationExamPicker from "../components/exams/OrganizationExamPicker";
import { useMyExams } from "../context/MyExamsContext";

// "MY EXAMS" management screen (spec section 13), reached from Profile. Unlike the onboarding
// screen's single batched "Continue" save, every action here (add / remove / set primary) calls
// its own granular endpoint immediately -- see MyExamsContext -- so the list on screen is always
// exactly what's saved server-side, with no separate "unsaved changes" state to track or lose.
export default function MyExams() {
  const navigate = useNavigate();
  const { exams, hasLoaded, addExam, removeExam, setPrimary } = useMyExams();

  const [pendingId, setPendingId] = useState(null); // examId currently mid-action, for inline spinners
  const [error, setError] = useState(null);
  const [adding, setAdding] = useState(false);

  const alreadyAddedIds = useMemo(() => new Set(exams.map((e) => e.examId)), [exams]);

  async function handleRemove(examId) {
    setPendingId(examId);
    setError(null);
    try {
      await removeExam(examId);
    } catch (err) {
      setError(err.message || "Couldn't remove that exam.");
    } finally {
      setPendingId(null);
    }
  }

  async function handleSetPrimary(examId) {
    setPendingId(examId);
    setError(null);
    try {
      await setPrimary(examId);
    } catch (err) {
      setError(err.message || "Couldn't update your primary exam.");
    } finally {
      setPendingId(null);
    }
  }

  async function handleAdd(examId) {
    setPendingId(examId);
    setError(null);
    try {
      await addExam(examId);
    } catch (err) {
      setError(err.message || "Couldn't add that exam.");
    } finally {
      setPendingId(null);
    }
  }

  return (
    <div className="mx-auto max-w-lg px-6 py-8 sm:py-12">
      <button
        type="button"
        onClick={() => navigate(-1)}
        className="flex items-center gap-1.5 text-sm font-semibold text-ink-500 hover:text-ink-700"
      >
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
        Back
      </button>

      <h1 className="mt-4 text-xl font-extrabold text-ink-900">My Exams</h1>
      <p className="mt-1 text-sm text-ink-500">
        These are the exams PYP, Question Bank, Mock Tests, and Practice Tests default to. You can
        still explore any other exam any time — this just sets what shows up first.
      </p>

      {!hasLoaded && (
        <div className="mt-10 flex justify-center">
          <Loader2 className="h-6 w-6 animate-spin text-primary-400" />
        </div>
      )}

      {error && (
        <div className="mt-4 flex items-center gap-2 rounded-xl2 border border-accent-100 bg-accent-50 p-3 text-sm text-accent-600">
          <AlertCircle className="h-4 w-4 shrink-0" strokeWidth={2.25} />
          {error}
        </div>
      )}

      {hasLoaded && (
        <div className="mt-6 space-y-2.5">
          {exams.map((exam) => {
            const isPending = pendingId === exam.examId;
            return (
              <div
                key={exam.examId}
                className="flex items-center gap-3 rounded-xl2 border border-primary-100 bg-white p-3.5 shadow-card"
              >
                <span className="min-w-0 flex-1 truncate text-sm font-bold text-ink-900">{exam.examName}</span>

                <button
                  type="button"
                  onClick={() => handleSetPrimary(exam.examId)}
                  disabled={isPending || exam.isPrimary}
                  title={exam.isPrimary ? "Primary exam" : "Set as primary"}
                  className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full transition-colors ${
                    exam.isPrimary ? "bg-amber-100 text-amber-500" : "bg-ink-50 text-ink-300 hover:text-amber-500"
                  }`}
                >
                  <Star className="h-4 w-4" strokeWidth={2.25} fill={exam.isPrimary ? "currentColor" : "none"} />
                </button>

                <button
                  type="button"
                  onClick={() => handleRemove(exam.examId)}
                  disabled={isPending || exams.length === 1}
                  title={exams.length === 1 ? "Select another exam before removing your last one" : "Remove"}
                  className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-ink-50 text-ink-300 transition-colors hover:bg-red-50 hover:text-red-500 disabled:cursor-not-allowed disabled:opacity-40"
                >
                  {isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Trash2 className="h-4 w-4" strokeWidth={2.25} />}
                </button>
              </div>
            );
          })}

          {!adding && (
            <button
              type="button"
              onClick={() => setAdding(true)}
              className="flex w-full items-center justify-center gap-1.5 rounded-xl2 border border-dashed border-primary-200 py-3 text-sm font-semibold text-primary-600 hover:border-primary-400 hover:bg-primary-50"
            >
              <Plus className="h-4 w-4" strokeWidth={2.5} />
              Add Exam
            </button>
          )}

          {adding && (
            <div className="rounded-xl2 border border-primary-100 bg-white p-3.5 shadow-card">
              <div className="mb-2 flex items-center justify-between">
                <h2 className="text-sm font-bold text-ink-900">Add Exam</h2>
                <button
                  type="button"
                  onClick={() => setAdding(false)}
                  className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-ink-300 hover:bg-ink-50"
                >
                  <X className="h-4 w-4" strokeWidth={2.25} />
                </button>
              </div>
              <OrganizationExamPicker
                selectedIds={[]}
                excludeIds={alreadyAddedIds}
                pendingId={pendingId}
                onToggle={(examId) => handleAdd(examId)}
              />
            </div>
          )}
        </div>
      )}
    </div>
  );
}
