import { useCallback, useEffect, useMemo, useState } from "react";
import { CheckCircle2, ChevronLeft, ChevronRight } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { PageHeader, Card, Button, FormField, TextInput, TextArea, Select, Alert, StatusBadge, friendlyError } from "../components/AdminUI";
import { imgSrc } from "../components/EditImageField";
import {
  bulkListPapers, bulkListQuestions, bulkFilterOptions, bulkPreview, bulkApply, uploadBulkImage, listStimuli,
} from "../api/sharedContent";

const STEPS = ["Papers", "Questions", "Operation", "Preview & apply"];
const OPS = [
  ["AttachStimulus", "Attach shared content (passage / table / image)"],
  ["ReplaceStimulus", "Replace shared content"],
  ["RemoveStimulus", "Remove shared content"],
  ["SetFields", "Set subject / topic / difficulty / language / explanation"],
  ["SetImage", "Set an image on a field"],
  ["RemoveImage", "Remove an image from a field"],
];
const IMAGE_FIELDS = ["Question", "OptionA", "OptionB", "OptionC", "OptionD", "Explanation"];
const MAX_PAPERS = 100;

const qKey = (q) => (q.questionId ? `q:${q.questionId}` : `l:${q.linkId}`);
const toTarget = (q) => (q.questionId ? { questionId: q.questionId } : { linkId: q.linkId });

function Pager({ page, total, pageSize, onPage }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  return (
    <div className="mt-3 flex items-center justify-center gap-3 text-sm">
      <Button variant="ghost" disabled={page <= 1} onClick={() => onPage(page - 1)}>Prev</Button>
      <span className="text-ink-600">Page {page} / {pages} · {total} total</span>
      <Button variant="ghost" disabled={page >= pages} onClick={() => onPage(page + 1)}>Next</Button>
    </div>
  );
}

// ---------- Step 1 ----------
function PapersStep({ token, selected, setSelected }) {
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);
  const pageSize = 20;

  useEffect(() => {
    const t = setTimeout(() => bulkListPapers(token, { search, page, pageSize }).then(setData).catch((e) => setError(friendlyError(e))), 250);
    return () => clearTimeout(t);
  }, [token, search, page]);

  const toggle = (p) => setSelected((m) => {
    const n = new Map(m);
    if (n.has(p.id)) n.delete(p.id);
    else if (n.size >= MAX_PAPERS) setError(`At most ${MAX_PAPERS} papers per operation.`);
    else n.set(p.id, p);
    return n;
  });
  const pageAll = () => setSelected((m) => { const n = new Map(m); (data?.items || []).forEach((p) => n.size < MAX_PAPERS && n.set(p.id, p)); return n; });

  return (
    <Card>
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <TextInput className="max-w-xs" placeholder="Search exam / label" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} />
        <Button variant="secondary" onClick={pageAll}>Select this page</Button>
        <Button variant="ghost" onClick={() => setSelected(new Map())}>Clear ({selected.size})</Button>
      </div>
      {error && <div className="mb-2"><Alert>{error}</Alert></div>}
      <div className="divide-y divide-primary-50 rounded-xl2 border border-primary-100">
        {!data && <p className="p-4 text-sm text-ink-400">Loading…</p>}
        {data?.items.map((p) => (
          <label key={p.id} className="flex cursor-pointer items-center gap-3 px-3 py-2 text-sm hover:bg-primary-50/50">
            <input type="checkbox" checked={selected.has(p.id)} onChange={() => toggle(p)} />
            <span className="min-w-0 flex-1 truncate font-semibold text-ink-900">{p.examName} {p.year} · {p.language}{p.paperLabel ? ` · ${p.paperLabel}` : ""}</span>
            <span className="text-xs text-ink-400">{p.questionCount} Q</span>
            <StatusBadge status={p.status} />
            {!p.contentEditable && <span className="text-[11px] text-ink-400" title="Published: only shared-content attach/replace/remove is allowed">locked</span>}
          </label>
        ))}
      </div>
      {data && <Pager page={page} total={data.totalCount} pageSize={pageSize} onPage={setPage} />}
    </Card>
  );
}

// ---------- Step 2 ----------
function QuestionsStep({ token, paperIds, selection, setSelection }) {
  const [filter, setFilter] = useState({ search: "", subject: "", topic: "", stimulus: "any", paperId: "" });
  const [page, setPage] = useState(1);
  const [data, setData] = useState(null);
  const [opts, setOpts] = useState({ subjects: [], topics: [] });
  const [error, setError] = useState(null);
  const pageSize = 25;

  useEffect(() => { bulkFilterOptions(token, paperIds).then(setOpts).catch(() => {}); }, [token, paperIds]);
  useEffect(() => {
    const t = setTimeout(() => {
      setData(null);
      bulkListQuestions(token, { paperIds, ...filter, page, pageSize }).then(setData).catch((e) => setError(friendlyError(e)));
    }, 250);
    return () => clearTimeout(t);
  }, [token, paperIds, filter, page]);

  const setF = (k, v) => { setFilter((f) => ({ ...f, [k]: v })); setPage(1); };
  const rows = selection.mode === "explicit" ? selection.rows : null;
  const toggle = (q) => setSelection((s) => {
    const n = new Map(s.mode === "explicit" ? s.rows : []);
    n.has(qKey(q)) ? n.delete(qKey(q)) : n.set(qKey(q), q);
    return { mode: "explicit", rows: n };
  });
  const selectPage = () => setSelection((s) => {
    const n = new Map(s.mode === "explicit" ? s.rows : []);
    (data?.items || []).forEach((q) => n.set(qKey(q), q));
    return { mode: "explicit", rows: n };
  });
  function selectAllMatching() {
    if (!data) return;
    const msg = `Select ALL ${data.totalCount} questions matching the current filters, across ${paperIds.length} paper(s)? The server will re-check this count when you apply.`;
    if (window.confirm(msg)) setSelection({ mode: "all", filter: { ...filter, paperId: filter.paperId || undefined }, count: data.totalCount });
  }

  return (
    <Card>
      <div className="mb-3 grid gap-2 sm:grid-cols-5">
        <TextInput className="sm:col-span-2" placeholder="Search question text" value={filter.search} onChange={(e) => setF("search", e.target.value)} />
        <Select value={filter.subject} onChange={(e) => setF("subject", e.target.value)}><option value="">All subjects</option>{opts.subjects.map((s) => <option key={s}>{s}</option>)}</Select>
        <Select value={filter.topic} onChange={(e) => setF("topic", e.target.value)}><option value="">All topics</option>{opts.topics.map((s) => <option key={s}>{s}</option>)}</Select>
        <Select value={filter.stimulus} onChange={(e) => setF("stimulus", e.target.value)}>
          <option value="any">Any shared content</option><option value="with">With shared content</option><option value="without">Without shared content</option>
        </Select>
      </div>
      <div className="mb-2 flex flex-wrap items-center gap-2">
        <Button variant="secondary" onClick={selectPage}>Select current page</Button>
        <Button variant="secondary" onClick={selectAllMatching} disabled={!data || data.totalCount === 0}>Select all matching{data ? ` (${data.totalCount})` : ""}</Button>
        <Button variant="ghost" onClick={() => setSelection({ mode: "explicit", rows: new Map() })}>Clear</Button>
        <span className="ml-auto text-xs font-semibold text-primary-600">
          {selection.mode === "all" ? `All ${selection.count} matching selected` : `${rows.size} selected`}
        </span>
      </div>
      {selection.mode === "all" && <div className="mb-2"><Alert type="success">“All matching” mode: ticking is disabled. Clear to pick individual questions.</Alert></div>}
      {error && <div className="mb-2"><Alert>{error}</Alert></div>}
      <div className="divide-y divide-primary-50 rounded-xl2 border border-primary-100">
        {!data && <p className="p-4 text-sm text-ink-400">Loading…</p>}
        {data?.items.length === 0 && <p className="p-4 text-sm text-ink-400">No questions match.</p>}
        {data?.items.map((q) => (
          <label key={qKey(q)} className="flex cursor-pointer items-start gap-3 px-3 py-2 text-sm hover:bg-primary-50/50">
            <input type="checkbox" className="mt-1" disabled={selection.mode === "all"} checked={selection.mode === "all" || rows.has(qKey(q))} onChange={() => toggle(q)} />
            <span className="w-24 shrink-0 text-xs text-ink-400"><b className="text-primary-600">Q.{q.questionNumber}</b><br />{q.paperName}</span>
            <span className="min-w-0 flex-1 text-ink-700 line-clamp-2">{q.textPreview}</span>
            <span className="flex shrink-0 flex-col items-end gap-1 text-[11px]">
              {q.hasStimulus && <span className="rounded-full bg-secondary-50 px-2 py-0.5 font-semibold text-secondary-600">{q.stimulusBusinessId}</span>}
              {q.source === "QuestionBank" && <span className="rounded-full bg-accent-50 px-2 py-0.5 font-semibold text-accent-600">QB</span>}
              {!q.contentEditable && <span className="text-ink-400">content locked</span>}
            </span>
          </label>
        ))}
      </div>
      {data && <Pager page={page} total={data.totalCount} pageSize={pageSize} onPage={setPage} />}
    </Card>
  );
}

// ---------- Step 3 ----------
function StimulusPicker({ token, value, onChange, label }) {
  const [search, setSearch] = useState("");
  const [items, setItems] = useState([]);
  useEffect(() => {
    const t = setTimeout(() => listStimuli(token, { search, status: "Active", pageSize: 30 }).then((r) => setItems(r.items)).catch(() => {}), 250);
    return () => clearTimeout(t);
  }, [token, search]);
  return (
    <div className="grid gap-2 sm:grid-cols-2">
      <FormField label={`${label} — search`}><TextInput value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Title / STM id" /></FormField>
      <FormField label={label}>
        <Select value={value} onChange={(e) => onChange(e.target.value)}>
          <option value="">— select —</option>
          {items.map((i) => <option key={i.id} value={i.id}>{i.businessId} · {i.title}</option>)}
        </Select>
      </FormField>
    </div>
  );
}

function OperationStep({ token, op, setOp }) {
  const set = (patch) => setOp((o) => ({ ...o, ...patch }));
  const setField = (k, v) => setOp((o) => ({ ...o, fields: { ...o.fields, [k]: v } }));
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState(null);

  async function upload(file) {
    if (!file) return;
    setUploading(true); setError(null);
    try { const r = await uploadBulkImage(token, file); set({ imageUrl: r.url }); }
    catch (e) { setError(friendlyError(e)); } finally { setUploading(false); }
  }

  return (
    <Card>
      <FormField label="Operation">
        <Select value={op.type} onChange={(e) => set({ type: e.target.value })}>{OPS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</Select>
      </FormField>
      <div className="mt-4 flex flex-col gap-3">
        {op.type === "AttachStimulus" && <StimulusPicker token={token} label="Shared content to attach" value={op.stimulusId} onChange={(v) => set({ stimulusId: v })} />}
        {op.type === "ReplaceStimulus" && <>
          <StimulusPicker token={token} label="Replace this (old)" value={op.fromStimulusId} onChange={(v) => set({ fromStimulusId: v })} />
          <StimulusPicker token={token} label="With this (new)" value={op.toStimulusId} onChange={(v) => set({ toStimulusId: v })} />
        </>}
        {op.type === "RemoveStimulus" && <>
          <StimulusPicker token={token} label="Remove only this content (leave empty = remove ALL shared content)" value={op.stimulusId} onChange={(v) => set({ stimulusId: v })} />
          {!op.stimulusId && <Alert>No content chosen: every shared-content link on the selected questions will be removed (the content itself is kept).</Alert>}
        </>}
        {op.type === "SetFields" && (
          <div className="grid gap-3 sm:grid-cols-2">
            <FormField label="Subject"><TextInput value={op.fields.subject} onChange={(e) => setField("subject", e.target.value)} placeholder="leave empty = unchanged" /></FormField>
            <FormField label="Topic"><TextInput value={op.fields.topic} onChange={(e) => setField("topic", e.target.value)} placeholder="leave empty = unchanged" /></FormField>
            <FormField label="Difficulty"><Select value={op.fields.difficultyLevel} onChange={(e) => setField("difficultyLevel", e.target.value)}><option value="">unchanged</option><option>Easy</option><option>Medium</option><option>Hard</option></Select></FormField>
            <FormField label="Language"><TextInput value={op.fields.language} onChange={(e) => setField("language", e.target.value)} placeholder="leave empty = unchanged" /></FormField>
            <div className="sm:col-span-2">
              <FormField label="Explanation (same text for every selected question)"><TextArea rows={3} value={op.fields.explanation} disabled={op.fields.clearExplanation} onChange={(e) => setField("explanation", e.target.value)} /></FormField>
              <label className="mt-1 flex items-center gap-2 text-xs text-ink-600"><input type="checkbox" checked={op.fields.clearExplanation} onChange={(e) => setField("clearExplanation", e.target.checked)} />Clear explanation instead</label>
            </div>
          </div>
        )}
        {(op.type === "SetImage" || op.type === "RemoveImage") && (
          <FormField label="Image field"><Select value={op.imageField} onChange={(e) => set({ imageField: e.target.value })}>{IMAGE_FIELDS.map((f) => <option key={f}>{f}</option>)}</Select></FormField>
        )}
        {op.type === "SetImage" && (
          <div>
            <label className="flex cursor-pointer items-center gap-2 rounded-xl2 border border-dashed border-primary-100 px-3 py-2.5 text-sm text-ink-600 hover:border-secondary-500">
              {uploading ? "Uploading…" : op.imageUrl ? "Replace uploaded image" : "Upload image (stored once, reused for every question)"}
              <input type="file" accept=".png,.jpg,.jpeg,.webp,.svg" className="hidden" onChange={(e) => { upload(e.target.files?.[0]); e.target.value = ""; }} />
            </label>
            {op.imageUrl && <img src={imgSrc(op.imageUrl)} alt="" className="mt-2 h-24 rounded-lg border border-primary-100" />}
          </div>
        )}
        {error && <Alert>{error}</Alert>}
      </div>
    </Card>
  );
}

function emptyOp() {
  return { type: "AttachStimulus", stimulusId: "", fromStimulusId: "", toStimulusId: "", imageField: "Question", imageUrl: "",
    fields: { subject: "", topic: "", difficultyLevel: "", language: "", explanation: "", clearExplanation: false } };
}

function opIsReady(op) {
  switch (op.type) {
    case "AttachStimulus": return Boolean(op.stimulusId);
    case "ReplaceStimulus": return Boolean(op.fromStimulusId && op.toStimulusId && op.fromStimulusId !== op.toStimulusId);
    case "RemoveStimulus": return true;
    case "SetImage": return Boolean(op.imageUrl);
    case "RemoveImage": return true;
    case "SetFields": { const f = op.fields; return Boolean(f.subject.trim() || f.topic.trim() || f.difficultyLevel || f.language.trim() || f.explanation.trim() || f.clearExplanation); }
    default: return false;
  }
}

function buildOperation(op) {
  const base = { type: op.type };
  switch (op.type) {
    case "AttachStimulus": return { ...base, stimulusId: op.stimulusId };
    case "ReplaceStimulus": return { ...base, fromStimulusId: op.fromStimulusId, toStimulusId: op.toStimulusId };
    case "RemoveStimulus": return { ...base, stimulusId: op.stimulusId || null };
    case "SetImage": return { ...base, imageField: op.imageField, imageUrl: op.imageUrl };
    case "RemoveImage": return { ...base, imageField: op.imageField };
    case "SetFields": {
      const f = op.fields;
      return { ...base, fields: {
        subject: f.subject.trim() || null, topic: f.topic.trim() || null, difficultyLevel: f.difficultyLevel || null,
        language: f.language.trim() || null, explanation: f.clearExplanation ? null : (f.explanation.trim() || null), clearExplanation: f.clearExplanation,
      } };
    }
    default: return base;
  }
}

// ---------- Page ----------
export default function BulkQuestionEditor() {
  const { token, hasPermission } = useAdminAuth();
  const canEdit = hasPermission("EditPaper");
  const [step, setStep] = useState(0);
  const [papers, setPapers] = useState(new Map());
  const [selection, setSelection] = useState({ mode: "explicit", rows: new Map() });
  const [op, setOp] = useState(emptyOp());
  const [preview, setPreview] = useState(null);
  const [loading, setLoading] = useState(false);
  const [applying, setApplying] = useState(false);
  const [result, setResult] = useState(null);
  const [error, setError] = useState(null);

  const paperIds = useMemo(() => [...papers.keys()], [papers]);
  const selCount = selection.mode === "all" ? selection.count : selection.rows.size;
  const dirty = !result && (papers.size > 0 || selCount > 0);

  useEffect(() => {
    if (!dirty) return undefined;
    const h = (e) => { e.preventDefault(); e.returnValue = ""; };
    window.addEventListener("beforeunload", h);
    return () => window.removeEventListener("beforeunload", h);
  }, [dirty]);

  const request = useCallback(() => {
    const body = { paperIds, operation: buildOperation(op) };
    if (selection.mode === "all") {
      body.allMatching = { search: selection.filter.search || null, subject: selection.filter.subject || null, topic: selection.filter.topic || null, stimulus: selection.filter.stimulus, paperId: selection.filter.paperId || null };
      body.expectedCount = selection.count;
    } else {
      body.targets = [...selection.rows.values()].map(toTarget);
    }
    return body;
  }, [paperIds, op, selection]);

  // Any change to papers/selection/operation invalidates a previous preview.
  useEffect(() => { setPreview(null); }, [papers, selection, op]);

  async function runPreview() {
    setLoading(true); setError(null);
    try { setPreview(await bulkPreview(token, request())); }
    catch (e) { setError(friendlyError(e)); } finally { setLoading(false); }
  }

  async function apply() {
    if (applying || !preview?.canApply) return;
    if (!window.confirm(`Apply to ${preview.changedCount} question(s) in ${preview.paperCount} paper(s)? This is saved in one transaction.`)) return;
    setApplying(true); setError(null);
    try { setResult(await bulkApply(token, request())); }
    catch (e) { setError(friendlyError(e)); } finally { setApplying(false); }
  }

  function reset() {
    setPapers(new Map()); setSelection({ mode: "explicit", rows: new Map() }); setOp(emptyOp()); setPreview(null); setResult(null); setError(null); setStep(0);
  }

  const canNext = step === 0 ? papers.size > 0 : step === 1 ? selCount > 0 : step === 2 ? opIsReady(op) : false;

  if (!canEdit) return <div><PageHeader title="Bulk Question Editor" /><div className="p-6"><Alert>You need the Edit Paper permission to use this tool.</Alert></div></div>;

  return (
    <div>
      <PageHeader title="Bulk Question Editor" subtitle="Update many questions across many papers. Nothing is deleted or recreated; changes are previewed first and saved all-or-nothing." />
      <div className="mx-auto max-w-5xl p-6">
        <ol className="mb-5 flex flex-wrap gap-2 text-xs font-semibold">
          {STEPS.map((s, i) => (
            <li key={s} className={`rounded-full px-3 py-1 ${i === step ? "bg-primary-600 text-white" : i < step ? "bg-mint-50 text-mint-500" : "bg-primary-50 text-ink-400"}`}>{i + 1}. {s}</li>
          ))}
        </ol>

        {result ? (
          <Card>
            <div className="flex items-start gap-3">
              <CheckCircle2 className={`h-6 w-6 ${result.success ? "text-mint-500" : "text-red-500"}`} />
              <div>
                <p className="font-bold text-ink-900">{result.success ? "Applied" : "Not applied"}</p>
                <p className="text-sm text-ink-600">{result.message}</p>
                {result.errors?.length > 0 && <ul className="mt-2 list-disc pl-5 text-xs text-red-600">{result.errors.map((e, i) => <li key={i}>{e}</li>)}</ul>}
              </div>
            </div>
            <Button className="mt-4" onClick={reset}>Start another</Button>
          </Card>
        ) : (
          <>
            {step === 0 && <PapersStep token={token} selected={papers} setSelected={setPapers} />}
            {step === 1 && <QuestionsStep token={token} paperIds={paperIds} selection={selection} setSelection={setSelection} />}
            {step === 2 && <OperationStep token={token} op={op} setOp={setOp} />}
            {step === 3 && (
              <Card>
                <p className="text-sm text-ink-600">{papers.size} paper(s) · {selCount} question(s) · <b>{OPS.find(([v]) => v === op.type)?.[1]}</b></p>
                {!preview && <Button className="mt-3" isLoading={loading} onClick={runPreview}>Run preview (nothing is saved)</Button>}
                {preview && (
                  <div className="mt-3 flex flex-col gap-3">
                    <div className="grid grid-cols-3 gap-3 text-center text-sm">
                      <div className="rounded-xl2 bg-primary-50 p-3"><p className="text-xl font-extrabold text-primary-600">{preview.changedCount}</p>will change</div>
                      <div className="rounded-xl2 bg-primary-50 p-3"><p className="text-xl font-extrabold text-ink-600">{preview.unchangedCount}</p>already as requested</div>
                      <div className="rounded-xl2 bg-primary-50 p-3"><p className={`text-xl font-extrabold ${preview.errorCount ? "text-red-600" : "text-mint-500"}`}>{preview.errorCount}</p>errors</div>
                    </div>
                    {preview.stimulusLabel && <p className="text-xs text-ink-600">Shared content: <b>{preview.stimulusLabel}</b></p>}
                    {preview.changes.length > 0 && <ul className="text-xs text-ink-600">{preview.changes.map((c) => <li key={c.label}>• {c.label}: <b>{c.count}</b></li>)}</ul>}
                    {preview.warnings.map((w, i) => <Alert key={i}>{w}</Alert>)}
                    {preview.errors.length > 0 && (
                      <div><Alert>Fix these before applying (nothing would be saved):</Alert>
                        <ul className="mt-2 max-h-48 list-disc overflow-y-auto pl-5 text-xs text-red-600">{preview.errors.map((e, i) => <li key={i}>{e}</li>)}</ul>
                        {preview.errorCount > preview.errors.length && <p className="mt-1 text-xs text-ink-400">…and {preview.errorCount - preview.errors.length} more.</p>}
                      </div>
                    )}
                    <div className="flex gap-2">
                      <Button isLoading={applying} disabled={!preview.canApply || preview.changedCount === 0} onClick={apply}>Apply {preview.changedCount} change(s)</Button>
                      <Button variant="ghost" onClick={runPreview} isLoading={loading}>Re-run preview</Button>
                    </div>
                  </div>
                )}
              </Card>
            )}
            {error && <div className="mt-3"><Alert>{error}</Alert></div>}
            <div className="mt-4 flex justify-between">
              <Button variant="ghost" disabled={step === 0} onClick={() => setStep(step - 1)}><ChevronLeft className="h-4 w-4" />Back</Button>
              {step < 3 && <Button disabled={!canNext} onClick={() => setStep(step + 1)}>Next<ChevronRight className="h-4 w-4" /></Button>}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
