import { useEffect, useState } from "react";
import { AlertTriangle, Archive, GitMerge, Info, Layers, Loader2, Pencil, Plus, Trash2, Wand2 } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  createExamSubject, renameExamSubject, createExamTopic, renameExamTopic,
  previewExamSubjectDelete, deleteExamSubject, previewExamTopicDelete, deleteExamTopic,
  setExamSubjectActive, setExamTopicActive, getExamSubjectUsage, getExamTopicUsage,
  previewInitialize, initializeExam, listExamTopics, mapExamQuestions, getQuestionClassifications,
  previewMergeExamSubject, mergeExamSubject, previewMergeExamTopic, mergeExamTopic,
} from "../api/examTaxonomy";
import { Button, FormField, TextInput, Select, Alert, friendlyError } from "./AdminUI";
import { Modal } from "./SubjectManagementDialogs";

const n = (v) => (v ?? 0).toLocaleString();

function Row({ label, value, strong }) {
  return (
    <div className={`flex items-baseline justify-between gap-3 py-1.5 text-sm ${strong ? "font-extrabold text-ink-900" : "text-ink-600"}`}>
      <span>{label}</span>
      <span className="tabular-nums font-semibold text-ink-900">{value}</span>
    </div>
  );
}

// What an exam subject/topic is used by. Real numbers from the API only.
export function UsageList({ usage, kind }) {
  if (!usage) return null;
  return (
    <div className="space-y-3">
      <div className="rounded-xl2 border border-primary-100 px-4 py-2">
        <p className="pt-1 text-[11px] font-bold uppercase tracking-wide text-ink-400">In this exam</p>
        <Row label="Questions classified here" value={n(usage.questions)} strong />
        {kind === "subject" && <Row label="Topics" value={`${n(usage.topics)}${usage.topics ? ` (${n(usage.activeTopics)} active)` : ""}`} />}
      </div>
      <div className="rounded-xl2 border border-primary-100 px-4 py-2">
        <p className="pt-1 text-[11px] font-bold uppercase tracking-wide text-ink-400">Content that contains those questions (never modified)</p>
        <Row label="Papers of this exam" value={n(usage.papers)} />
        <Row label="Mock tests of this exam" value={n(usage.mockTests)} />
        <Row label="Practice tests of this exam" value={n(usage.practiceTests)} />
        <Row label="Quizzes (not exam-specific)" value={n(usage.quizzes)} />
        <Row label="Recorded student answers (history)" value={n(usage.studentAnswers)} />
      </div>
    </div>
  );
}

// ------------------------------------------------------------------------------------------------
// Create / rename (subject or topic)
// ------------------------------------------------------------------------------------------------
export function NameDialog({ kind, mode, examName, parentName, initial = "", onSubmit, onClose, onMerge }) {
  const [name, setName] = useState(initial);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [clash, setClash] = useState(null); // { existingId, existingName } when the new name already exists in this exam
  const isRename = mode === "rename";
  const title = `${isRename ? "Rename" : "Add"} ${kind}`;

  const submit = async (e) => {
    e?.preventDefault();
    if (busy) return;
    setBusy(true);
    setError(null);
    setClash(null);
    try {
      await onSubmit(name);
    } catch (err) {
      setError(friendlyError(err));
      // Rename collided with an existing active name in this exam: offer to merge into it instead of a dead end.
      const code = err?.data?.code;
      const payload = err?.data?.data;
      if (isRename && onMerge && (code === "DUPLICATE_SUBJECT" || code === "DUPLICATE_TOPIC") && payload?.existingId) setClash(payload);
      setBusy(false);
    }
  };

  return (
    <Modal title={title} icon={isRename ? Pencil : Plus} onClose={onClose} busy={busy}
      footer={<>
        <Button variant="ghost" onClick={onClose} disabled={busy}>Cancel</Button>
        {clash && <Button variant="secondary" onClick={() => onMerge(clash)} disabled={busy}><GitMerge className="h-4 w-4" /> Merge into “{clash.existingName}”</Button>}
        <Button onClick={submit} isLoading={busy} disabled={!name.trim() || (isRename && name.trim() === initial)}>{isRename ? "Rename" : "Add"}</Button>
      </>}>
      <form onSubmit={submit} className="space-y-3">
        <div className="flex gap-2 rounded-xl2 bg-primary-50 p-3 text-xs text-primary-600">
          <Info className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            {isRename
              ? <>This changes the name in <b>{examName}</b> only. Other exams, the global subject list, questions, papers, tests and results are not affected.</>
              : <>This {kind} is created for <b>{examName}</b> only{parentName ? <> (under <b>{parentName}</b>)</> : null}.</>}
          </span>
        </div>
        <FormField label={`${kind === "subject" ? "Subject" : "Topic"} name`}>
          <TextInput autoFocus value={name} onChange={(e) => setName(e.target.value)} maxLength={kind === "subject" ? 100 : 150} placeholder={kind === "subject" ? "e.g. General Awareness" : "e.g. Indian History"} />
        </FormField>
        {error && <Alert>{error}</Alert>}
        {clash && (
          <p className="rounded-xl2 bg-primary-50 p-3 text-xs text-primary-600">
            “{clash.existingName}” already exists in <b>{examName}</b>. You can <b>merge</b> “{initial}” into it: its questions{kind === "subject" ? " and topics" : ""} move to “{clash.existingName}” and “{initial}” is removed from this exam. You'll see exactly what changes before confirming.
          </p>
        )}
      </form>
    </Modal>
  );
}

// ------------------------------------------------------------------------------------------------
// Merge (source -> existing target, same exam): preview, then explicit confirmation
// ------------------------------------------------------------------------------------------------
export function MergeDialog({ kind, examId, examName, source, targetId, onClose, onDone }) {
  const { token } = useAdminAuth();
  const [preview, setPreview] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const loadPreview = async () => {
    const fn = kind === "subject" ? previewMergeExamSubject : previewMergeExamTopic;
    setPreview(await fn(token, examId, source.id, targetId));
  };
  useEffect(() => {
    let alive = true;
    (kind === "subject" ? previewMergeExamSubject : previewMergeExamTopic)(token, examId, source.id, targetId)
      .then((p) => alive && setPreview(p)).catch((e) => alive && setError(friendlyError(e)));
    return () => { alive = false; };
  }, [token, kind, examId, source.id, targetId]);

  const run = async () => {
    if (busy || !preview) return;
    setBusy(true);
    setError(null);
    try {
      const fn = kind === "subject" ? mergeExamSubject : mergeExamTopic;
      const res = await fn(token, examId, source.id, { targetId, confirm: true, expectedQuestions: preview.questions });
      onDone(res.message);
    } catch (err) {
      setError(friendlyError(err));
      if (err?.data?.code === "IMPACT_CHANGED") { try { await loadPreview(); } catch { /* keep old */ } }
      setBusy(false);
    }
  };

  return (
    <Modal title={`Merge ${kind} in ${examName}`} icon={GitMerge} onClose={onClose} busy={busy} wide
      footer={<>
        <Button variant="ghost" onClick={onClose} disabled={busy}>Cancel</Button>
        <Button onClick={run} isLoading={busy} disabled={!preview}><GitMerge className="h-4 w-4" /> Merge</Button>
      </>}>
      {!preview && !error && <p className="flex items-center gap-2 text-sm text-ink-400"><Loader2 className="h-4 w-4 animate-spin" /> Checking what will move…</p>}
      {preview && (
        <>
          <p className="text-sm text-ink-600">{preview.message}</p>
          <div className="rounded-xl2 border border-primary-100 px-4 py-2">
            <Row label={`“${preview.sourceName}” (removed after merge)`} value="→" />
            <Row label={`Into “${preview.targetName}” (kept)`} value="" />
            <Row label="Questions that move" value={n(preview.questions)} strong />
            {kind === "subject" && <Row label="Topics folded into same-named topics" value={n(preview.topicsMerged.length)} />}
            {kind === "subject" && <Row label="Topics moved as they are" value={n(preview.topicsMoved.length)} />}
          </div>
          {preview.topicsMerged.length > 0 && <p className="text-xs text-ink-400">Folded: {preview.topicsMerged.join(", ")}</p>}
          {preview.topicsMoved.length > 0 && <p className="text-xs text-ink-400">Moved: {preview.topicsMoved.join(", ")}</p>}
          <p className="rounded-xl2 bg-primary-50 p-3 text-xs text-primary-600">
            Only <b>{examName}</b> changes. Questions themselves, papers, tests, attempts, results, the global subject list and other exams are not affected. The whole merge is one all-or-nothing step.
          </p>
        </>
      )}
      {error && <Alert>{error}</Alert>}
    </Modal>
  );
}

export function useTaxonomyActions() {
  const { token } = useAdminAuth();
  return {
    createSubject: (examId, name) => createExamSubject(token, examId, { name }),
    renameSubject: (examId, s, name) => renameExamSubject(token, examId, s.id, { name, version: s.version }),
    createTopic: (examId, subjectId, name) => createExamTopic(token, examId, subjectId, { name }),
    renameTopic: (examId, t, name) => renameExamTopic(token, examId, t.id, { name, version: t.version }),
    setSubjectActive: (examId, s, isActive) => setExamSubjectActive(token, examId, s.id, { isActive, version: s.version }),
    setTopicActive: (examId, t, isActive) => setExamTopicActive(token, examId, t.id, { isActive, version: t.version }),
  };
}

// ------------------------------------------------------------------------------------------------
// Usage
// ------------------------------------------------------------------------------------------------
export function UsageDialog({ kind, examId, examName, item, onClose }) {
  const { token } = useAdminAuth();
  const [usage, setUsage] = useState(null);
  const [error, setError] = useState(null);
  useEffect(() => {
    let alive = true;
    (kind === "subject" ? getExamSubjectUsage : getExamTopicUsage)(token, examId, item.id)
      .then((u) => alive && setUsage(u)).catch((e) => alive && setError(friendlyError(e)));
    return () => { alive = false; };
  }, [token, kind, examId, item.id]);
  return (
    <Modal title={`${item.name} — usage in ${examName}`} icon={Layers} onClose={onClose} wide
      footer={<Button variant="secondary" onClick={onClose}>Close</Button>}>
      {error && <Alert>{error}</Alert>}
      {!usage && !error && <p className="flex items-center gap-2 text-sm text-ink-400"><Loader2 className="h-4 w-4 animate-spin" /> Checking what uses this…</p>}
      <UsageList usage={usage} kind={kind} />
    </Modal>
  );
}

// ------------------------------------------------------------------------------------------------
// Delete: preview -> archive | unassign-then-delete | plain delete (only when unused)
// ------------------------------------------------------------------------------------------------
export function DeleteDialog({ kind, examId, examName, item, onClose, onDone }) {
  const { token } = useAdminAuth();
  const [preview, setPreview] = useState(null);
  const [choice, setChoice] = useState(null); // "archive" | "unassign" | "delete"
  const [typed, setTyped] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  useEffect(() => {
    let alive = true;
    (kind === "subject" ? previewExamSubjectDelete : previewExamTopicDelete)(token, examId, item.id)
      .then((p) => { if (alive) { setPreview(p); setChoice(p.canDelete ? "delete" : p.canArchive ? "archive" : "unassign"); } })
      .catch((e) => alive && setError(friendlyError(e)));
    return () => { alive = false; };
  }, [token, kind, examId, item.id]);

  const needsName = choice === "delete" || choice === "unassign";
  const nameOk = !needsName || typed.trim().toLowerCase() === item.name.trim().toLowerCase();

  const run = async () => {
    if (busy || !preview) return;
    setBusy(true);
    setError(null);
    try {
      if (choice === "archive") {
        const fn = kind === "subject" ? setExamSubjectActive : setExamTopicActive;
        const res = await fn(token, examId, item.id, { isActive: false, version: item.version });
        onDone(res.message);
        return;
      }
      const fn = kind === "subject" ? deleteExamSubject : deleteExamTopic;
      const res = await fn(token, examId, item.id, {
        mode: choice === "unassign" ? "unassign" : "delete",
        confirmName: typed,
        expectedQuestions: choice === "unassign" ? preview.usage.questions : undefined,
      });
      onDone(res.message);
    } catch (err) {
      // IMPACT_CHANGED / *_IN_USE: refresh the preview so the admin decides again with current numbers.
      setError(friendlyError(err));
      if (err?.data?.code === "IMPACT_CHANGED" || /_IN_USE$/.test(err?.data?.code || "")) {
        try { setPreview(await (kind === "subject" ? previewExamSubjectDelete : previewExamTopicDelete)(token, examId, item.id)); } catch { /* keep old */ }
      }
      setBusy(false);
    }
  };

  const label = choice === "archive" ? "Archive" : choice === "unassign" ? "Unassign & delete" : "Delete";

  return (
    <Modal title={`Delete ${kind} “${item.name}” from ${examName}`} icon={AlertTriangle} tone="danger" onClose={onClose} busy={busy} wide
      footer={<>
        <Button variant="ghost" onClick={onClose} disabled={busy}>Cancel</Button>
        <Button variant={choice === "archive" ? "secondary" : "danger"} onClick={run} isLoading={busy} disabled={!preview || !choice || !nameOk}>
          {choice === "archive" ? <Archive className="h-4 w-4" /> : <Trash2 className="h-4 w-4" />} {label}
        </Button>
      </>}>
      {!preview && !error && <p className="flex items-center gap-2 text-sm text-ink-400"><Loader2 className="h-4 w-4 animate-spin" /> Checking what depends on this…</p>}
      {preview && (
        <>
          <p className="text-sm text-ink-600">{preview.message} <b>This only affects {examName}</b> — the global subject list and every other exam stay as they are.</p>
          <UsageList usage={preview.usage} kind={kind} />
          {preview.blockers.length > 0 && (
            <ul className="list-disc space-y-0.5 rounded-xl2 bg-accent-50 py-3 pl-8 pr-4 text-xs text-accent-600">
              {preview.blockers.map((b) => <li key={b}>{b}</li>)}
            </ul>
          )}
          <fieldset className="space-y-2">
            <legend className="mb-1 text-xs font-bold uppercase tracking-wide text-ink-400">What do you want to do?</legend>
            {preview.canArchive && (
              <label className="flex cursor-pointer gap-2 rounded-xl2 border border-primary-100 p-3 text-sm has-[:checked]:border-primary-600">
                <input type="radio" name="choice" checked={choice === "archive"} onChange={() => setChoice("archive")} className="mt-1" />
                <span><b>Archive (recommended)</b><br /><span className="text-xs text-ink-400">Hide it from filters and pickers. Every question stays mapped, and you can restore it any time.</span></span>
              </label>
            )}
            {preview.canUnassign && (
              <label className="flex cursor-pointer gap-2 rounded-xl2 border border-primary-100 p-3 text-sm has-[:checked]:border-red-300">
                <input type="radio" name="choice" checked={choice === "unassign"} onChange={() => setChoice("unassign")} className="mt-1" />
                <span><b>Unassign its {n(preview.usage.questions)} question(s), then delete</b><br />
                  <span className="text-xs text-ink-400">{kind === "subject" ? "The subject and its topics are removed; its questions become “Unassigned” in this exam" : "The topic is removed; its questions keep their subject but have no topic in this exam"}. The questions themselves, papers, tests and results are never deleted.</span></span>
              </label>
            )}
            {preview.canDelete && (
              <label className="flex cursor-pointer gap-2 rounded-xl2 border border-primary-100 p-3 text-sm has-[:checked]:border-red-300">
                <input type="radio" name="choice" checked={choice === "delete"} onChange={() => setChoice("delete")} className="mt-1" />
                <span><b>Delete</b><br /><span className="text-xs text-ink-400">Nothing depends on it, so it can simply be removed.</span></span>
              </label>
            )}
          </fieldset>
          {needsName && (
            <FormField label={`Type the ${kind} name to confirm: ${item.name}`}>
              <TextInput value={typed} onChange={(e) => setTyped(e.target.value)} autoComplete="off" />
            </FormField>
          )}
        </>
      )}
      {error && <Alert>{error}</Alert>}
    </Modal>
  );
}

// ------------------------------------------------------------------------------------------------
// Initialise an exam from the current (global) classification
// ------------------------------------------------------------------------------------------------
export function InitDialog({ examId, examName, onClose, onDone }) {
  const { token } = useAdminAuth();
  const [preview, setPreview] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  useEffect(() => {
    let alive = true;
    previewInitialize(token, examId).then((p) => alive && setPreview(p)).catch((e) => alive && setError(friendlyError(e)));
    return () => { alive = false; };
  }, [token, examId]);

  const run = async () => {
    setBusy(true); setError(null);
    try {
      const res = await initializeExam(token, examId, { confirm: true, expectedQuestionsToClassify: preview.questionsToClassify });
      onDone(res.message);
    } catch (err) {
      setError(friendlyError(err));
      try { setPreview(await previewInitialize(token, examId)); } catch { /* keep */ }
      setBusy(false);
    }
  };

  return (
    <Modal title={`Initialise ${examName}`} icon={Wand2} onClose={onClose} busy={busy} wide
      footer={<>
        <Button variant="ghost" onClick={onClose} disabled={busy}>Cancel</Button>
        <Button onClick={run} isLoading={busy} disabled={!preview?.canRun}>Create subject &amp; topic list</Button>
      </>}>
      {!preview && !error && <p className="flex items-center gap-2 text-sm text-ink-400"><Loader2 className="h-4 w-4 animate-spin" /> Reading the current classification…</p>}
      {preview && (
        <>
          <div className="flex gap-2 rounded-xl2 bg-primary-50 p-3 text-xs text-primary-600">
            <Info className="mt-0.5 h-4 w-4 shrink-0" />
            <span>
              This copies the <b>current</b> subject/topic of each question of {examName} into this exam's own list, so you can rename, add or remove them for this exam
              without touching any other exam. No question, paper, test or result is changed, and running it again is safe.
            </span>
          </div>
          <div className="rounded-xl2 border border-primary-100 px-4 py-2">
            <Row label="Questions in this exam" value={n(preview.questionsInExam)} />
            <Row label="Questions to classify" value={n(preview.questionsToClassify)} strong />
            <Row label="Subjects to create" value={n(preview.subjectsToCreate)} />
            <Row label="Topics to create" value={n(preview.topicsToCreate)} />
          </div>
          {preview.subjects.length > 0 && (
            <div className="max-h-56 overflow-y-auto rounded-xl2 border border-primary-100">
              <table className="w-full text-left text-xs">
                <thead className="bg-surface text-[11px] font-bold uppercase text-ink-400"><tr><th className="px-3 py-2">Subject</th><th className="px-3 py-2 text-right">Questions</th><th className="px-3 py-2">Topics</th></tr></thead>
                <tbody className="divide-y divide-primary-50">
                  {preview.subjects.map((s) => (
                    <tr key={s.name}><td className="px-3 py-2 font-semibold text-ink-900">{s.name}{s.exists && <span className="ml-1 font-normal text-ink-400">(exists)</span>}</td><td className="px-3 py-2 text-right tabular-nums">{n(s.questions)}</td><td className="px-3 py-2 text-ink-600">{s.topics.join(", ")}</td></tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {preview.conflicts.length > 0 && (
            <div className="space-y-1 rounded-xl2 bg-red-50 p-3 text-xs text-red-600">
              <p className="font-bold">Resolve these name clashes first (nothing is renamed automatically):</p>
              <ul className="list-disc pl-5">{preview.conflicts.map((c) => <li key={c}>{c}</li>)}</ul>
            </div>
          )}
          {preview.questionsToClassify === 0 && preview.conflicts.length === 0 && <Alert type="success">Every question of this exam is already classified.</Alert>}
        </>
      )}
      {error && <Alert>{error}</Alert>}
    </Modal>
  );
}

// ------------------------------------------------------------------------------------------------
// Map the selected questions to an exam subject / topic
// ------------------------------------------------------------------------------------------------
export function MapDialog({ examId, examName, subjects, defaultSubjectId, defaultTopicId, questionIds, onClose, onDone }) {
  const { token } = useAdminAuth();
  const active = subjects.filter((s) => s.isActive);
  const [subjectId, setSubjectId] = useState(defaultSubjectId && active.some((s) => s.id === defaultSubjectId) ? defaultSubjectId : active[0]?.id || "");
  const [topics, setTopics] = useState([]);
  const [topicId, setTopicId] = useState(defaultTopicId || "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  useEffect(() => {
    if (!subjectId) { setTopics([]); return undefined; }
    let alive = true;
    listExamTopics(token, examId, subjectId, { status: "active" })
      .then((r) => { if (alive) { setTopics(r.topics); setTopicId((cur) => (r.topics.some((t) => t.id === cur) ? cur : "")); } })
      .catch((e) => alive && setError(friendlyError(e)));
    return () => { alive = false; };
  }, [token, examId, subjectId]);

  const run = async () => {
    setBusy(true); setError(null);
    try {
      const res = await mapExamQuestions(token, examId, { questionIds, examSubjectId: subjectId, examTopicId: topicId || null });
      onDone(res.message);
    } catch (err) { setError(friendlyError(err)); setBusy(false); }
  };

  return (
    <Modal title={`Map ${n(questionIds.length)} question(s) in ${examName}`} icon={Layers} onClose={onClose} busy={busy}
      footer={<>
        <Button variant="ghost" onClick={onClose} disabled={busy}>Cancel</Button>
        <Button onClick={run} isLoading={busy} disabled={!subjectId}>Map questions</Button>
      </>}>
      <p className="text-xs text-ink-400">The questions are only re-classified inside <b>{examName}</b>. Their mapping in every other exam, and their content, stay as they are.</p>
      <FormField label="Subject">
        <Select value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
          {active.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
        </Select>
      </FormField>
      <FormField label="Topic (optional)">
        <Select value={topicId} onChange={(e) => setTopicId(e.target.value)}>
          <option value="">— no topic —</option>
          {topics.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
        </Select>
      </FormField>
      {error && <Alert>{error}</Alert>}
    </Modal>
  );
}

// ------------------------------------------------------------------------------------------------
// How ONE question is classified in every exam it belongs to
// ------------------------------------------------------------------------------------------------
export function ClassificationsDialog({ question, onClose }) {
  const { token } = useAdminAuth();
  const [rows, setRows] = useState(null);
  const [error, setError] = useState(null);
  useEffect(() => {
    let alive = true;
    getQuestionClassifications(token, question.id).then((r) => alive && setRows(r)).catch((e) => alive && setError(friendlyError(e)));
    return () => { alive = false; };
  }, [token, question.id]);
  return (
    <Modal title="Classification in each exam" icon={Layers} onClose={onClose} wide footer={<Button variant="secondary" onClick={onClose}>Close</Button>}>
      <p className="line-clamp-3 text-sm text-ink-600">{question.questionText}</p>
      {error && <Alert>{error}</Alert>}
      {!rows && !error && <p className="flex items-center gap-2 text-sm text-ink-400"><Loader2 className="h-4 w-4 animate-spin" /> Loading…</p>}
      {rows && (
        <table className="w-full text-left text-sm">
          <thead className="text-[11px] font-bold uppercase text-ink-400"><tr><th className="py-2">Exam</th><th className="py-2">Subject</th><th className="py-2">Topic</th></tr></thead>
          <tbody className="divide-y divide-primary-50">
            {rows.map((r) => (
              <tr key={r.examId}>
                <td className="py-2 font-semibold text-ink-900">{r.examName}<span className="ml-1 text-xs font-normal text-ink-400">{r.years.join(", ")}</span></td>
                <td className="py-2">{r.examSubjectName || <span className="text-ink-400">Unassigned</span>}</td>
                <td className="py-2">{r.examTopicName || <span className="text-ink-400">—</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Modal>
  );
}
