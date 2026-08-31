import { useEffect, useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { Check, GraduationCap, Loader2, Star, AlertCircle } from "lucide-react";
import { listExams } from "../api/exams";
import { useMyExams } from "../context/MyExamsContext";

// "MY EXAMS" onboarding -- "What are you preparing for?" (spec section 4). Reached from
// AppLayout's redirect the moment an authenticated student has zero exams configured (a genuinely
// new signup, or an existing pre-feature account -- spec section 27 -- that's never set this up).
// Minimum one exam; Primary Exam is optional to pick explicitly here -- if the student skips it,
// the first exam they tapped becomes Primary automatically (see SetMyExamsDto's own comment on the
// backend, ScoramAPI/DTOs/UserExamDTOs.cs).
export default function SelectExams() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const redirectTo = searchParams.get("redirect") || "/";
  const { save } = useMyExams();

  const [exams, setExams] = useState([]);
  const [status, setStatus] = useState("loading"); // loading | ready | error
  const [selectedIds, setSelectedIds] = useState([]);
  const [primaryId, setPrimaryId] = useState(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  useEffect(() => {
    let cancelled = false;
    listExams()
      .then((res) => {
        if (cancelled) return;
        setExams(res || []);
        setStatus("ready");
      })
      .catch(() => !cancelled && setStatus("error"));
    return () => { cancelled = true; };
  }, []);

  function toggleExam(examId) {
    setSelectedIds((prev) => {
      const isSelected = prev.includes(examId);
      const next = isSelected ? prev.filter((id) => id !== examId) : [...prev, examId];
      // Keep Primary valid: clearing the only selection drops it; removing the current Primary
      // hands it to whichever exam is now first, so "Continue" never has to fight an invalid state.
      if (isSelected && primaryId === examId) setPrimaryId(next[0] || null);
      if (!isSelected && !primaryId) setPrimaryId(examId);
      return next;
    });
  }

  async function handleContinue() {
    if (selectedIds.length === 0) return;
    setSaving(true);
    setError(null);
    try {
      await save({ examIds: selectedIds, primaryExamId: primaryId });
      navigate(redirectTo, { replace: true });
    } catch (err) {
      setError(err.message || "Couldn't save your exams. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  const selectedSet = useMemo(() => new Set(selectedIds), [selectedIds]);

  return (
    <div className="mx-auto flex min-h-full max-w-lg flex-col px-6 py-10 sm:py-16">
      <span className="flex h-12 w-12 items-center justify-center rounded-full bg-primary-50 text-primary-600">
        <GraduationCap className="h-6 w-6" strokeWidth={2.25} />
      </span>
      <h1 className="mt-4 text-2xl font-extrabold text-ink-900">What are you preparing for?</h1>
      <p className="mt-1.5 text-sm text-ink-500">
        Select every exam you're preparing for. We'll use this to show you relevant PYPs, Question
        Bank questions, Mock Tests, and Practice Tests by default — you can always explore other
        exams or change this later from your profile.
      </p>

      {status === "loading" && (
        <div className="mt-10 flex justify-center">
          <Loader2 className="h-6 w-6 animate-spin text-primary-400" />
        </div>
      )}

      {status === "error" && (
        <div className="mt-6 flex items-center gap-2 rounded-xl2 border border-accent-100 bg-accent-50 p-3 text-sm text-accent-600">
          <AlertCircle className="h-4 w-4 shrink-0" strokeWidth={2.25} />
          Couldn't load the list of exams. Please refresh and try again.
        </div>
      )}

      {status === "ready" && (
        <>
          <div className="mt-6 grid grid-cols-1 gap-2.5 sm:grid-cols-2">
            {exams.map((exam) => {
              const isSelected = selectedSet.has(exam.id);
              return (
                <button
                  key={exam.id}
                  type="button"
                  onClick={() => toggleExam(exam.id)}
                  className={`flex items-center gap-3 rounded-xl2 border p-3.5 text-left transition-colors ${
                    isSelected
                      ? "border-primary-400 bg-primary-50"
                      : "border-primary-100 bg-white hover:border-primary-300"
                  }`}
                >
                  <span
                    className={`flex h-5 w-5 shrink-0 items-center justify-center rounded-md border-2 ${
                      isSelected ? "border-primary-600 bg-primary-600 text-white" : "border-ink-200"
                    }`}
                  >
                    {isSelected && <Check className="h-3.5 w-3.5" strokeWidth={3} />}
                  </span>
                  <span className="min-w-0 flex-1 truncate text-sm font-bold text-ink-900">{exam.name}</span>
                </button>
              );
            })}
          </div>

          {selectedIds.length > 1 && (
            <div className="mt-6">
              <h2 className="text-sm font-bold text-ink-900">What's your primary target?</h2>
              <p className="mt-0.5 text-xs text-ink-400">
                Optional — we use this to prioritize your Home recommendations.
              </p>
              <div className="mt-3 flex flex-wrap gap-2">
                {exams
                  .filter((exam) => selectedSet.has(exam.id))
                  .map((exam) => {
                    const isPrimary = primaryId === exam.id;
                    return (
                      <button
                        key={exam.id}
                        type="button"
                        onClick={() => setPrimaryId(exam.id)}
                        className={`flex items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-semibold transition-colors ${
                          isPrimary
                            ? "bg-primary-600 text-white"
                            : "bg-primary-50 text-primary-600 hover:bg-primary-100"
                        }`}
                      >
                        <Star className="h-3 w-3" strokeWidth={2.5} fill={isPrimary ? "currentColor" : "none"} />
                        {exam.name}
                      </button>
                    );
                  })}
              </div>
            </div>
          )}

          {error && (
            <div className="mt-4 flex items-center gap-2 rounded-xl2 border border-accent-100 bg-accent-50 p-3 text-sm text-accent-600">
              <AlertCircle className="h-4 w-4 shrink-0" strokeWidth={2.25} />
              {error}
            </div>
          )}

          <button
            type="button"
            onClick={handleContinue}
            disabled={selectedIds.length === 0 || saving}
            className="mt-8 flex w-full items-center justify-center gap-2 rounded-xl2 bg-primary-600 py-3 text-sm font-bold text-white transition-colors hover:bg-primary-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            Continue
          </button>
        </>
      )}
    </div>
  );
}
