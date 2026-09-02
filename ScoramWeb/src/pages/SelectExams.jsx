import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { GraduationCap, Loader2, Star, AlertCircle } from "lucide-react";
import OrganizationExamPicker from "../components/exams/OrganizationExamPicker";
import { useMyExams } from "../context/MyExamsContext";

// "MY EXAMS" onboarding -- "What are you preparing for?" (spec section 4). Reached from
// AppLayout's redirect the moment an authenticated student has zero exams configured (a genuinely
// new signup, or an existing pre-feature account -- spec section 27 -- that's never set this up).
// Minimum one exam; Primary Exam is optional to pick explicitly here -- if the student skips it,
// the first exam they tapped becomes Primary automatically (see SetMyExamsDto's own comment on the
// backend, ScoramAPI/DTOs/UserExamDTOs.cs).
//
// ORGANIZATION HIERARCHY -- exam selection itself is delegated to OrganizationExamPicker (pick an
// Organization, then pick from its exams) rather than a flat list of every exam at once; this
// screen just owns the selection + Primary Exam state and the Continue action around it.
export default function SelectExams() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const redirectTo = searchParams.get("redirect") || "/";
  const { save } = useMyExams();

  const [selectedIds, setSelectedIds] = useState([]);
  const [examNames, setExamNames] = useState({}); // examId -> examName, for the Primary chips below
  const [primaryId, setPrimaryId] = useState(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  function toggleExam(examId, examName) {
    setSelectedIds((prev) => {
      const isSelected = prev.includes(examId);
      const next = isSelected ? prev.filter((id) => id !== examId) : [...prev, examId];
      // Keep Primary valid: clearing the only selection drops it; removing the current Primary
      // hands it to whichever exam is now first, so "Continue" never has to fight an invalid state.
      if (isSelected && primaryId === examId) setPrimaryId(next[0] || null);
      if (!isSelected && !primaryId) setPrimaryId(examId);
      return next;
    });
    setExamNames((prev) => ({ ...prev, [examId]: examName }));
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

      <div className="mt-6">
        <OrganizationExamPicker selectedIds={selectedIds} onToggle={toggleExam} />
      </div>

      {selectedIds.length > 1 && (
        <div className="mt-6">
          <h2 className="text-sm font-bold text-ink-900">What's your primary target?</h2>
          <p className="mt-0.5 text-xs text-ink-400">
            Optional — we use this to prioritize your Home recommendations.
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            {selectedIds.map((examId) => {
              const isPrimary = primaryId === examId;
              return (
                <button
                  key={examId}
                  type="button"
                  onClick={() => setPrimaryId(examId)}
                  className={`flex items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-semibold transition-colors ${
                    isPrimary
                      ? "bg-primary-600 text-white"
                      : "bg-primary-50 text-primary-600 hover:bg-primary-100"
                  }`}
                >
                  <Star className="h-3 w-3" strokeWidth={2.5} fill={isPrimary ? "currentColor" : "none"} />
                  {examNames[examId] || "Exam"}
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
    </div>
  );
}
