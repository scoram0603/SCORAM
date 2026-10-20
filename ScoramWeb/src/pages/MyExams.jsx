import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, Loader2, Plus, Star, AlertCircle, CheckCircle2, RefreshCw, X } from "lucide-react";
import OrganizationExamPicker from "../components/exams/OrganizationExamPicker";
import { useMyExams } from "../context/MyExamsContext";

// "MY EXAMS" management screen (Profile -> My Exams). Edits are collected in a local DRAFT and
// saved together with "Update My Exams" (one PUT of the whole selection), so the student can
// add/remove several exams, change their mind, and discard -- nothing is saved half-way.
//
// Removing every exam is allowed: My Exams then goes empty and exam content is replaced by
// "Choose My Exams" prompts (never "all exams"). A short warning explains that before saving.
//
// SAFETY: if the saved list could not be loaded (loadError) the editor is NOT shown -- an empty draft
// built from a failed load would look like "no exams" and one tap on Update would wipe the student's real
// selection. They get a Retry instead.
const focusRing = "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary-400 focus-visible:ring-offset-1";

export default function MyExams() {
  const navigate = useNavigate();
  const { exams, hasLoaded, loadError, refresh, save } = useMyExams();

  // draft: [{ examId, examName }] in display order; primaryId: examId | null
  const [draft, setDraft] = useState([]);
  const [primaryId, setPrimaryId] = useState(null);
  const [initialised, setInitialised] = useState(false);
  const [adding, setAdding] = useState(false);
  const [saving, setSaving] = useState(false);
  const [retrying, setRetrying] = useState(false);
  const [error, setError] = useState(null);
  const [saved, setSaved] = useState(false);
  const savedTimer = useRef(null);

  // Seed the draft from the saved list once it has loaded successfully.
  useEffect(() => {
    if (!hasLoaded || loadError || initialised) return;
    setDraft(exams.map((e) => ({ examId: e.examId, examName: e.examName })));
    setPrimaryId(exams.find((e) => e.isPrimary)?.examId || null);
    setInitialised(true);
  }, [hasLoaded, loadError, initialised, exams]);

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
    if (draft.some((d) => d.examId === examId)) {
      const next = draft.filter((d) => d.examId !== examId);
      setDraft(next);
      if (primaryId === examId) setPrimaryId(next[0]?.examId || null);
      return;
    }
    if (!primaryId && draft.length === 0) setPrimaryId(examId);
    setDraft([...draft, { examId, examName: examName || "Exam" }]);
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

  async function handleRetry() {
    setRetrying(true);
    try {
      await refresh();
    } catch {
      /* the context keeps loadError set, so the retry panel stays visible */
    } finally {
      setRetrying(false);
    }
  }

  const effectivePrimary = primaryId || draft[0]?.examId || null;
  const ready = hasLoaded && !loadError;

  return (
    <div className="mx-auto w-full max-w-3xl px-4 py-6 pb-12 sm:px-6 sm:py-10">
      <button
        type="button"
        onClick={() => navigate(-1)}
        className={`-ml-1 flex items-center gap-1.5 rounded-lg px-1 py-1 text-sm font-semibold text-ink-600 hover:text-ink-900 ${focusRing}`}
      >
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
        Back
      </button>

      <header className="mt-4">
        <h1 className="text-2xl font-extrabold tracking-tight text-ink-900">My Exams</h1>
        <p className="mt-1.5 max-w-2xl text-sm leading-relaxed text-ink-600">
          SCORAM shows you PYPs, questions, tests, mock tests and groups for these exams only. To see another
          exam&apos;s content, add it here.
        </p>
      </header>

      {!hasLoaded && (
        <div className="mt-6 grid gap-2 sm:grid-cols-2" aria-busy="true" aria-label="Loading My Exams">
          {[0, 1, 2, 3].map((i) => <div key={i} className="h-16 animate-pulse rounded-xl2 bg-primary-50" />)}
        </div>
      )}

      {hasLoaded && loadError && (
        <div role="alert" className="mt-6 flex flex-wrap items-center gap-3 rounded-xl2 border border-accent-100 bg-accent-50 p-4 text-sm text-accent-600">
          <AlertCircle className="h-5 w-5 shrink-0" strokeWidth={2.25} />
          <span className="min-w-0 flex-1">
            We couldn&apos;t load your exams, so nothing has been changed. Check your connection and try again.
          </span>
          <button
            type="button"
            onClick={handleRetry}
            disabled={retrying}
            className={`flex items-center gap-1.5 rounded-lg bg-white px-3 py-2 text-xs font-bold text-accent-600 hover:bg-accent-100 disabled:opacity-60 ${focusRing}`}
          >
            {retrying ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <RefreshCw className="h-3.5 w-3.5" strokeWidth={2.5} />}
            Retry
          </button>
        </div>
      )}

      {saved && (
        <div role="status" className="mt-5 flex items-center gap-2 rounded-xl2 border border-mint-100 bg-mint-50 p-3 text-sm font-semibold text-mint-500">
          <CheckCircle2 className="h-4 w-4 shrink-0" strokeWidth={2.25} />
          My Exams updated.
        </div>
      )}

      {error && (
        <div role="alert" className="mt-5 flex items-start gap-2 rounded-xl2 border border-accent-100 bg-accent-50 p-3 text-sm text-accent-600">
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
          <span className="min-w-0 flex-1 break-words">{error}</span>
        </div>
      )}

      {ready && (
        <>
          <section className="mt-7" aria-labelledby="my-exams-selected">
            <div className="flex items-center justify-between gap-3">
              <h2 id="my-exams-selected" className="text-xs font-bold uppercase tracking-wide text-ink-400">
                Currently selected
                <span className="ml-2 rounded-full bg-primary-50 px-2 py-0.5 text-[11px] normal-case tracking-normal text-primary-600">
                  {draft.length}
                </span>
              </h2>
              {isDirty && <span className="text-xs font-semibold text-amber-600">Unsaved changes</span>}
            </div>

            {draft.length === 0 ? (
              <p className="mt-3 rounded-xl2 border border-dashed border-primary-100 bg-primary-50/40 p-5 text-center text-sm text-ink-600">
                No exams selected. Add an exam to personalize your SCORAM content.
              </p>
            ) : (
              <ul className="mt-3 grid gap-2 sm:grid-cols-2">
                {draft.map((exam) => {
                  const isPrimary = effectivePrimary === exam.examId;
                  return (
                    <li
                      key={exam.examId}
                      className={`flex items-center gap-2 rounded-xl2 border bg-white p-3 shadow-card ${isPrimary && draft.length > 1 ? "border-amber-100" : "border-primary-100"}`}
                    >
                      <div className="min-w-0 flex-1">
                        <p className="break-words text-sm font-bold leading-snug text-ink-900">{exam.examName}</p>
                        {isPrimary && draft.length > 1 && (
                          <p className="mt-0.5 text-[11px] font-semibold text-amber-600">Primary exam</p>
                        )}
                      </div>
                      {draft.length > 1 && (
                        <button
                          type="button"
                          onClick={() => { setPrimaryId(exam.examId); setSaved(false); }}
                          disabled={isPrimary}
                          title={isPrimary ? "Primary exam" : "Set as primary"}
                          aria-label={isPrimary ? `${exam.examName} is your primary exam` : `Set ${exam.examName} as primary`}
                          aria-pressed={isPrimary}
                          className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full transition-colors ${focusRing} ${
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
                        className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-surface text-ink-400 transition-colors hover:bg-red-50 hover:text-red-500 disabled:opacity-40 ${focusRing}`}
                      >
                        <X className="h-4 w-4" strokeWidth={2.25} />
                      </button>
                    </li>
                  );
                })}
              </ul>
            )}

            {draft.length === 0 && savedIds.length > 0 && (
              <p className="mt-3 flex items-start gap-2 rounded-xl2 border border-amber-100 bg-amber-50 p-3 text-xs text-amber-700">
                <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" strokeWidth={2.25} />
                Saving with no exams means PYP, questions, tests and mock tests stay empty until you choose exams again.
              </p>
            )}
          </section>

          <section className="mt-5" aria-label="Add exams">
            {!adding ? (
              <button
                type="button"
                onClick={() => setAdding(true)}
                className={`flex w-full items-center justify-center gap-1.5 rounded-xl2 border border-dashed border-primary-100 py-3.5 text-sm font-semibold text-primary-600 hover:border-primary-400 hover:bg-primary-50 ${focusRing}`}
              >
                <Plus className="h-4 w-4" strokeWidth={2.5} />
                Add Exam
              </button>
            ) : (
              <div className="rounded-xl2 border border-primary-100 bg-white p-3.5 shadow-card sm:p-4">
                <div className="mb-3 flex items-center justify-between gap-2">
                  <h3 className="text-sm font-bold text-ink-900">
                    Add Exam <span className="font-semibold text-ink-400">· Selected: {draft.length}</span>
                  </h3>
                  <button
                    type="button"
                    onClick={() => setAdding(false)}
                    aria-label="Close add exam panel"
                    className={`flex h-10 w-10 items-center justify-center rounded-full text-ink-400 hover:bg-surface ${focusRing}`}
                  >
                    <X className="h-4 w-4" strokeWidth={2.25} />
                  </button>
                </div>
                <OrganizationExamPicker selectedIds={draftIds} onToggle={toggleExam} autoFocusSearch />
                <button
                  type="button"
                  onClick={() => setAdding(false)}
                  className={`mt-3 w-full rounded-xl2 bg-primary-50 py-2.5 text-sm font-bold text-primary-600 hover:bg-primary-100 ${focusRing}`}
                >
                  Done
                </button>
              </div>
            )}
          </section>

          <div className="mt-7 flex flex-col-reverse gap-2 border-t border-primary-100 pt-5 sm:flex-row sm:justify-end">
            {isDirty && (
              <button
                type="button"
                onClick={discard}
                disabled={saving}
                className={`rounded-xl2 px-5 py-3 text-sm font-semibold text-ink-600 hover:bg-primary-50 disabled:opacity-50 ${focusRing}`}
              >
                Discard
              </button>
            )}
            <button
              type="button"
              onClick={handleSave}
              disabled={!isDirty || saving}
              className={`flex items-center justify-center gap-2 rounded-xl2 bg-primary-600 px-6 py-3 text-sm font-bold text-white transition-colors hover:bg-primary-700 disabled:cursor-not-allowed disabled:opacity-50 sm:min-w-[12rem] ${focusRing}`}
            >
              {saving && <Loader2 className="h-4 w-4 animate-spin" />}
              Update My Exams
            </button>
          </div>
        </>
      )}
    </div>
  );
}
