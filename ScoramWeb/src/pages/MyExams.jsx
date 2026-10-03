import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, Loader2, Plus, Star, AlertCircle, CheckCircle2, X } from "lucide-react";
import OrganizationExamPicker from "../components/exams/OrganizationExamPicker";
import { useMyExams } from "../context/MyExamsContext";

// "MY EXAMS" management screen (Profile -> My Exams). Edits are collected in a local DRAFT and
// saved together with "Update My Exams" (one PUT of the whole selection), so the student can
// add/remove several exams, change their mind, and discard -- nothing is saved half-way.
//
// Removing every exam is allowed: My Exams then goes empty and exam content is replaced by
// "Choose My Exams" prompts (never "all exams"). A short warning explains that before saving.
export default function MyExams() {
  const navigate = useNavigate();
  const { exams, hasLoaded, save } = useMyExams();

  // draft: [{ examId, examName }] in display order; primaryId: examId | null
  const [draft, setDraft] = useState([]);
  const [primaryId, setPrimaryId] = useState(null);
  const [initialised, setInitialised] = useState(false);
  const [adding, setAdding] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);
  const [saved, setSaved] = useState(false);
  const savedTimer = useRef(null);

  // Seed the draft from the saved list once it has loaded.
  useEffect(() => {
    if (!hasLoaded || initialised) return;
    setDraft(exams.map((e) => ({ examId: e.examId, examName: e.examName })));
    setPrimaryId(exams.find((e) => e.isPrimary)?.examId || null);
    setInitialised(true);
  }, [hasLoaded, initialised, exams]);

  useEffect(() => () => clearTimeout(savedTimer.current), []);

  const draftIds = useMemo(() => draft.map((d) => d.examId), [draft]);
  const savedIds = useMemo(() => exams.map((e) => e.examId), [exams]);
  const savedPrimaryId = useMemo(() => exams.find((e) => e.isPrimary)?.examId || null, [exams]);

  const isDirty = useMemo(() => {
    if (draftIds.length !== savedIds.length) return true;
    const savedSet = new Set(savedIds);
    if (!draftIds.every((id) => savedSet.has(id))) return true;
    return draftIds.length > 0 && (primaryId || draftIds[0]) !== (savedPrimaryId || savedIds[0]);
  }, [draftIds, savedIds, primaryId, savedPrimaryId]);

  function toggleExam(examId, examName) {
    setSaved(false);
    setError(null);
    setDraft((prev) => {
      if (prev.some((d) => d.examId === examId)) {
        const next = prev.filter((d) => d.examId !== examId);
        if (primaryId === examId) setPrimaryId(next[0]?.examId || null);
        return next;
      }
      if (!primaryId && prev.length === 0) setPrimaryId(examId);
      return [...prev, { examId, examName: examName || "Exam" }];
    });
  }

  function discard() {
    setDraft(exams.map((e) => ({ examId: e.examId, examName: e.examName })));
    setPrimaryId(savedPrimaryId);
    setError(null);
    setSaved(false);
    setAdding(false);
  }

  async function handleSave() {
    if (saving || !isDirty) return;
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      const res = await save({ examIds: draftIds, primaryExamId: draftIds.length > 0 ? primaryId || draftIds[0] : null });
      setDraft((res.exams || []).map((e) => ({ examId: e.examId, examName: e.examName })));
      setPrimaryId(res.primaryExamId || null);
      setAdding(false);
      setSaved(true);
      clearTimeout(savedTimer.current);
      savedTimer.current = setTimeout(() => setSaved(false), 4000);
    } catch (err) {
      setError(err.message || "Couldn't update your exams. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  const effectivePrimary = primaryId || draft[0]?.examId || null;

  return (
    <div className="mx-auto max-w-lg px-4 py-6 pb-10 sm:px-6 sm:py-10">
      <button type="button" onClick={() => navigate(-1)} className="flex items-center gap-1.5 text-sm font-semibold text-ink-600 hover:text-ink-900">
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
        Back
      </button>

      <h1 className="mt-4 text-xl font-extrabold text-ink-900">My Exams</h1>
      <p className="mt-1 text-sm text-ink-600">
        SCORAM shows you PYPs, questions, tests, mock tests and groups for these exams only. To see another
        exam's content, add it here.
      </p>

      {!hasLoaded && (
        <div className="mt-6 space-y-2" aria-busy="true" aria-label="Loading My Exams">
          {[0, 1].map((i) => <div key={i} className="h-14 animate-pulse rounded-xl2 bg-primary-50" />)}
        </div>
      )}

      {saved && (
        <div role="status" className="mt-4 flex items-center gap-2 rounded-xl2 border border-mint-100 bg-mint-50 p-3 text-sm font-semibold text-mint-500">
          <CheckCircle2 className="h-4 w-4 shrink-0" strokeWidth={2.25} />
          My Exams updated.
        </div>
      )}

      {error && (
        <div role="alert" className="mt-4 flex items-center gap-2 rounded-xl2 border border-accent-100 bg-accent-50 p-3 text-sm text-accent-600">
          <AlertCircle className="h-4 w-4 shrink-0" strokeWidth={2.25} />
          {error}
        </div>
      )}

      {hasLoaded && (
        <div className="mt-6">
          <h2 className="text-xs font-bold uppercase tracking-wide text-ink-400">Currently selected ({draft.length})</h2>

          {draft.length === 0 ? (
            <p className="mt-2 rounded-xl2 border border-dashed border-primary-100 bg-primary-50/40 p-4 text-sm text-ink-600">
              No exams selected. Add an exam to personalize your SCORAM content.
            </p>
          ) : (
            <div className="mt-2 space-y-2">
              {draft.map((exam) => {
                const isPrimary = effectivePrimary === exam.examId;
                return (
                  <div key={exam.examId} className="flex items-center gap-3 rounded-xl2 border border-primary-100 bg-white p-3.5 shadow-card">
                    <span className="min-w-0 flex-1 truncate text-sm font-bold text-ink-900">{exam.examName}</span>
                    {draft.length > 1 && (
                      <button
                        type="button"
                        onClick={() => { setPrimaryId(exam.examId); setSaved(false); }}
                        disabled={isPrimary}
                        title={isPrimary ? "Primary exam" : "Set as primary"}
                        aria-label={isPrimary ? "Primary exam" : `Set ${exam.examName} as primary`}
                        className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full transition-colors ${
                          isPrimary ? "bg-amber-100 text-amber-500" : "bg-surface text-ink-400 hover:text-amber-500"
                        }`}
                      >
                        <Star className="h-4 w-4" strokeWidth={2.25} fill={isPrimary ? "currentColor" : "none"} />
                      </button>
                    )}
                    <button
                      type="button"
                      onClick={() => toggleExam(exam.examId, exam.examName)}
                      disabled={saving}
                      title="Remove"
                      aria-label={`Remove ${exam.examName}`}
                      className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-surface text-ink-400 transition-colors hover:bg-red-50 hover:text-red-500 disabled:opacity-40"
                    >
                      <X className="h-4 w-4" strokeWidth={2.25} />
                    </button>
                  </div>
                );
              })}
            </div>
          )}

          {draft.length === 0 && savedIds.length > 0 && (
            <p className="mt-3 flex items-start gap-2 rounded-xl2 border border-amber-100 bg-amber-50 p-3 text-xs text-amber-700">
              <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" strokeWidth={2.25} />
              Saving with no exams means PYP, questions, tests and mock tests stay empty until you choose exams again.
            </p>
          )}

          {!adding ? (
            <button
              type="button"
              onClick={() => setAdding(true)}
              className="mt-3 flex w-full items-center justify-center gap-1.5 rounded-xl2 border border-dashed border-primary-100 py-3 text-sm font-semibold text-primary-600 hover:border-primary-400 hover:bg-primary-50"
            >
              <Plus className="h-4 w-4" strokeWidth={2.5} />
              Add Exam
            </button>
          ) : (
            <div className="mt-3 rounded-xl2 border border-primary-100 bg-white p-3.5 shadow-card">
              <div className="mb-2 flex items-center justify-between">
                <h3 className="text-sm font-bold text-ink-900">Add Exam <span className="font-semibold text-ink-400">· Selected: {draft.length}</span></h3>
                <button type="button" onClick={() => setAdding(false)} aria-label="Close" className="flex h-8 w-8 items-center justify-center rounded-full text-ink-400 hover:bg-surface">
                  <X className="h-4 w-4" strokeWidth={2.25} />
                </button>
              </div>
              <OrganizationExamPicker selectedIds={draftIds} onToggle={toggleExam} autoFocusSearch />
              <button
                type="button"
                onClick={() => setAdding(false)}
                className="mt-3 w-full rounded-xl2 bg-primary-50 py-2 text-sm font-bold text-primary-600 hover:bg-primary-100"
              >
                Done
              </button>
            </div>
          )}
        </div>
      )}

      {hasLoaded && (
        <div className="mt-6 border-t border-primary-100 pt-4">
          <div className="flex items-center gap-2">
            {isDirty && (
              <button type="button" onClick={discard} disabled={saving} className="rounded-xl2 px-4 py-3 text-sm font-semibold text-ink-600 hover:bg-primary-50 disabled:opacity-50">
                Discard
              </button>
            )}
            <button
              type="button"
              onClick={handleSave}
              disabled={!isDirty || saving}
              className="flex flex-1 items-center justify-center gap-2 rounded-xl2 bg-primary-600 py-3 text-sm font-bold text-white transition-colors hover:bg-primary-700 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {saving && <Loader2 className="h-4 w-4 animate-spin" />}
              Update My Exams
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
