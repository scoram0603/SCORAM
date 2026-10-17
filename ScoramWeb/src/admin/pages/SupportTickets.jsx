import { useEffect, useState } from "react";
import { MessageSquareReply, Search, StickyNote, X } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  addTicketNote, getTicket, listSupportAgents, listTickets, respondToTicket, updateTicket,
} from "../api/platform";
import { updateFeedbackStatus } from "../api/feedback";
import { Alert, Button, Card, PageHeader, Select, TextArea, TextInput, friendlyError } from "../components/AdminUI";
import Pager from "../components/Pager";
import { useLoader } from "../components/analytics";

const PAGE_SIZE = 20;
const PRIORITIES = ["Low", "Normal", "High", "Urgent"];
const STATUSES = ["New", "InReview", "Resolved", "Rejected"];
const when = (iso) => (iso ? new Date(iso).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" }) : "");
const PRIORITY_TONE = { Urgent: "bg-red-50 text-red-600", High: "bg-accent-50 text-accent-600", Normal: "bg-primary-50 text-primary-600", Low: "bg-ink-400/10 text-ink-600" };
const STATUS_TONE = { New: "bg-secondary-50 text-secondary-600", InReview: "bg-accent-50 text-accent-600", Resolved: "bg-mint-50 text-mint-500", Rejected: "bg-ink-400/10 text-ink-600" };

const Chip = ({ tone, children }) => <span className={`rounded-full px-2 py-0.5 text-[11px] font-semibold ${tone}`}>{children}</span>;

export default function SupportTickets() {
  const { token } = useAdminAuth();
  const [filters, setFilters] = useState({ search: "", status: "open", priority: "", assignee: "", type: "" });
  const [applied, setApplied] = useState(filters);
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState(null);
  const [tick, setTick] = useState(0);
  const q = useLoader(() => listTickets(token, { ...applied, page, pageSize: PAGE_SIZE }), [token, applied, page, tick]);

  function apply(next) { setFilters(next); setApplied(next); setPage(1); }

  return (
    <div>
      <PageHeader title="Support Tickets" subtitle="Student feedback and issues, with owners, priorities and replies." />
      <div className="flex flex-col gap-4 p-4 sm:p-6">
        <form onSubmit={(e) => { e.preventDefault(); apply(filters); }} className="flex flex-wrap items-end gap-2">
          <div className="relative min-w-[200px] flex-1">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" />
            <TextInput className="!pl-9" placeholder="Search message, student or ticket no. (FDB-00012)" value={filters.search} onChange={(e) => setFilters({ ...filters, search: e.target.value })} aria-label="Search tickets" />
          </div>
          <Select className="!w-auto" aria-label="Status" value={filters.status} onChange={(e) => apply({ ...filters, status: e.target.value })}>
            <option value="open">Open (New + In review)</option><option value="">All statuses</option>
            {STATUSES.map((s) => <option key={s} value={s}>{s === "InReview" ? "In review" : s}</option>)}
          </Select>
          <Select className="!w-auto" aria-label="Priority" value={filters.priority} onChange={(e) => apply({ ...filters, priority: e.target.value })}>
            <option value="">Any priority</option>{PRIORITIES.map((p) => <option key={p}>{p}</option>)}
          </Select>
          <Select className="!w-auto" aria-label="Assignee" value={filters.assignee} onChange={(e) => apply({ ...filters, assignee: e.target.value })}>
            <option value="">Anyone</option><option value="me">Assigned to me</option><option value="none">Unassigned</option>
          </Select>
          <Button type="submit">Search</Button>
        </form>

        {q.error && <Alert>{q.error}</Alert>}

        <Card className="overflow-x-auto !p-0">
          <table className="w-full min-w-[760px] text-left text-sm">
            <thead className="border-b border-primary-50 text-xs text-ink-400">
              <tr><th className="px-4 py-3 font-semibold">Ticket</th><th className="px-4 py-3 font-semibold">Student</th><th className="px-4 py-3 font-semibold">Summary</th><th className="px-4 py-3 font-semibold">Priority</th><th className="px-4 py-3 font-semibold">Status</th><th className="px-4 py-3 font-semibold">Owner</th></tr>
            </thead>
            <tbody className="divide-y divide-primary-50">
              {q.isLoading && !q.data && Array.from({ length: 5 }).map((_, i) => <tr key={i}><td colSpan={6} className="px-4 py-3"><div className="h-5 animate-pulse rounded bg-primary-50" /></td></tr>)}
              {q.data?.items?.length === 0 && <tr><td colSpan={6} className="px-4 py-10 text-center text-sm text-ink-400">No tickets match these filters.</td></tr>}
              {q.data?.items?.map((t) => (
                <tr key={t.id} className="cursor-pointer hover:bg-primary-50/50" onClick={() => setSelectedId(t.id)}>
                  <td className="px-4 py-3 text-xs"><button type="button" className="font-bold text-primary-600" onClick={() => setSelectedId(t.id)}>{t.code}</button><span className="block text-ink-400">{t.type} · {t.platform}</span></td>
                  <td className="px-4 py-3 text-xs"><span className="font-semibold text-ink-900">{t.userFullName}</span>{t.username && <span className="block text-ink-400">@{t.username}</span>}</td>
                  <td className="max-w-[320px] px-4 py-3 text-xs text-ink-600">{t.preview}{t.noteCount > 0 && <span className="ml-1 text-ink-400">· {t.noteCount} note(s)</span>}</td>
                  <td className="px-4 py-3"><Chip tone={PRIORITY_TONE[t.priority]}>{t.priority}</Chip></td>
                  <td className="px-4 py-3"><Chip tone={STATUS_TONE[t.status]}>{t.status === "InReview" ? "In review" : t.status}</Chip></td>
                  <td className="px-4 py-3 text-xs text-ink-600">{t.assignedTo || <span className="text-ink-400">Unassigned</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
        {q.data && <Pager page={page} pageSize={PAGE_SIZE} totalCount={q.data.totalCount} onPage={setPage} />}
      </div>
      {selectedId && <TicketDrawer id={selectedId} token={token} onClose={() => setSelectedId(null)} onChanged={() => setTick((t) => t + 1)} />}
    </div>
  );
}

function TicketDrawer({ id, token, onClose, onChanged }) {
  const [t, setT] = useState(null);
  const [agents, setAgents] = useState([]);
  const [error, setError] = useState(null);
  const [notice, setNotice] = useState(null);
  const [mode, setMode] = useState("reply"); // reply | note
  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    let cancelled = false;
    getTicket(token, id).then((r) => !cancelled && setT(r)).catch((e) => !cancelled && setError(friendlyError(e)));
    return () => { cancelled = true; };
  }, [token, id, tick]);
  useEffect(() => { listSupportAgents(token).then(setAgents).catch(() => {}); }, [token]);
  useEffect(() => {
    const onKey = (e) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  const reload = () => { setTick((x) => x + 1); onChanged(); };
  async function act(fn, ok) {
    setError(null); setNotice(null);
    try { await fn(); if (ok) setNotice(ok); reload(); } catch (e) { setError(friendlyError(e)); }
  }
  async function submitText() {
    setBusy(true);
    await act(async () => {
      if (mode === "reply") await respondToTicket(token, id, text);
      else await addTicketNote(token, id, text);
      setText("");
    }, mode === "reply" ? "Reply sent to the student." : "Note saved.");
    setBusy(false);
  }

  return (
    <div className="fixed inset-0 z-40 flex justify-end" role="dialog" aria-modal="true" aria-label="Ticket details">
      <button type="button" className="absolute inset-0 bg-black/40" aria-label="Close ticket" onClick={onClose} />
      <aside className="relative z-10 flex h-full w-full max-w-xl flex-col overflow-y-auto bg-white p-5 shadow-xl">
        <div className="flex items-start justify-between gap-3">
          <div><h2 className="text-lg font-extrabold text-ink-900">{t?.code || "Ticket"}</h2>{t && <p className="text-xs text-ink-400">{t.type} · {t.platform} · opened {when(t.createdAt)}</p>}</div>
          <button type="button" onClick={onClose} aria-label="Close" className="rounded-lg p-1.5 text-ink-600 hover:bg-primary-50"><X className="h-5 w-5" /></button>
        </div>
        {error && <div className="mt-3"><Alert>{error}</Alert></div>}
        {notice && <div className="mt-3"><Alert type="success">{notice}</Alert></div>}
        {!t && !error && <div className="mt-6 h-40 animate-pulse rounded-xl2 bg-primary-50" aria-busy="true" />}

        {t && (
          <div className="mt-4 flex flex-col gap-4">
            <div className="rounded-xl2 bg-primary-50/60 p-3">
              <p className="text-xs font-semibold text-ink-600">{t.student ? `${t.student.fullName} (@${t.student.username})` : "Unknown student"}{t.rating ? ` · rated ${t.rating}/5` : ""}</p>
              <p className="mt-1 whitespace-pre-wrap text-sm text-ink-900">{t.message}</p>
            </div>

            <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
              <label className="text-xs font-semibold text-ink-600">Status
                <Select className="mt-1" value={t.status} onChange={(e) => act(() => updateFeedbackStatus(token, id, e.target.value), "Status updated.")}>
                  {STATUSES.map((s) => <option key={s} value={s}>{s === "InReview" ? "In review" : s}</option>)}
                </Select>
              </label>
              <label className="text-xs font-semibold text-ink-600">Priority
                <Select className="mt-1" value={t.priority} onChange={(e) => act(() => updateTicket(token, id, { priority: e.target.value }), "Priority updated.")}>
                  {PRIORITIES.map((p) => <option key={p}>{p}</option>)}
                </Select>
              </label>
              <label className="text-xs font-semibold text-ink-600">Owner
                <Select className="mt-1" value={t.assignedToId || ""} onChange={(e) => act(() => updateTicket(token, id, { assignee: e.target.value || "none" }), "Owner updated.")}>
                  <option value="">Unassigned</option>
                  {agents.map((a) => <option key={a.id} value={a.id}>{a.fullName}</option>)}
                </Select>
              </label>
            </div>
            <button type="button" className="self-start text-xs font-semibold text-secondary-600 hover:underline" onClick={() => act(() => updateTicket(token, id, { assignee: "me" }), "Assigned to you.")}>Assign to me</button>
            {t.resolvedAt && <p className="text-xs text-ink-400">Closed {when(t.resolvedAt)}</p>}

            <section>
              <h3 className="mb-2 text-sm font-extrabold text-ink-900">Timeline</h3>
              {t.timeline.length === 0 ? <p className="text-xs text-ink-400">No activity yet.</p> : (
                <ol className="space-y-2">
                  {t.timeline.map((e) => (
                    <li key={e.id} className={`rounded-xl2 p-3 text-xs ${e.kind === "Response" ? "bg-mint-50" : e.kind === "Note" ? "bg-accent-50/60" : "bg-primary-50/50"}`}>
                      <p className="font-semibold text-ink-900">{e.kind === "Response" ? "Reply to student" : e.kind === "Note" ? "Internal note" : "Update"} <span className="font-normal text-ink-400">· {e.by} · {when(e.at)}</span></p>
                      <p className="mt-0.5 whitespace-pre-wrap text-ink-600">{e.body}</p>
                    </li>
                  ))}
                </ol>
              )}
            </section>

            <section className="rounded-xl2 border border-primary-100 p-3">
              <div className="mb-2 flex gap-1">
                {[["reply", "Reply to student", MessageSquareReply], ["note", "Internal note", StickyNote]].map(([k, l, Icon]) => (
                  <button key={k} type="button" onClick={() => setMode(k)} className={`flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-semibold ${mode === k ? "bg-primary-600 text-white" : "bg-primary-50 text-primary-600"}`}><Icon className="h-3.5 w-3.5" />{l}</button>
                ))}
              </div>
              <TextArea rows={3} maxLength={mode === "reply" ? 500 : 2000} value={text} onChange={(e) => setText(e.target.value)} aria-label={mode === "reply" ? "Reply" : "Note"}
                placeholder={mode === "reply" ? "The student receives this as a notification." : "Only admins can see internal notes."} />
              <div className="mt-2 flex items-center justify-between">
                <span className="text-[11px] text-ink-400">{text.length}/{mode === "reply" ? 500 : 2000}</span>
                <Button isLoading={busy} disabled={!text.trim()} onClick={submitText}>{mode === "reply" ? "Send reply" : "Save note"}</Button>
              </div>
            </section>
          </div>
        )}
      </aside>
    </div>
  );
}
