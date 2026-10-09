import { useCallback, useEffect, useState } from "react";
import { Archive, ArchiveRestore, Pencil, Plus, Search } from "lucide-react";
import { Link } from "react-router-dom";
import { useAdminAuth } from "../context/AdminAuthContext";
import { PageHeader, Card, Button, FormField, TextInput, Select, Alert, friendlyError } from "../components/AdminUI";
import ContentBlocksField, { cleanBlocks } from "../components/ContentBlocksField";
import { StimulusPanel } from "../../components/tests/PaperContent";
import {
  listStimuli, getStimulus, createStimulus, updateStimulus, archiveStimulus, restoreStimulus,
  getStimulusQuestions, uploadStimulusImage,
} from "../api/sharedContent";

const TYPES = ["Passage", "Table", "Chart", "Graph", "Diagram", "Case Study", "Image", "Other"];
const LANGUAGES = ["English", "Hindi", "Bilingual"];
const EMPTY = { title: "", description: "", type: "Passage", language: "English", contentBlocks: [] };

function Editor({ token, id, onClose, onSaved }) {
  const [form, setForm] = useState(EMPTY);
  const [loading, setLoading] = useState(Boolean(id));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);
  const [dirty, setDirty] = useState(false);
  const [linked, setLinked] = useState(null);

  useEffect(() => {
    if (!id) return;
    Promise.all([getStimulus(token, id), getStimulusQuestions(token, id)])
      .then(([s, q]) => {
        setForm({ title: s.title, description: s.description || "", type: s.type || "Passage", language: s.language || "English", contentBlocks: s.contentBlocks || [] });
        setLinked(q);
      })
      .catch((e) => setError(friendlyError(e)))
      .finally(() => setLoading(false));
  }, [id, token]);

  // Unsaved-changes guard (browser tab close/refresh).
  useEffect(() => {
    if (!dirty) return undefined;
    const h = (e) => { e.preventDefault(); e.returnValue = ""; };
    window.addEventListener("beforeunload", h);
    return () => window.removeEventListener("beforeunload", h);
  }, [dirty]);

  const set = (k, v) => { setForm((f) => ({ ...f, [k]: v })); setDirty(true); };

  async function save() {
    if (saving) return; // double-submit guard
    const blocks = cleanBlocks(form.contentBlocks);
    if (!form.title.trim()) return setError("Title is required.");
    if (blocks.length === 0) return setError("Add at least one content block.");
    setSaving(true);
    setError(null);
    try {
      const body = { title: form.title.trim(), description: form.description.trim() || null, type: form.type, language: form.language, contentBlocks: blocks };
      const saved = id ? await updateStimulus(token, id, body) : await createStimulus(token, body);
      setDirty(false);
      onSaved(saved);
    } catch (e) {
      setError(friendlyError(e));
    } finally {
      setSaving(false);
    }
  }

  function close() {
    if (dirty && !window.confirm("Discard unsaved changes?")) return;
    onClose();
  }

  if (loading) return <Card><p className="text-sm text-ink-400">Loading…</p></Card>;

  const previewStimulus = [{ id: "preview", title: form.title, contentBlocks: cleanBlocks(form.contentBlocks) }];

  return (
    <div className="grid gap-4 xl:grid-cols-2">
      <Card>
        <h3 className="mb-3 text-sm font-bold text-ink-900">{id ? "Edit shared content" : "New shared content"}</h3>
        <div className="flex flex-col gap-3">
          <FormField label="Title"><TextInput value={form.title} maxLength={200} onChange={(e) => set("title", e.target.value)} placeholder="e.g. Passage – Climate Change (Q.21–25)" /></FormField>
          <div className="grid grid-cols-2 gap-3">
            <FormField label="Type"><Select value={form.type} onChange={(e) => set("type", e.target.value)}>{TYPES.map((t) => <option key={t}>{t}</option>)}</Select></FormField>
            <FormField label="Language"><Select value={form.language} onChange={(e) => set("language", e.target.value)}>{LANGUAGES.map((t) => <option key={t}>{t}</option>)}</Select></FormField>
          </div>
          <FormField label="Internal note (not shown to students)"><TextInput value={form.description} maxLength={500} onChange={(e) => set("description", e.target.value)} /></FormField>
          <ContentBlocksField label="Content" value={form.contentBlocks} onChange={(v) => set("contentBlocks", v)} uploadImage={(f) => uploadStimulusImage(token, f)} />
          {error && <Alert>{error}</Alert>}
          <div className="flex gap-2">
            <Button isLoading={saving} onClick={save}>Save</Button>
            <Button variant="ghost" onClick={close}>Close</Button>
          </div>
        </div>
        {id && linked && (
          <div className="mt-5 border-t border-primary-100 pt-4">
            <h4 className="text-xs font-bold text-ink-900">Used by {linked.length} question{linked.length === 1 ? "" : "s"}</h4>
            {linked.length > 0 && (
              <ul className="mt-2 max-h-48 space-y-1 overflow-y-auto text-xs text-ink-600">
                {linked.map((q, i) => (
                  <li key={i}>
                    <Link className="font-semibold text-primary-600 hover:underline" to={`/admin/papers/${q.paperId}`}>{q.paperName}</Link>
                    {" · Q."}{q.questionNumber ?? "?"} · {q.questionTextPreview}
                  </li>
                ))}
              </ul>
            )}
            {linked.length > 0 && <p className="mt-2 text-[11px] text-ink-400">Editing this content updates it for all of these questions at once.</p>}
          </div>
        )}
      </Card>

      <div>
        <p className="mb-1 text-xs font-semibold text-ink-600">Student preview</p>
        {previewStimulus[0].contentBlocks.length > 0
          ? <StimulusPanel stimuli={previewStimulus} />
          : <Card><p className="text-sm text-ink-400">Preview appears here as you add content.</p></Card>}
      </div>
    </div>
  );
}

export default function SharedContent() {
  const { token, hasPermission } = useAdminAuth();
  const canEdit = hasPermission("EditPaper") || hasPermission("UploadPaper");
  const [filters, setFilters] = useState({ search: "", status: "Active", type: "", language: "" });
  const [page, setPage] = useState(1);
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);
  const [editing, setEditing] = useState(null); // null | "new" | id
  const [busyId, setBusyId] = useState(null);
  const pageSize = 20;

  const load = useCallback(() => {
    setError(null);
    listStimuli(token, { ...filters, page, pageSize })
      .then(setData)
      .catch((e) => setError(friendlyError(e)));
  }, [token, filters, page]);

  useEffect(() => {
    const t = setTimeout(load, 250); // debounce typing in search
    return () => clearTimeout(t);
  }, [load]);

  const setFilter = (k, v) => { setFilters((f) => ({ ...f, [k]: v })); setPage(1); };

  async function toggle(s) {
    if (busyId) return;
    if (s.status === "Active" && s.linkedQuestionCount > 0 &&
      !window.confirm(`This content is used by ${s.linkedQuestionCount} question(s). Archived content is hidden from students. Archive anyway?`)) return;
    setBusyId(s.id);
    try {
      await (s.status === "Active" ? archiveStimulus : restoreStimulus)(token, s.id);
      load();
    } catch (e) { setError(friendlyError(e)); } finally { setBusyId(null); }
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1;

  return (
    <div>
      <PageHeader
        title="Shared Content"
        subtitle="Passages, tables, charts and images shared by several questions — stored once, attached to many."
        action={canEdit && !editing && <Button onClick={() => setEditing("new")}><Plus className="h-4 w-4" />New shared content</Button>}
      />
      <div className="mx-auto max-w-6xl p-6">
        {editing ? (
          <Editor
            token={token}
            id={editing === "new" ? null : editing}
            onClose={() => setEditing(null)}
            onSaved={(s) => { setEditing(s.id); load(); }}
          />
        ) : (
          <>
            <Card className="mb-4">
              <div className="grid gap-3 sm:grid-cols-4">
                <div className="relative sm:col-span-2">
                  <Search className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-ink-400" />
                  <TextInput className="pl-9" placeholder="Search title or STM id" value={filters.search} onChange={(e) => setFilter("search", e.target.value)} />
                </div>
                <Select value={filters.status} onChange={(e) => setFilter("status", e.target.value)}>
                  <option value="Active">Active</option><option value="Archived">Archived</option><option value="">All</option>
                </Select>
                <Select value={filters.type} onChange={(e) => setFilter("type", e.target.value)}>
                  <option value="">All types</option>{TYPES.map((t) => <option key={t}>{t}</option>)}
                </Select>
              </div>
            </Card>
            {error && <div className="mb-3"><Alert>{error}</Alert></div>}
            <Card className="overflow-x-auto p-0">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-primary-100 text-xs text-ink-400">
                  <tr><th className="px-4 py-3">ID</th><th>Title</th><th>Type</th><th>Language</th><th>Questions</th><th>Status</th><th /></tr>
                </thead>
                <tbody>
                  {!data && <tr><td colSpan={7} className="px-4 py-6 text-ink-400">Loading…</td></tr>}
                  {data?.items.length === 0 && <tr><td colSpan={7} className="px-4 py-6 text-ink-400">Nothing found.</td></tr>}
                  {data?.items.map((s) => (
                    <tr key={s.id} className="border-b border-primary-50 last:border-0">
                      <td className="px-4 py-3 font-mono text-xs text-ink-600">{s.businessId}</td>
                      <td className="max-w-xs truncate font-semibold text-ink-900">{s.title}</td>
                      <td className="text-ink-600">{s.type}</td>
                      <td className="text-ink-600">{s.language}</td>
                      <td className="text-ink-600">{s.linkedQuestionCount}</td>
                      <td><span className={`rounded-full px-2 py-0.5 text-xs font-semibold ${s.status === "Active" ? "bg-mint-50 text-mint-500" : "bg-ink-400/10 text-ink-600"}`}>{s.status}</span></td>
                      <td className="px-4">
                        <div className="flex justify-end gap-1">
                          <button type="button" onClick={() => setEditing(s.id)} className="rounded-lg p-1.5 text-ink-400 hover:bg-primary-50 hover:text-primary-600" title={canEdit ? "Edit" : "View"}><Pencil className="h-4 w-4" /></button>
                          {canEdit && (
                            <button type="button" disabled={busyId === s.id} onClick={() => toggle(s)} className="rounded-lg p-1.5 text-ink-400 hover:bg-primary-50 hover:text-primary-600 disabled:opacity-50" title={s.status === "Active" ? "Archive" : "Restore"}>
                              {s.status === "Active" ? <Archive className="h-4 w-4" /> : <ArchiveRestore className="h-4 w-4" />}
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Card>
            {data && data.total > pageSize && (
              <div className="mt-3 flex items-center justify-center gap-3 text-sm">
                <Button variant="ghost" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>Prev</Button>
                <span className="text-ink-600">Page {page} / {totalPages}</span>
                <Button variant="ghost" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>Next</Button>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
