import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { AlertTriangle, ArrowRightLeft, GitMerge, Loader2, Pencil, Plus, Power, Trash2, X, Info } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  createManagedSubject, renameManagedSubject, setManagedSubjectActive, listManagedSubjects,
  previewSubjectMerge, mergeSubjects, previewSubjectReassign, reassignSubjectContent,
  previewSubjectDelete, deleteManagedSubject, getManagedSubject,
} from "../api/subjects";
import { Button, FormField, TextInput, Select, Alert, friendlyError } from "./AdminUI";

const n = (v) => (v ?? 0).toLocaleString();

// ---------------------------------------------------------------------------------------------
// Shared building blocks
// ---------------------------------------------------------------------------------------------

const TONES = {
  default: { ring: "bg-primary-50 text-primary-600" },
  warning: { ring: "bg-accent-50 text-accent-600" },
  danger: { ring: "bg-red-50 text-red-600" },
};

// Lightweight modal shell (there's no shared modal in the admin kit yet). Escape / backdrop click close
// it, but never while a request is in flight -- so an operation can't be "cancelled" mid-write.
export function Modal({ title, icon: Icon, tone = "default", onClose, busy = false, wide = false, children, footer }) {
  useEffect(() => {
    const onKey = (e) => { if (e.key === "Escape" && !busy) onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [busy, onClose]);

  return (
    <div
      className="fixed inset-0 z-50 flex items-end justify-center bg-ink-900/50 p-0 sm:items-center sm:p-4"
      onMouseDown={(e) => { if (e.target === e.currentTarget && !busy) onClose(); }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={`flex max-h-[92vh] w-full flex-col rounded-t-xl2 bg-white shadow-floating sm:rounded-xl2 ${wide ? "sm:max-w-2xl" : "sm:max-w-md"}`}
      >
        <div className="flex items-center gap-3 border-b border-primary-100 px-5 py-4">
          {Icon && (
            <span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full ${TONES[tone].ring}`}>
              <Icon className="h-5 w-5" strokeWidth={2.25} />
            </span>
          )}
          <h2 className="flex-1 text-base font-extrabold text-ink-900">{title}</h2>
          <button type="button" onClick={onClose} disabled={busy} aria-label="Close"
            className="rounded-full p-1.5 text-ink-400 hover:bg-primary-50 disabled:opacity-40">
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="flex-1 space-y-4 overflow-y-auto px-5 py-4">{children}</div>
        {footer && <div className="flex flex-wrap justify-end gap-2 border-t border-primary-100 px-5 py-3">{footer}</div>}
      </div>
    </div>
  );
}

function Row({ label, value, sub, strong }) {
  return (
    <div className={`flex items-baseline justify-between gap-3 py-1.5 text-sm ${strong ? "font-extrabold text-ink-900" : "text-ink-600"}`}>
      <span>{label}{sub && <span className="ml-1.5 text-xs font-normal text-ink-400">{sub}</span>}</span>
      <span className="tabular-nums font-semibold text-ink-900">{value}</span>
    </div>
  );
}

// Real numbers only (they come straight from the API). Two groups on purpose: what a merge/reassign
// REWRITES vs. content that only follows its questions and needs no change.
export function UsageGrid({ usage, showFollows = true }) {
  if (!usage) return null;
  return (
    <div className="space-y-3">
      <div className="rounded-xl2 border border-primary-100 px-4 py-2">
        <p className="pt-1 text-[11px] font-bold uppercase tracking-wide text-ink-400">Records that will be updated</p>
        <Row label="Question Bank (PYQ) questions" value={n(usage.questionBankQuestions)}
          sub={usage.inactiveQuestionBankQuestions > 0 ? `${n(usage.inactiveQuestionBankQuestions)} inactive` : undefined} />
        <Row label="PYP paper questions" value={n(usage.pypQuestions)} />
        <Row label="Topics" value={n(usage.topics)} />
        <Row label="Practice test templates" value={n(usage.practiceTemplates)} />
        <div className="border-t border-primary-100 pt-1"><Row label="Total affected records" value={n(usage.totalAffected)} strong /></div>
      </div>
      {showFollows && (
        <div className="rounded-xl2 bg-surface px-4 py-2">
          <p className="pt-1 text-[11px] font-bold uppercase tracking-wide text-ink-400">Follows automatically (not edited)</p>
          <Row label="Mock Test questions" value={n(usage.mockTestQuestions)} sub={usage.mockTests > 0 ? `in ${n(usage.mockTests)} test(s)` : undefined} />
          <Row label="Quiz questions" value={n(usage.quizQuestions)} sub={usage.quizzes > 0 ? `in ${n(usage.quizzes)} quiz(zes)` : undefined} />
          {usage.papers > 0 && <Row label="Papers containing this subject" value={n(usage.papers)} />}
          {usage.practiceAttempts > 0 && <Row label="Past practice attempts (history)" value={n(usage.practiceAttempts)} />}
        </div>
      )}
    </div>
  );
}

function Warning({ children, tone = "warning" }) {
  const cls = tone === "danger" ? "bg-red-50 text-red-600" : "bg-accent-50 text-accent-600";
  return (
    <div className={`flex items-start gap-2 rounded-xl2 p-3 text-xs font-medium ${cls}`}>
      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
      <div>{children}</div>
    </div>
  );
}

// Loads every subject (the list endpoint pages at 100) for the merge/reassign pickers.
function useAllSubjects() {
  const { token } = useAdminAuth();
  const [subjects, setSubjects] = useState(null);
  const [error, setError] = useState(null);
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        let page = 1;
        let all = [];
        for (;;) {
          const res = await listManagedSubjects(token, { page, pageSize: 100, sortBy: "name", sortDir: "asc" });
          all = all.concat(res.items);
          if (all.length >= res.total || res.items.length === 0) break;
          page += 1;
        }
        if (!cancelled) setSubjects(all);
      } catch (err) {
        if (!cancelled) setError(friendlyError(err));
      }
    })();
    return () => { cancelled = true; };
  }, [token]);
  return { subjects, error };
}

// ---------------------------------------------------------------------------------------------
// Add / rename
// ---------------------------------------------------------------------------------------------

export function SubjectFormDialog({ subject, onClose, onDone }) {
  const { token } = useAdminAuth();
  const isEdit = !!subject;
  const [name, setName] = useState(subject?.name ?? "");
  const [isActive, setIsActive] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [confirmUsage, setConfirmUsage] = useState(null); // set => showing the "used by N records" confirmation
  const submitting = useRef(false);

  const trimmed = name.trim().replace(/\s+/g, " ");
  const fieldError = trimmed.length === 0 ? "Subject name is required." : trimmed.length > 100 ? "Subject name can be at most 100 characters." : null;
  const unchanged = isEdit && trimmed === subject.name;

  const save = useCallback(async (confirmed) => {
    if (submitting.current) return; // prevent double-submit
    submitting.current = true;
    setBusy(true);
    setError(null);
    try {
      const res = isEdit
        ? await renameManagedSubject(token, subject.id, { name: trimmed, version: subject.version, confirm: confirmed })
        : await createManagedSubject(token, { name: trimmed, isActive });
      onDone(res.message);
    } catch (err) {
      const usage = err?.data?.data?.usage;
      if (err?.data?.code === "CONFIRMATION_REQUIRED" && usage) {
        setConfirmUsage(usage); // usage changed since the list loaded -- ask again with fresh numbers
      } else {
        setError(friendlyError(err));
        if (err?.data?.code === "CONCURRENT_MODIFICATION") onDone(null, { keepOpen: true }); // refresh list behind the dialog
      }
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }, [token, isEdit, subject, trimmed, isActive, onDone]);

  const onSubmit = () => {
    if (fieldError || unchanged) return;
    if (isEdit && subject.usage?.totalAffected > 0) setConfirmUsage(subject.usage);
    else save(false);
  };

  if (confirmUsage) {
    return (
      <Modal title="Rename subject?" icon={Pencil} tone="warning" onClose={onClose} busy={busy}
        footer={<>
          <Button variant="ghost" onClick={() => setConfirmUsage(null)} disabled={busy}>Cancel</Button>
          <Button onClick={() => save(true)} isLoading={busy}>Confirm Update</Button>
        </>}>
        <p className="text-sm text-ink-600">
          This subject is currently used by <b>{n(confirmUsage.totalAffected)} records</b>. Renaming it will update the
          subject name shown across SCORAM.
        </p>
        <div className="rounded-xl2 bg-surface px-4 py-3 text-sm">
          <span className="text-ink-400">{subject.name}</span>
          <span className="mx-2 text-ink-400">→</span>
          <b className="text-ink-900">{trimmed}</b>
        </div>
        <p className="text-xs text-ink-400">Existing content stays linked to the same subject — no duplicate subject is created.</p>
        {error && <Alert>{error}</Alert>}
      </Modal>
    );
  }

  return (
    <Modal title={isEdit ? "Edit subject" : "Add subject"} icon={isEdit ? Pencil : Plus} onClose={onClose} busy={busy}
      footer={<>
        <Button variant="ghost" onClick={onClose} disabled={busy}>Cancel</Button>
        <Button onClick={onSubmit} isLoading={busy} disabled={!!fieldError || unchanged}>{isEdit ? "Save changes" : "Add subject"}</Button>
      </>}>
      <FormField label="Subject name" hint="Names are compared ignoring capitalisation and extra spaces, so “reasoning” and “ Reasoning ” count as the same subject.">
        <TextInput value={name} onChange={(e) => setName(e.target.value)} maxLength={120} autoFocus
          onKeyDown={(e) => { if (e.key === "Enter") onSubmit(); }} placeholder="e.g. General Awareness" />
      </FormField>
      {name.length > 0 && fieldError && <p className="-mt-2 text-xs font-medium text-red-600">{fieldError}</p>}
      {!isEdit && (
        <FormField label="Status">
          <Select value={isActive ? "active" : "inactive"} onChange={(e) => setIsActive(e.target.value === "active")}>
            <option value="active">Active — available for new content</option>
            <option value="inactive">Inactive — hidden from new-content dropdowns</option>
          </Select>
        </FormField>
      )}
      {error && <Alert>{error}</Alert>}
    </Modal>
  );
}

// ---------------------------------------------------------------------------------------------
// Activate / deactivate
// ---------------------------------------------------------------------------------------------

export function DeactivateDialog({ subject, onClose, onDone }) {
  const { token } = useAdminAuth();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const submitting = useRef(false);

  const run = async () => {
    if (submitting.current) return;
    submitting.current = true;
    setBusy(true);
    setError(null);
    try {
      const res = await setManagedSubjectActive(token, subject.id, { isActive: false, version: subject.version });
      onDone(res.message);
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  };

  return (
    <Modal title="Deactivate subject?" icon={Power} tone="warning" onClose={onClose} busy={busy}
      footer={<>
        <Button variant="ghost" onClick={onClose} disabled={busy}>Cancel</Button>
        <Button onClick={run} isLoading={busy}>Deactivate</Button>
      </>}>
      <p className="text-sm text-ink-600">
        <b>{subject.name}</b> will no longer appear in new-content dropdowns or student-facing subject filters.
      </p>
      <p className="text-sm text-ink-600">
        Existing content ({n(subject.usage?.totalAffected)} records) keeps this subject and stays visible to admins.
        You can re-activate it at any time.
      </p>
      {error && <Alert>{error}</Alert>}
    </Modal>
  );
}

// ---------------------------------------------------------------------------------------------
// Merge / reassign (one dialog, two modes -- identical engine on the server)
// ---------------------------------------------------------------------------------------------

export function MoveDialog({ mode, initialSourceId, onClose, onDone }) {
  const { token } = useAdminAuth();
  const isMerge = mode === "merge";
  const { subjects, error: loadError } = useAllSubjects();

  const [sourceIds, setSourceIds] = useState(initialSourceId ? [initialSourceId] : []);
  const [targetId, setTargetId] = useState("");
  const [sourceSearch, setSourceSearch] = useState("");
  const [preview, setPreview] = useState(null);
  const [typed, setTyped] = useState("");
  const [ack, setAck] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const submitting = useRef(false);

  const byId = useMemo(() => new Map((subjects ?? []).map((s) => [s.id, s])), [subjects]);
  const targets = (subjects ?? []).filter((s) => s.isActive && !sourceIds.includes(s.id));
  const sourceChoices = (subjects ?? []).filter((s) => s.id !== targetId && s.name.toLowerCase().includes(sourceSearch.trim().toLowerCase()));
  const canPreview = sourceIds.length > 0 && !!targetId && (isMerge || sourceIds.length === 1);

  const toggleSource = (id) => {
    if (!isMerge) return setSourceIds([id]);
    setSourceIds((cur) => (cur.includes(id) ? cur.filter((x) => x !== id) : [...cur, id]));
  };

  const loadPreview = async () => {
    if (submitting.current) return;
    submitting.current = true;
    setBusy(true);
    setError(null);
    try {
      const res = isMerge
        ? await previewSubjectMerge(token, { sourceIds, targetId })
        : await previewSubjectReassign(token, { sourceId: sourceIds[0], targetId });
      setPreview(res);
      setTyped("");
      setAck(false);
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  };

  const execute = async () => {
    if (submitting.current) return;
    submitting.current = true;
    setBusy(true);
    setError(null);
    try {
      const expectedTotalAffected = preview.combined.totalAffected;
      const res = isMerge
        ? await mergeSubjects(token, { sourceIds, targetId, confirm: true, confirmName: typed, expectedTotalAffected })
        : await reassignSubjectContent(token, { sourceId: sourceIds[0], targetId, confirm: true, expectedTotalAffected });
      onDone(res.message);
    } catch (err) {
      setError(friendlyError(err));
      // Content changed since the preview: nothing ran; show the fresh numbers so they can re-confirm.
      if (err?.data?.code === "IMPACT_CHANGED") {
        submitting.current = false;
        setBusy(false);
        await loadPreview();
        setError(friendlyError(err));
        return;
      }
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  };

  const target = preview?.target;
  const typedOk = !isMerge || typed.trim().toLowerCase() === (target?.name ?? "").toLowerCase();
  const canExecute = !!preview && preview.canProceed && typedOk && (isMerge || ack);
  const title = isMerge ? "Merge subjects" : "Reassign content";
  const Icon = isMerge ? GitMerge : ArrowRightLeft;

  // ----- Step 2: impact preview + explicit confirmation -----
  if (preview) {
    return (
      <Modal title={title} icon={Icon} tone="warning" wide onClose={onClose} busy={busy}
        footer={<>
          <Button variant="ghost" onClick={() => { setPreview(null); setError(null); }} disabled={busy}>Back</Button>
          <Button variant="ghost" onClick={onClose} disabled={busy}>Cancel</Button>
          <Button onClick={execute} isLoading={busy} disabled={!canExecute}>
            {isMerge ? `Merge ${preview.sources.length} subject${preview.sources.length > 1 ? "s" : ""}` : "Reassign content"}
          </Button>
        </>}>
        <div className="text-sm text-ink-600">
          You are about to {isMerge ? "merge" : "move content from"}:
          <ul className="mt-1.5 flex flex-wrap gap-1.5">
            {preview.sources.map((s) => (
              <li key={s.id} className="rounded-full bg-primary-50 px-3 py-1 text-xs font-bold text-primary-600">{s.name}</li>
            ))}
          </ul>
          <p className="mt-2">{isMerge ? "into" : "to"}: <b className="text-ink-900">{target.name}</b></p>
        </div>

        <UsageGrid usage={preview.combined} />

        {preview.topicsToMove + preview.topicsToCombine > 0 && (
          <p className="text-xs text-ink-400">
            Topics: {n(preview.topicsToMove)} move under {target.name}
            {preview.topicsToCombine > 0 && `, ${n(preview.topicsToCombine)} merge into same-named existing topics`}.
          </p>
        )}

        {preview.blockers.map((b) => <Warning key={b} tone="danger">{b}</Warning>)}
        <Warning>
          All linked content will be reassigned to the target subject. This action may affect filters, reports and content categorization.
        </Warning>
        {preview.warnings.map((w) => (
          <p key={w} className="flex items-start gap-1.5 text-xs text-ink-400"><Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />{w}</p>
        ))}

        {isMerge ? (
          <FormField label={`To confirm, type the target subject's name: ${target.name}`}>
            <TextInput value={typed} onChange={(e) => setTyped(e.target.value)} placeholder={target.name} autoComplete="off" disabled={busy} />
          </FormField>
        ) : (
          <label className="flex items-start gap-2 text-sm text-ink-600">
            <input type="checkbox" className="mt-1" checked={ack} onChange={(e) => setAck(e.target.checked)} disabled={busy} />
            I understand this moves {n(preview.combined.totalAffected)} records to {target.name}.
          </label>
        )}
        {error && <Alert>{error}</Alert>}
      </Modal>
    );
  }

  // ----- Step 1: choose source(s) and target -----
  return (
    <Modal title={title} icon={Icon} wide onClose={onClose} busy={busy}
      footer={<>
        <Button variant="ghost" onClick={onClose} disabled={busy}>Cancel</Button>
        <Button onClick={loadPreview} isLoading={busy} disabled={!canPreview}>Preview impact</Button>
      </>}>
      <p className="text-sm text-ink-600">
        {isMerge
          ? "Pick the subject(s) to merge away, then the one they should be merged into. Nothing changes until you review the impact and confirm."
          : "Move all content from one subject to another. The source subject stays active. Nothing changes until you review the impact and confirm."}
      </p>
      {!subjects && !loadError && <p className="flex items-center gap-2 text-sm text-ink-400"><Loader2 className="h-4 w-4 animate-spin" /> Loading subjects…</p>}
      {loadError && <Alert>{loadError}</Alert>}
      {subjects && (
        <>
          <FormField label={isMerge ? "Source subject(s) — will be deactivated" : "Move content from"}>
            <div className="space-y-2">
              {isMerge && <TextInput value={sourceSearch} onChange={(e) => setSourceSearch(e.target.value)} placeholder="Search subjects…" />}
              <div className="max-h-44 space-y-0.5 overflow-y-auto rounded-xl2 border border-primary-100 p-1.5">
                {sourceChoices.map((s) => (
                  <label key={s.id} className="flex cursor-pointer items-center gap-2 rounded-lg px-2 py-1.5 text-sm hover:bg-primary-50">
                    <input type={isMerge ? "checkbox" : "radio"} name="src" checked={sourceIds.includes(s.id)} onChange={() => toggleSource(s.id)} />
                    <span className="flex-1 text-ink-900">{s.name}</span>
                    {!s.isActive && <span className="text-[11px] font-semibold text-ink-400">inactive</span>}
                    <span className="text-xs tabular-nums text-ink-400">{n(s.usage.totalAffected)}</span>
                  </label>
                ))}
                {sourceChoices.length === 0 && <p className="px-2 py-1.5 text-xs text-ink-400">No matching subjects.</p>}
              </div>
            </div>
          </FormField>
          <FormField label={isMerge ? "Target subject — keeps all the content" : "Move content to"} hint="Only active subjects can be a target.">
            <Select value={targetId} onChange={(e) => setTargetId(e.target.value)}>
              <option value="">Select target subject…</option>
              {targets.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </Select>
          </FormField>
          {sourceIds.length > 0 && (
            <p className="text-xs text-ink-400">Selected: {sourceIds.map((id) => byId.get(id)?.name).filter(Boolean).join(", ")}</p>
          )}
        </>
      )}
      {error && <Alert>{error}</Alert>}
    </Modal>
  );
}

// ---------------------------------------------------------------------------------------------
// Delete
// ---------------------------------------------------------------------------------------------

export function DeleteDialog({ subject, onClose, onDone, onReassign, onDeactivate }) {
  const { token } = useAdminAuth();
  const [preview, setPreview] = useState(null);
  const [typed, setTyped] = useState("");
  const [loadError, setLoadError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const submitting = useRef(false);

  useEffect(() => {
    let cancelled = false;
    previewSubjectDelete(token, subject.id)
      .then((res) => { if (!cancelled) setPreview(res); })
      .catch((err) => { if (!cancelled) setLoadError(friendlyError(err)); });
    return () => { cancelled = true; };
  }, [token, subject.id]);

  const run = async () => {
    if (submitting.current) return;
    submitting.current = true;
    setBusy(true);
    setError(null);
    try {
      const res = await deleteManagedSubject(token, subject.id, typed);
      onDone(res.message);
    } catch (err) {
      setError(friendlyError(err));
      // Became in-use since the preview (e.g. an import ran): refresh so the blocked view appears.
      if (err?.data?.code === "SUBJECT_IN_USE") previewSubjectDelete(token, subject.id).then(setPreview).catch(() => {});
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  };

  if (!preview) {
    return (
      <Modal title="Delete subject" icon={Trash2} tone="danger" onClose={onClose}>
        {loadError ? <Alert>{loadError}</Alert> : <p className="flex items-center gap-2 text-sm text-ink-400"><Loader2 className="h-4 w-4 animate-spin" /> Checking what uses this subject…</p>}
      </Modal>
    );
  }

  // Blocked: never a direct delete when content is linked -- offer the safe alternatives instead.
  if (!preview.canDelete) {
    return (
      <Modal title="Cannot delete this subject" icon={AlertTriangle} tone="danger" wide onClose={onClose}
        footer={<>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          {subject.isActive && <Button variant="secondary" onClick={() => onDeactivate(subject)}>Archive / deactivate</Button>}
          <Button onClick={() => onReassign(subject)}>Reassign content…</Button>
        </>}>
        <Warning tone="danger">Cannot delete this subject because it is currently used by existing content.</Warning>
        <p className="text-sm text-ink-600"><b>{subject.name}</b> is used by:</p>
        <ul className="list-disc space-y-0.5 pl-5 text-sm text-ink-600">
          {preview.blockers.map((b) => <li key={b}>{b}</li>)}
        </ul>
        <UsageGrid usage={preview.subject.usage} />
        <p className="text-xs text-ink-400">
          Reassign the content to another subject first, or archive (deactivate) this one to hide it from new content while keeping history intact.
        </p>
        {error && <Alert>{error}</Alert>}
      </Modal>
    );
  }

  return (
    <Modal title="Delete Subject?" icon={Trash2} tone="danger" onClose={onClose} busy={busy}
      footer={<>
        <Button variant="ghost" onClick={onClose} disabled={busy}>Cancel</Button>
        <Button variant="danger" onClick={run} isLoading={busy} disabled={typed.trim().toLowerCase() !== subject.name.toLowerCase()}>Delete Subject</Button>
      </>}>
      <Warning tone="danger">This subject has no linked content and will be permanently deleted.</Warning>
      <FormField label={`To confirm, type the subject's name: ${subject.name}`}>
        <TextInput value={typed} onChange={(e) => setTyped(e.target.value)} placeholder={subject.name} autoComplete="off" disabled={busy} />
      </FormField>
      {error && <Alert>{error}</Alert>}
    </Modal>
  );
}

// ---------------------------------------------------------------------------------------------
// Usage details
// ---------------------------------------------------------------------------------------------

export function UsageDialog({ subject, onClose }) {
  const { token } = useAdminAuth();
  const [detail, setDetail] = useState(null);
  const [error, setError] = useState(null);

  useEffect(() => {
    let cancelled = false;
    getManagedSubject(token, subject.id)
      .then((res) => { if (!cancelled) setDetail(res); })
      .catch((err) => { if (!cancelled) setError(friendlyError(err)); });
    return () => { cancelled = true; };
  }, [token, subject.id]);

  return (
    <Modal title={`${subject.name} — usage`} icon={Info} wide onClose={onClose}
      footer={<Button variant="ghost" onClick={onClose}>Close</Button>}>
      {error && <Alert>{error}</Alert>}
      {!detail && !error && <p className="flex items-center gap-2 text-sm text-ink-400"><Loader2 className="h-4 w-4 animate-spin" /> Loading…</p>}
      {detail && (
        <>
          <UsageGrid usage={detail.usage} />
          {detail.topicList.length > 0 && (
            <div className="rounded-xl2 border border-primary-100 px-4 py-2">
              <p className="pt-1 text-[11px] font-bold uppercase tracking-wide text-ink-400">Topics</p>
              {detail.topicList.map((t) => (
                <Row key={t.id} label={t.name} sub={t.isActive ? undefined : "inactive"} value={`${n(t.questionCount)} questions`} />
              ))}
            </div>
          )}
          <div className="flex flex-wrap gap-3 text-sm font-semibold">
            <Link to="/admin/question-bank" className="text-secondary-500 hover:underline">Open PYQs →</Link>
            <Link to="/admin/question-bank/subjects-topics" className="text-secondary-500 hover:underline">Manage topics →</Link>
            <Link to="/admin/papers" className="text-secondary-500 hover:underline">All PYP papers →</Link>
          </div>
        </>
      )}
    </Modal>
  );
}
