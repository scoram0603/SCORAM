import { useCallback, useEffect, useRef, useState } from "react";
import { ArrowDown, ArrowRightLeft, ArrowUp, BookOpen, CheckCircle2, ChevronLeft, ChevronRight, GitMerge, Pencil, Plus, Power, Search, Trash2 } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { listManagedSubjects, setManagedSubjectActive } from "../api/subjects";
import { PageHeader, Card, Button, TextInput, Alert, friendlyError } from "../components/AdminUI";
import { SubjectFormDialog, DeactivateDialog, MoveDialog, DeleteDialog, UsageDialog } from "../components/SubjectManagementDialogs";
import { formatDate } from "../components/subjectFormat";

const PAGE_SIZE = 20;

const STATUS_FILTERS = [
  { key: "all", label: "All" },
  { key: "active", label: "Active" },
  { key: "inactive", label: "Inactive" },
];

// [column label, sortBy key | null, header alignment]
const COLUMNS = [
  ["Subject", "name", "left"],
  ["Status", "status", "left"],
  ["Total Questions", "questions", "right"],
  ["PYP Questions", "pyp", "right"],
  ["PYQ Questions", "pyq", "right"],
  ["Mock/Test Questions", "mock", "right"],
  ["Quiz Questions", "quiz", "right"],
  ["Created", "created", "left"],
  ["Updated", "updated", "left"],
  ["Actions", null, "right"],
];

const num = (v) => (v ?? 0).toLocaleString();

// Admin > Subjects. Master-data screen for the QuestionBankSubject records that every PYQ / PYP /
// Practice / Mock / Quiz dropdown and student-facing subject filter is built from. All the risky work
// (rename, merge, reassign, delete) happens behind impact-preview + confirmation dialogs and is
// enforced again on the server.
export default function SubjectManagement() {
  const { token } = useAdminAuth();

  const [data, setData] = useState(null); // { items, total, activeCount, inactiveCount }
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [notice, setNotice] = useState(null);

  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("all");
  const [sortBy, setSortBy] = useState("name");
  const [sortDir, setSortDir] = useState("asc");
  const [page, setPage] = useState(1);

  // dialog: null | { type: "add" | "edit" | "deactivate" | "merge" | "reassign" | "delete" | "usage", subject? }
  const [dialog, setDialog] = useState(null);
  const [rowBusyId, setRowBusyId] = useState(null);
  const requestSeq = useRef(0);

  // Debounce the search box so we don't hit the API on every keystroke.
  useEffect(() => {
    const t = setTimeout(() => { setSearch(searchInput); setPage(1); }, 300);
    return () => clearTimeout(t);
  }, [searchInput]);

  const load = useCallback(async () => {
    const seq = ++requestSeq.current;
    setLoading(true);
    setError(null);
    try {
      const res = await listManagedSubjects(token, { search, status, sortBy, sortDir, page, pageSize: PAGE_SIZE });
      if (seq !== requestSeq.current) return; // a newer request superseded this one
      // Deleting the last row of the last page leaves it empty -- step back instead of showing nothing.
      if (res.items.length === 0 && res.total > 0 && page > 1) { setPage(Math.max(1, Math.ceil(res.total / PAGE_SIZE))); return; }
      setData(res);
    } catch (err) {
      if (seq === requestSeq.current) setError(friendlyError(err));
    } finally {
      if (seq === requestSeq.current) setLoading(false);
    }
  }, [token, search, status, sortBy, sortDir, page]);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    if (!notice) return undefined;
    const t = setTimeout(() => setNotice(null), 7000);
    return () => clearTimeout(t);
  }, [notice]);

  // Every successful dialog action lands here: close, toast, and refresh list + counts (filters/search/page are kept).
  const handleDone = (message, opts) => {
    if (!opts?.keepOpen) setDialog(null);
    if (message) setNotice(message);
    load();
  };

  const changeSort = (key) => {
    if (sortBy === key) setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    else { setSortBy(key); setSortDir(key === "name" || key === "status" ? "asc" : "desc"); }
    setPage(1);
  };

  const activate = async (s) => {
    if (rowBusyId) return;
    setRowBusyId(s.id);
    setError(null);
    try {
      const res = await setManagedSubjectActive(token, s.id, { isActive: true, version: s.version });
      setNotice(res.message);
      await load();
    } catch (err) {
      setError(friendlyError(err));
      load();
    } finally {
      setRowBusyId(null);
    }
  };

  const items = data?.items ?? [];
  const total = data?.total ?? 0;
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE));
  const filtersActive = search.trim() !== "" || status !== "all";
  const counts = { all: (data?.activeCount ?? 0) + (data?.inactiveCount ?? 0), active: data?.activeCount ?? 0, inactive: data?.inactiveCount ?? 0 };

  return (
    <div>
      <PageHeader
        title="Subject Management"
        subtitle="Rename, organise and clean up the subjects used across PYQs, PYPs, tests and quizzes."
        action={
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => setDialog({ type: "merge" })}><GitMerge className="h-4 w-4" /> Merge Subjects</Button>
            <Button onClick={() => setDialog({ type: "add" })}><Plus className="h-4 w-4" /> Add Subject</Button>
          </div>
        }
      />

      <div className="space-y-4 p-4 sm:p-6">
        {notice && (
          <div className="flex items-start gap-2 rounded-xl2 bg-mint-50 p-3 text-sm font-medium text-mint-500" role="status">
            <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" /> {notice}
          </div>
        )}
        {error && <Alert>{error}</Alert>}

        <Card className="!p-4">
          <div className="flex flex-wrap items-center gap-3">
            <div className="relative min-w-[220px] flex-1">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" />
              <TextInput value={searchInput} onChange={(e) => setSearchInput(e.target.value)} placeholder="Search subjects…" className="!pl-9" aria-label="Search subjects" />
            </div>
            <div className="flex gap-1 rounded-xl2 bg-surface p-1" role="tablist" aria-label="Filter by status">
              {STATUS_FILTERS.map((f) => (
                <button key={f.key} type="button" role="tab" aria-selected={status === f.key}
                  onClick={() => { setStatus(f.key); setPage(1); }}
                  className={`rounded-xl2 px-3.5 py-1.5 text-xs font-bold transition-colors ${status === f.key ? "bg-white text-primary-600 shadow-card" : "text-ink-400 hover:text-ink-600"}`}>
                  {f.label} <span className="ml-0.5 font-semibold text-ink-400">{data ? counts[f.key] : ""}</span>
                </button>
              ))}
            </div>
          </div>
        </Card>

        <Card className="!p-0 overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[980px] text-left text-sm">
              <thead className="bg-surface text-[11px] font-bold uppercase tracking-wide text-ink-400">
                <tr>
                  {COLUMNS.map(([label, key, align]) => (
                    <th key={label} scope="col" className={`whitespace-nowrap px-4 py-3 ${align === "right" ? "text-right" : ""}`}
                      aria-sort={key && sortBy === key ? (sortDir === "asc" ? "ascending" : "descending") : undefined}>
                      {key ? (
                        <button type="button" onClick={() => changeSort(key)} className={`inline-flex items-center gap-1 uppercase hover:text-ink-600 ${sortBy === key ? "text-primary-600" : ""}`}>
                          {label}
                          {sortBy === key && (sortDir === "asc" ? <ArrowUp className="h-3 w-3" /> : <ArrowDown className="h-3 w-3" />)}
                        </button>
                      ) : label}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className={`divide-y divide-primary-50 ${loading && data ? "opacity-60" : ""}`}>
                {items.map((s) => (
                  <tr key={s.id} className="hover:bg-surface/60">
                    <td className="px-4 py-3">
                      <button type="button" onClick={() => setDialog({ type: "usage", subject: s })} className="text-left font-bold text-ink-900 hover:text-secondary-500 hover:underline">
                        {s.name}
                      </button>
                    </td>
                    <td className="px-4 py-3">
                      <span className={`rounded-full px-2.5 py-1 text-[11px] font-bold ${s.isActive ? "bg-mint-50 text-mint-500" : "bg-surface text-ink-400"}`}>
                        {s.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                    {[s.usage.totalQuestions, s.usage.pypQuestions, s.usage.questionBankQuestions, s.usage.mockTestQuestions, s.usage.quizQuestions].map((v, i) => (
                      <td key={i} className="px-4 py-3 text-right tabular-nums">
                        <button type="button" onClick={() => setDialog({ type: "usage", subject: s })} className="text-ink-600 hover:text-secondary-500 hover:underline" title="View usage details">
                          {num(v)}
                        </button>
                      </td>
                    ))}
                    <td className="whitespace-nowrap px-4 py-3 text-ink-600">{formatDate(s.createdAt)}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-ink-600">{formatDate(s.updatedAt)}</td>
                    <td className="px-4 py-3">
                      <div className="flex justify-end gap-0.5">
                        <IconAction label="Edit subject" onClick={() => setDialog({ type: "edit", subject: s })} icon={Pencil} />
                        {s.isActive
                          ? <IconAction label="Deactivate subject" onClick={() => setDialog({ type: "deactivate", subject: s })} icon={Power} />
                          : <IconAction label="Activate subject" onClick={() => activate(s)} icon={Power} busy={rowBusyId === s.id} tone="mint" />}
                        <IconAction label="Merge into another subject" onClick={() => setDialog({ type: "merge", subject: s })} icon={GitMerge} />
                        <IconAction label="Reassign content" onClick={() => setDialog({ type: "reassign", subject: s })} icon={ArrowRightLeft} />
                        <IconAction label="Delete subject" onClick={() => setDialog({ type: "delete", subject: s })} icon={Trash2} tone="danger" />
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Loading / empty states */}
          {loading && !data && <p className="px-4 py-12 text-center text-sm text-ink-400">Loading subjects…</p>}
          {!loading && !error && items.length === 0 && (
            <div className="flex flex-col items-center gap-2 px-4 py-14 text-center">
              <span className="flex h-12 w-12 items-center justify-center rounded-full bg-primary-50 text-primary-600"><BookOpen className="h-6 w-6" /></span>
              <p className="text-sm font-bold text-ink-900">{filtersActive ? "No subjects match your filters" : "No subjects yet"}</p>
              <p className="max-w-sm text-xs text-ink-400">
                {filtersActive ? "Try a different search or status filter." : "Add your first subject to start organising questions, papers and tests."}
              </p>
              {filtersActive
                ? <Button variant="secondary" onClick={() => { setSearchInput(""); setSearch(""); setStatus("all"); setPage(1); }}>Clear filters</Button>
                : <Button onClick={() => setDialog({ type: "add" })}><Plus className="h-4 w-4" /> Add Subject</Button>}
            </div>
          )}
          {!loading && error && !data && (
            <div className="px-4 py-10 text-center"><Button variant="secondary" onClick={load}>Try again</Button></div>
          )}

          {/* Pagination */}
          {total > 0 && (
            <div className="flex flex-wrap items-center justify-between gap-2 border-t border-primary-50 px-4 py-3 text-xs text-ink-400">
              <span>
                Showing {(page - 1) * PAGE_SIZE + 1}–{Math.min(page * PAGE_SIZE, total)} of {total}
              </span>
              <div className="flex items-center gap-1">
                <Button variant="ghost" className="!px-2.5 !py-1.5" disabled={page <= 1 || loading} onClick={() => setPage((p) => p - 1)} aria-label="Previous page"><ChevronLeft className="h-4 w-4" /></Button>
                <span className="px-2 font-semibold text-ink-600">Page {page} of {pageCount}</span>
                <Button variant="ghost" className="!px-2.5 !py-1.5" disabled={page >= pageCount || loading} onClick={() => setPage((p) => p + 1)} aria-label="Next page"><ChevronRight className="h-4 w-4" /></Button>
              </div>
            </div>
          )}
        </Card>

        <p className="text-xs text-ink-400">
          “PYQ” counts are Question Bank questions; “PYP” counts are questions inside uploaded papers. Mock Test and Quiz counts are questions placed in those tests —
          they follow each question’s subject automatically.
        </p>
      </div>

      {dialog?.type === "add" && <SubjectFormDialog onClose={() => setDialog(null)} onDone={handleDone} />}
      {dialog?.type === "edit" && <SubjectFormDialog subject={dialog.subject} onClose={() => setDialog(null)} onDone={handleDone} />}
      {dialog?.type === "deactivate" && <DeactivateDialog subject={dialog.subject} onClose={() => setDialog(null)} onDone={handleDone} />}
      {dialog?.type === "merge" && <MoveDialog mode="merge" initialSourceId={dialog.subject?.id} onClose={() => setDialog(null)} onDone={handleDone} />}
      {dialog?.type === "reassign" && <MoveDialog mode="reassign" initialSourceId={dialog.subject?.id} onClose={() => setDialog(null)} onDone={handleDone} />}
      {dialog?.type === "usage" && <UsageDialog subject={dialog.subject} onClose={() => setDialog(null)} />}
      {dialog?.type === "delete" && (
        <DeleteDialog
          subject={dialog.subject}
          onClose={() => setDialog(null)}
          onDone={handleDone}
          onReassign={(subject) => setDialog({ type: "reassign", subject })}
          onDeactivate={(subject) => setDialog({ type: "deactivate", subject })}
        />
      )}
    </div>
  );
}

function IconAction({ label, icon: Icon, onClick, tone = "default", busy = false }) {
  const toneCls = tone === "danger" ? "text-red-500 hover:bg-red-50" : tone === "mint" ? "text-mint-500 hover:bg-mint-50" : "text-ink-400 hover:bg-primary-50 hover:text-primary-600";
  return (
    <button type="button" onClick={onClick} disabled={busy} title={label} aria-label={label}
      className={`rounded-lg p-2 transition-colors disabled:opacity-40 ${toneCls}`}>
      <Icon className={`h-4 w-4 ${busy ? "animate-pulse" : ""}`} />
    </button>
  );
}
