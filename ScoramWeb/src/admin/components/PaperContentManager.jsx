import { useCallback, useEffect, useMemo, useState } from "react";
import { ArrowDown, ArrowUp, Archive, ArchiveRestore, Pencil, Plus, Trash2 } from "lucide-react";
import { Link } from "react-router-dom";
import { Card, Button, FormField, TextInput, Select, Alert, friendlyError } from "./AdminUI";
import ContentBlocksField, { cleanBlocks } from "./ContentBlocksField";
import { RichQuestionBody, MathText } from "../../components/questions/MathText";
import { StimulusPanel, PaperInstructionsList } from "../../components/tests/PaperContent";
import {
  listInstructions, createInstruction, updateInstruction, archiveInstruction, restoreInstruction, reorderInstructions, deleteInstruction,
  listStimuli, listStimulusLinks, attachStimulus, detachStimulus, replaceStimulus, getContentPreview, uploadStimulusImage,
} from "../api/sharedContent";

const TABS = [["instructions", "Instructions"], ["stimulus", "Shared content"], ["preview", "Student preview"]];
const targetKey = (q) => (q.questionId ? `q:${q.questionId}` : `l:${q.linkId}`);
const toTarget = (q) => (q.questionId ? { questionId: q.questionId } : { linkId: q.linkId });

// ---------------- Instructions ----------------
function InstructionsTab({ token, paperId, canEdit }) {
  const [items, setItems] = useState(null);
  const [error, setError] = useState(null);
  const [editing, setEditing] = useState(null); // null | "new" | instruction
  const [form, setForm] = useState({ title: "", language: "English", contentBlocks: [] });
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => listInstructions(token, paperId, true).then(setItems).catch((e) => setError(friendlyError(e))), [token, paperId]);
  useEffect(() => { load(); }, [load]);

  const active = (items || []).filter((i) => i.status === "Active");

  async function run(fn) {
    if (busy) return;
    setBusy(true); setError(null);
    try { await fn(); await load(); } catch (e) { setError(friendlyError(e)); } finally { setBusy(false); }
  }

  function startEdit(ins) {
    setEditing(ins || "new");
    setForm(ins ? { title: ins.title || "", language: ins.language || "English", contentBlocks: ins.contentBlocks || [] } : { title: "", language: "English", contentBlocks: [] });
  }

  async function save() {
    const blocks = cleanBlocks(form.contentBlocks);
    if (blocks.length === 0) return setError("Add at least one content block.");
    await run(async () => {
      const body = { title: form.title.trim() || null, language: form.language, contentBlocks: blocks };
      if (editing === "new") await createInstruction(token, paperId, body);
      else await updateInstruction(token, paperId, editing.id, body);
      setEditing(null);
    });
  }

  function move(ins, d) {
    const ids = active.map((i) => i.id);
    const i = ids.indexOf(ins.id), j = i + d;
    if (j < 0 || j >= ids.length) return;
    [ids[i], ids[j]] = [ids[j], ids[i]];
    run(() => reorderInstructions(token, paperId, ids));
  }

  return (
    <div className="flex flex-col gap-3">
      <p className="text-xs text-ink-400">Shown to students on the instructions screen before the test starts (and from the “Instructions” button during it).</p>
      {error && <Alert>{error}</Alert>}
      {!items && <p className="text-sm text-ink-400">Loading…</p>}
      {items?.length === 0 && !editing && <p className="text-sm text-ink-400">No instructions yet.</p>}

      {items?.map((ins) => (
        <div key={ins.id} className={`rounded-xl2 border p-3 ${ins.status === "Active" ? "border-primary-100" : "border-dashed border-ink-400/40 opacity-70"}`}>
          <div className="flex items-start justify-between gap-2">
            <div className="min-w-0 flex-1 text-sm">
              {ins.title && <p className="font-bold text-ink-900">{ins.title}</p>}
              <RichQuestionBody contentBlocks={ins.contentBlocks} fallbackText="" className="space-y-1 text-ink-700" />
            </div>
            {canEdit && (
              <div className="flex shrink-0 items-center gap-1 text-ink-400">
                {ins.status === "Active" && <>
                  <button type="button" disabled={busy} onClick={() => move(ins, -1)} className="p-1 hover:text-primary-600"><ArrowUp className="h-4 w-4" /></button>
                  <button type="button" disabled={busy} onClick={() => move(ins, 1)} className="p-1 hover:text-primary-600"><ArrowDown className="h-4 w-4" /></button>
                </>}
                <button type="button" onClick={() => startEdit(ins)} className="p-1 hover:text-primary-600"><Pencil className="h-4 w-4" /></button>
                <button type="button" disabled={busy} onClick={() => run(() => (ins.status === "Active" ? archiveInstruction : restoreInstruction)(token, paperId, ins.id))} className="p-1 hover:text-primary-600" title={ins.status === "Active" ? "Archive" : "Restore"}>
                  {ins.status === "Active" ? <Archive className="h-4 w-4" /> : <ArchiveRestore className="h-4 w-4" />}
                </button>
                {ins.status !== "Active" && (
                  <button type="button" disabled={busy} onClick={() => window.confirm("Permanently delete this archived instruction?") && run(() => deleteInstruction(token, paperId, ins.id))} className="p-1 hover:text-red-600"><Trash2 className="h-4 w-4" /></button>
                )}
              </div>
            )}
          </div>
        </div>
      ))}

      {editing && (
        <Card className="border border-primary-100">
          <div className="flex flex-col gap-3">
            <div className="grid grid-cols-3 gap-3">
              <div className="col-span-2"><FormField label="Heading (optional)"><TextInput maxLength={200} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} /></FormField></div>
              <FormField label="Language"><Select value={form.language} onChange={(e) => setForm({ ...form, language: e.target.value })}><option>English</option><option>Hindi</option><option>Bilingual</option></Select></FormField>
            </div>
            <ContentBlocksField label="Content" value={form.contentBlocks} onChange={(v) => setForm({ ...form, contentBlocks: v })} uploadImage={(f) => uploadStimulusImage(token, f)} />
            <div className="flex gap-2"><Button isLoading={busy} onClick={save}>Save</Button><Button variant="ghost" onClick={() => setEditing(null)}>Cancel</Button></div>
          </div>
        </Card>
      )}
      {canEdit && !editing && <div><Button variant="secondary" onClick={() => startEdit(null)}><Plus className="h-4 w-4" />Add instruction</Button></div>}
    </div>
  );
}

// ---------------- Shared content <-> questions ----------------
function StimulusTab({ token, paperId, canEdit }) {
  const [data, setData] = useState(null);       // { questions, links }
  const [error, setError] = useState(null);
  const [notice, setNotice] = useState(null);
  const [picked, setPicked] = useState(new Set());
  const [search, setSearch] = useState("");
  const [options, setOptions] = useState([]);
  const [stimulusId, setStimulusId] = useState("");
  const [fromId, setFromId] = useState("");
  const [range, setRange] = useState({ from: "", to: "" });
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => Promise.all([getContentPreview(token, paperId), listStimulusLinks(token, paperId)])
    .then(([p, links]) => setData({ questions: p.questions, links }))
    .catch((e) => setError(friendlyError(e))), [token, paperId]);
  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    const t = setTimeout(() => listStimuli(token, { search, status: "Active", pageSize: 30 }).then((r) => setOptions(r.items)).catch(() => {}), 250);
    return () => clearTimeout(t);
  }, [token, search]);

  const linksByTarget = useMemo(() => {
    const m = new Map();
    (data?.links || []).forEach((l) => {
      const k = l.questionId ? `q:${l.questionId}` : `l:${l.linkId}`;
      m.set(k, [...(m.get(k) || []), l]);
    });
    return m;
  }, [data]);

  const toggle = (k) => setPicked((s) => { const n = new Set(s); n.has(k) ? n.delete(k) : n.add(k); return n; });
  function selectRange() {
    const a = Number(range.from), b = Number(range.to);
    if (!a || !b || a > b) return setError("Enter a valid Q.No range, e.g. 21 to 25.");
    setError(null);
    setPicked((s) => { const n = new Set(s); data.questions.filter((q) => q.questionNumber >= a && q.questionNumber <= b).forEach((q) => n.add(targetKey(q))); return n; });
  }

  async function run(fn) {
    if (busy) return;
    if (picked.size === 0) return setError("Select at least one question.");
    setBusy(true); setError(null); setNotice(null);
    try {
      const targets = data.questions.filter((q) => picked.has(targetKey(q))).map(toTarget);
      const res = await fn(targets);
      setNotice(res.message || `Changed ${res.changed}, skipped ${res.skipped}.`);
      setPicked(new Set());
      await load();
    } catch (e) { setError(friendlyError(e)); } finally { setBusy(false); }
  }

  if (!data) return <p className="text-sm text-ink-400">{error || "Loading…"}</p>;

  return (
    <div className="flex flex-col gap-3">
      {canEdit && (
        <Card className="border border-primary-100">
          <div className="grid gap-3 sm:grid-cols-2">
            <FormField label="Find shared content">
              <TextInput placeholder="Search title / STM id" value={search} onChange={(e) => setSearch(e.target.value)} />
            </FormField>
            <FormField label="Choose content" hint={<Link to="/admin/shared-content" className="font-semibold text-primary-600 hover:underline">Create new shared content →</Link>}>
              <Select value={stimulusId} onChange={(e) => setStimulusId(e.target.value)}>
                <option value="">— select —</option>
                {options.map((o) => <option key={o.id} value={o.id}>{o.businessId} · {o.title}</option>)}
              </Select>
            </FormField>
          </div>
          <div className="mt-3 flex flex-wrap items-end gap-2">
            <FormField label="Select Q.No from"><TextInput type="number" className="w-24" value={range.from} onChange={(e) => setRange({ ...range, from: e.target.value })} /></FormField>
            <FormField label="to"><TextInput type="number" className="w-24" value={range.to} onChange={(e) => setRange({ ...range, to: e.target.value })} /></FormField>
            <Button variant="secondary" onClick={selectRange}>Select range</Button>
            <Button variant="ghost" onClick={() => setPicked(new Set(data.questions.map(targetKey)))}>All</Button>
            <Button variant="ghost" onClick={() => setPicked(new Set())}>Clear</Button>
          </div>
          <div className="mt-3 flex flex-wrap items-end gap-2 border-t border-primary-100 pt-3">
            <Button isLoading={busy} disabled={!stimulusId} onClick={() => run((t) => attachStimulus(token, paperId, stimulusId, t))}>Attach to {picked.size} selected</Button>
            <Button variant="danger" isLoading={busy} disabled={!stimulusId} onClick={() => run((t) => detachStimulus(token, paperId, stimulusId, t))}>Remove this content from selected</Button>
            <div className="ml-auto flex items-end gap-2">
              <FormField label="Replace (old →  chosen above)">
                <Select value={fromId} onChange={(e) => setFromId(e.target.value)}>
                  <option value="">old content…</option>
                  {[...new Map((data.links || []).map((l) => [l.stimulusId, l])).values()].map((l) => <option key={l.stimulusId} value={l.stimulusId}>{l.stimulusBusinessId} · {l.stimulusTitle}</option>)}
                </Select>
              </FormField>
              <Button variant="secondary" isLoading={busy} disabled={!stimulusId || !fromId} onClick={() => run((t) => replaceStimulus(token, paperId, fromId, stimulusId, t))}>Replace</Button>
            </div>
          </div>
        </Card>
      )}
      {error && <Alert>{error}</Alert>}
      {notice && <Alert type="success">{notice}</Alert>}

      <div className="divide-y divide-primary-50 rounded-xl2 border border-primary-100 bg-white">
        {data.questions.map((q) => {
          const k = targetKey(q);
          const ls = linksByTarget.get(k) || [];
          return (
            <label key={k} className="flex cursor-pointer items-start gap-3 px-3 py-2 text-sm hover:bg-primary-50/50">
              {canEdit && <input type="checkbox" className="mt-1" checked={picked.has(k)} onChange={() => toggle(k)} />}
              <span className="w-10 shrink-0 font-bold text-primary-600">Q.{q.questionNumber}</span>
              <span className="min-w-0 flex-1 truncate text-ink-700"><MathText text={q.questionText} /></span>
              <span className="flex shrink-0 flex-wrap justify-end gap-1">
                {ls.map((l) => (
                  <span key={l.stimulusId} className={`rounded-full px-2 py-0.5 text-[11px] font-semibold ${l.stimulusStatus === "Active" ? "bg-secondary-50 text-secondary-600" : "bg-ink-400/10 text-ink-600"}`} title={l.stimulusTitle}>{l.stimulusBusinessId}{l.stimulusStatus !== "Active" ? " (archived)" : ""}</span>
                ))}
                {q.source === "QuestionBank" && <span className="rounded-full bg-accent-50 px-2 py-0.5 text-[11px] font-semibold text-accent-600">QB</span>}
              </span>
            </label>
          );
        })}
      </div>
    </div>
  );
}

// ---------------- Student preview ----------------
function PreviewTab({ token, paperId }) {
  const [p, setP] = useState(null);
  const [error, setError] = useState(null);
  useEffect(() => { getContentPreview(token, paperId).then(setP).catch((e) => setError(friendlyError(e))); }, [token, paperId]);
  if (error) return <Alert>{error}</Alert>;
  if (!p) return <p className="text-sm text-ink-400">Loading…</p>;
  const byId = new Map(p.stimuli.map((s) => [s.id, s]));
  return (
    <div className="flex flex-col gap-4">
      <p className="text-xs text-ink-400">Exactly what students see (only Active content). Rendered with the same components as the test screen.</p>
      {p.instructions.length > 0 && <Card className="border border-primary-100"><PaperInstructionsList instructions={p.instructions} /></Card>}
      {p.questions.map((q) => {
        const st = q.stimulusIds.map((id) => byId.get(id)).filter(Boolean);
        return (
          <div key={`${q.questionId}-${q.linkId}`} className={st.length ? "grid gap-3 lg:grid-cols-2" : ""}>
            {st.length > 0 && <StimulusPanel stimuli={st} />}
            <Card className="min-w-0">
              <p className="text-xs font-bold text-primary-600">Q.{q.questionNumber}</p>
              <div className="mt-1 text-sm font-medium text-ink-900"><RichQuestionBody contentBlocks={q.contentBlocks} fallbackText={q.questionText} /></div>
              <ol className="mt-2 space-y-1 text-sm text-ink-700">
                {["A", "B", "C", "D"].map((L) => <li key={L}><b>{L}.</b> <MathText text={q[`option${L}`]} /></li>)}
              </ol>
            </Card>
          </div>
        );
      })}
    </div>
  );
}

export default function PaperContentManager({ token, paperId, canEdit }) {
  const [tab, setTab] = useState("instructions");
  return (
    <Card className="mb-6">
      <h3 className="text-sm font-bold text-ink-900">Manage content</h3>
      <p className="mb-3 text-xs text-ink-400">Paper instructions and shared passages/tables. Questions themselves are never deleted or recreated here.</p>
      <div className="mb-4 flex gap-1 border-b border-primary-100">
        {TABS.map(([k, label]) => (
          <button key={k} type="button" onClick={() => setTab(k)} className={`-mb-px border-b-2 px-3 py-2 text-xs font-semibold ${tab === k ? "border-primary-600 text-primary-600" : "border-transparent text-ink-400 hover:text-ink-600"}`}>{label}</button>
        ))}
      </div>
      {tab === "instructions" && <InstructionsTab token={token} paperId={paperId} canEdit={canEdit} />}
      {tab === "stimulus" && <StimulusTab token={token} paperId={paperId} canEdit={canEdit} />}
      {tab === "preview" && <PreviewTab token={token} paperId={paperId} />}
    </Card>
  );
}
