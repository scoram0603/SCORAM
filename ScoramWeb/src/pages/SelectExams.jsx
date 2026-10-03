import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { GraduationCap, Loader2, Star, AlertCircle, X } from "lucide-react";
import OrganizationExamPicker from "../components/exams/OrganizationExamPicker";
import { useMyExams } from "../context/MyExamsContext";

// "MY EXAMS" first-time selection. AppLayout sends an authenticated student here the moment they
// have no exams (a new signup, or anyone who skipped / cleared My Exams in an earlier session).
// Existing students who already chose exams under the old "Preparing For" name are NOT sent here --
// that data was always stored as My Exams.
//
// Multi-select with server-side search (OrganizationExamPicker). "Continue" saves the whole
// selection in one PUT; "Skip for Now" saves NOTHING -- My Exams stays empty (it never silently
// selects every exam) and the app shows "Choose My Exams" prompts where exam content would be.
// Primary exam stays optional: with 2+ exams the first one tapped is primary unless changed.
export default function SelectExams() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const redirectTo = searchParams.get("redirect") || "/";
  const { save, skipOnboarding } = useMyExams();

  const [selectedIds, setSelectedIds] = useState([]);
  const [examNames, setExamNames] = useState({}); // examId -> examName, for the chips below
  const [primaryId, setPrimaryId] = useState(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  function toggleExam(examId, examName) {
    setError(null);
    setSelectedIds((prev) => {
      const isSelected = prev.includes(examId);
      const next = isSelected ? prev.filter((id) => id !== examId) : [...prev, examId];
      // Keep Primary valid: removing the current Primary hands it to whichever exam is now first.
      if (isSelected && primaryId === examId) setPrimaryId(next[0] || null);
      if (!isSelected && !primaryId) setPrimaryId(examId);
      return next;
    });
    if (examName) setExamNames((prev) => ({ ...prev, [examId]: examName }));
  }

  async function handleContinue() {
    if (selectedIds.length === 0 || saving) return;
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

  function handleSkip() {
    skipOnboarding();
    navigate(redirectTo, { replace: true });
  }

  return (
    <div className="mx-auto flex min-h-full max-w-lg flex-col px-4 py-8 sm:px-6 sm:py-14">
      <span className="flex h-12 w-12 items-center justify-center rounded-full bg-primary-50 text-primary-600">
        <GraduationCap className="h-6 w-6" strokeWidth={2.25} />
      </span>
      <h1 className="mt-4 text-2xl font-extrabold text-ink-900">My Exams</h1>
      <p className="mt-1.5 text-sm font-semibold text-ink-900">Choose the exams you are preparing for.</p>
      <p className="mt-1 text-sm text-ink-600">
        Select one or more exams to personalize your SCORAM experience. You'll see PYPs, Question Bank
        questions, tests, mock tests and groups for these exams — you can change them anytime from your profile.
      </p>

      <div className="mt-5">
        <OrganizationExamPicker selectedIds={selectedIds} onToggle={toggleExam} />
      </div>

      {selectedIds.length > 0 && (
        <div className="mt-5 rounded-xl2 border border-primary-100 bg-white p-3.5 shadow-card">
          <p className="text-xs font-bold uppercase tracking-wide text-ink-400">Selected: {selectedIds.length}</p>
          <div className="mt-2 flex flex-wrap gap-1.5">
            {selectedIds.map((examId) => (
              <span key={examId} className="flex items-center gap-1 rounded-full bg-primary-50 py-1 pl-2.5 pr-1.5 text-xs font-bold text-primary-600">
                {examNames[examId] || "Exam"}
                <button type="button" onClick={() => toggleExam(examId)} aria-label={`Remove ${examNames[examId] || "exam"}`} className="rounded-full p-0.5 hover:bg-primary-100">
                  <X className="h-3 w-3" strokeWidth={2.5} />
                </button>
              </span>
            ))}
          </div>

          {selectedIds.length > 1 && (
            <div className="mt-4 border-t border-primary-50 pt-3">
              <h2 className="text-sm font-bold text-ink-900">What's your primary target?</h2>
              <p className="mt-0.5 text-xs text-ink-400">Optional — used to prioritize your Home recommendations.</p>
              <div className="mt-2 flex flex-wrap gap-2">
                {selectedIds.map((examId) => {
                  const isPrimary = primaryId === examId;
                  return (
                    <button
                      key={examId}
                      type="button"
                      onClick={() => setPrimaryId(examId)}
                      className={`flex items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-semibold transition-colors ${
                        isPrimary ? "bg-primary-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"
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
        </div>
      )}

      {error && (
        <div role="alert" className="mt-4 flex items-center gap-2 rounded-xl2 border border-accent-100 bg-accent-50 p-3 text-sm text-accent-600">
          <AlertCircle className="h-4 w-4 shrink-0" strokeWidth={2.25} />
          {error}
        </div>
      )}

      <div className="sticky bottom-0 -mx-4 mt-6 bg-surface/95 px-4 pb-2 pt-3 backdrop-blur sm:-mx-6 sm:px-6">
        <div className="flex flex-col-reverse gap-2 sm:flex-row sm:items-center sm:justify-between">
          <button
            type="button"
            onClick={handleSkip}
            disabled={saving}
            className="rounded-xl2 px-4 py-3 text-sm font-semibold text-ink-600 transition-colors hover:bg-primary-50 hover:text-ink-900 disabled:cursor-not-allowed disabled:opacity-50"
          >
            Skip for Now
          </button>
          <button
            type="button"
            onClick={handleContinue}
            disabled={selectedIds.length === 0 || saving}
            className="flex items-center justify-center gap-2 rounded-xl2 bg-primary-600 px-8 py-3 text-sm font-bold text-white transition-colors hover:bg-primary-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            Continue{selectedIds.length > 0 ? ` (${selectedIds.length})` : ""}
          </button>
        </div>
      </div>
    </div>
  );
}
