import { useState } from "react";
import { ArrowDown, ArrowUp, ImagePlus, Loader2, Plus, Trash2 } from "lucide-react";
import { TextArea, Alert, friendlyError } from "./AdminUI";
import { imgSrc } from "./EditImageField";

// Visual editor for an ordered { type: text|math|image|table, content } list -- the same shape the
// question editor's raw-JSON field uses, and what the student app's RichQuestionBody renders.
// value = array of blocks, onChange(array). uploadImage(file) -> Promise<{url}>.
// Images are uploaded once and referenced by URL; removing a block never deletes the stored file.
const TYPES = [
  { v: "text", label: "Text" },
  { v: "math", label: "Math (LaTeX)" },
  { v: "image", label: "Image" },
  { v: "table", label: "Table" },
];

function parseTable(content) {
  try {
    const rows = JSON.parse(content);
    if (Array.isArray(rows) && rows.length > 0 && rows.every(Array.isArray)) return rows.map((r) => r.map((c) => String(c ?? "")));
  } catch { /* fall through */ }
  return [["", ""], ["", ""]];
}

function TableBlock({ content, onChange }) {
  const rows = parseTable(content);
  const cols = Math.max(...rows.map((r) => r.length));
  const emit = (next) => onChange(JSON.stringify(next));
  const setCell = (r, c, val) => emit(rows.map((row, ri) => (ri === r ? Object.assign([...row], { [c]: val }) : row)));
  const addRow = () => emit([...rows, Array(cols).fill("")]);
  const addCol = () => emit(rows.map((r) => [...r, ""]));
  const delRow = () => rows.length > 1 && emit(rows.slice(0, -1));
  const delCol = () => cols > 1 && emit(rows.map((r) => r.slice(0, -1)));
  return (
    <div>
      <div className="overflow-x-auto">
        <table className="border-collapse text-sm">
          <tbody>
            {rows.map((row, r) => (
              <tr key={r}>
                {Array.from({ length: cols }).map((_, c) => (
                  <td key={c} className="border border-primary-100 p-0">
                    <input
                      value={row[c] ?? ""}
                      onChange={(e) => setCell(r, c, e.target.value)}
                      className="w-28 px-2 py-1 text-xs focus:bg-primary-50 focus:outline-none"
                    />
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="mt-1.5 flex flex-wrap gap-3 text-xs font-semibold text-primary-600">
        <button type="button" onClick={addRow}>+ Row</button>
        <button type="button" onClick={addCol}>+ Column</button>
        <button type="button" onClick={delRow} className="text-red-600">− Row</button>
        <button type="button" onClick={delCol} className="text-red-600">− Column</button>
      </div>
    </div>
  );
}

export default function ContentBlocksField({ label, value, onChange, uploadImage }) {
  const blocks = Array.isArray(value) ? value : [];
  const [uploadingIdx, setUploadingIdx] = useState(null);
  const [error, setError] = useState(null);

  const update = (i, patch) => onChange(blocks.map((b, bi) => (bi === i ? { ...b, ...patch } : b)));
  const remove = (i) => onChange(blocks.filter((_, bi) => bi !== i));
  const move = (i, d) => {
    const j = i + d;
    if (j < 0 || j >= blocks.length) return;
    const next = [...blocks];
    [next[i], next[j]] = [next[j], next[i]];
    onChange(next);
  };
  const add = (type) => onChange([...blocks, { type, content: type === "table" ? JSON.stringify([["", ""], ["", ""]]) : "" }]);

  async function pick(i, file) {
    if (!file) return;
    setError(null);
    setUploadingIdx(i);
    try {
      const res = await uploadImage(file);
      update(i, { content: res.url });
    } catch (e) {
      setError(friendlyError(e));
    } finally {
      setUploadingIdx(null);
    }
  }

  return (
    <div>
      {label && <span className="mb-1 block text-xs font-semibold text-ink-600">{label}</span>}
      <div className="flex flex-col gap-2">
        {blocks.map((b, i) => (
          <div key={i} className="rounded-xl2 border border-primary-100 bg-white p-2.5">
            <div className="mb-1.5 flex items-center gap-2">
              <select
                value={b.type}
                onChange={(e) => update(i, { type: e.target.value, content: e.target.value === "table" ? JSON.stringify([["", ""], ["", ""]]) : "" })}
                className="rounded-lg border border-primary-100 bg-white px-2 py-1 text-xs font-semibold text-ink-600"
              >
                {TYPES.map((t) => <option key={t.v} value={t.v}>{t.label}</option>)}
              </select>
              <span className="text-[11px] text-ink-400">Block {i + 1}</span>
              <div className="ml-auto flex items-center gap-1 text-ink-400">
                <button type="button" onClick={() => move(i, -1)} disabled={i === 0} className="p-1 hover:text-primary-600 disabled:opacity-30"><ArrowUp className="h-3.5 w-3.5" /></button>
                <button type="button" onClick={() => move(i, 1)} disabled={i === blocks.length - 1} className="p-1 hover:text-primary-600 disabled:opacity-30"><ArrowDown className="h-3.5 w-3.5" /></button>
                <button type="button" onClick={() => remove(i)} className="p-1 hover:text-red-600"><Trash2 className="h-3.5 w-3.5" /></button>
              </div>
            </div>

            {b.type === "image" ? (
              <div className="flex items-center gap-3">
                {b.content && <img src={imgSrc(b.content)} alt="" className="h-20 rounded-lg border border-primary-100" />}
                <label className="flex cursor-pointer items-center gap-2 rounded-xl2 border border-dashed border-primary-100 px-3 py-2 text-xs text-ink-600 hover:border-secondary-500">
                  {uploadingIdx === i ? <Loader2 className="h-4 w-4 animate-spin" /> : <ImagePlus className="h-4 w-4 text-ink-400" />}
                  {b.content ? "Replace image" : "Upload image"}
                  <input type="file" accept=".png,.jpg,.jpeg,.webp,.svg" className="hidden" onChange={(e) => { pick(i, e.target.files?.[0]); e.target.value = ""; }} />
                </label>
              </div>
            ) : b.type === "table" ? (
              <TableBlock content={b.content} onChange={(c) => update(i, { content: c })} />
            ) : (
              <TextArea
                rows={b.type === "math" ? 2 : 4}
                value={b.content}
                onChange={(e) => update(i, { content: e.target.value })}
                placeholder={b.type === "math" ? "LaTeX, e.g. \\frac{x^2-5x+6}{x-2}=0" : "Text. Use $...$ for inline math."}
                className={b.type === "math" ? "font-mono text-xs" : ""}
              />
            )}
          </div>
        ))}
      </div>

      <div className="mt-2 flex flex-wrap items-center gap-2">
        <span className="text-xs text-ink-400">Add:</span>
        {TYPES.map((t) => (
          <button key={t.v} type="button" onClick={() => add(t.v)} className="flex items-center gap-1 rounded-full bg-primary-50 px-2.5 py-1 text-xs font-semibold text-primary-600 hover:bg-primary-100">
            <Plus className="h-3 w-3" strokeWidth={2.5} />{t.label}
          </button>
        ))}
      </div>
      {error && <div className="mt-2"><Alert>{error}</Alert></div>}
    </div>
  );
}

// Drop empty blocks before saving (server rejects nothing-content blocks anyway).
export function cleanBlocks(blocks) {
  return (blocks || []).filter((b) => (b.content || "").trim() !== "").map((b) => ({ type: b.type, content: b.content }));
}
