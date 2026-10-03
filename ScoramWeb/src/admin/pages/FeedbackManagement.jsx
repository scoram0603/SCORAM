import { useEffect, useRef, useState } from "react";
import { Search, Star, X, Loader2, ChevronLeft, ChevronRight, Inbox } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { listFeedback, updateFeedbackStatus } from "../api/feedback";
import { PageHeader, Card, Button, Alert, Select, TextInput, friendlyError } from "../components/AdminUI";

const PAGE_SIZE = 20;

const TYPE_LABELS = {
  Suggestion: "Suggestion",
  Improvement: "Improvement",
  BugReport: "Bug Report",
  ContentIssue: "Content Issue",
  UiUx: "UI/UX",
  Other: "Other",
};
const STATUS_LABELS = { New: "New", InReview: "In Review", Resolved: "Resolved", Rejected: "Rejected" };
const STATUS_STYLES = {
  New: "bg-accent-50 text-accent-600",
  InReview: "bg-secondary-50 text-secondary-600",
  Resolved: "bg-mint-50 text-mint-500",
  Rejected: "bg-ink-400/10 text-ink-600",
};
const PLATFORM_LABELS = { Web: "Web", Flutter: "Mobile app", Other: "Other" };

function formatDate(iso) {
  return new Date(iso).toLocaleString("en-IN", { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" });
}

function Badge({ value, labels, styles }) {
  return (
    <span className={`whitespace-nowrap rounded-full px-2.5 py-1 text-xs font-semibold ${styles?.[value] || "bg-primary-50 text-primary-600"}`}>
      {labels[value] || value}
    </span>
  );
}

function Stars({ rating }) {
  if (!rating) return <span className="text-ink-400">—</span>;
  return (
    <span className="flex items-center gap-0.5 text-amber-400" aria-label={`${rating} out of 5`}>
      {[1, 2, 3, 4, 5].map((n) => <Star key={n} className="h-3.5 w-3.5" strokeWidth={2} fill={n <= rating ? "currentColor" : "none"} />)}
    </span>
  );
}

// Admin > Feedback: everything students send from the floating Feedback button. Shows only what's
// needed to handle it (display name + username -- never email/phone/credentials; the API doesn't
// return them). Search + type/status/platform/date filters, server-side paging, and a detail panel
// where the status is changed with an explicit "Save status" -- a stray click on the dropdown alone
// never changes anything.
export default function FeedbackManagement() {
  const { token } = useAdminAuth();

  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [type, setType] = useState("");
  const [status, setStatus] = useState("");
  const [platform, setPlatform] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [page, setPage] = useState(1);

  const [data, setData] = useState({ items: [], totalCount: 0 });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [selected, setSelected] = useState(null);

  // Debounce the search box; any filter change goes back to page 1.
  useEffect(() => {
    const handle = setTimeout(() => { setSearch(searchInput.trim()); setPage(1); }, 350);
    return () => clearTimeout(handle);
  }, [searchInput]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    listFeedback(token, { search, type, status, platform, from, to, page, pageSize: PAGE_SIZE }, { signal: controller.signal })
      .then((res) => setData(res))
      .catch((err) => { if (err.name !== "AbortError") setError(friendlyError(err)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [token, search, type, status, platform, from, to, page]);

  function changeFilter(setter) {
    return (e) => { setter(e.target.value); setPage(1); };
  }

  function clearFilters() {
    setSearchInput(""); setSearch(""); setType(""); setStatus(""); setPlatform(""); setFrom(""); setTo(""); setPage(1);
  }

  function handleStatusSaved(updated) {
    setData((prev) => ({ ...prev, items: prev.items.map((f) => (f.id === updated.id ? updated : f)) }));
    setSelected(updated);
  }

  const totalPages = Math.max(1, Math.ceil(data.totalCount / PAGE_SIZE));
  const hasFilters = search || type || status || platform || from || to;

  return (
    <div>
      <PageHeader title="Feedback" subtitle="Suggestions, improvements and bug reports from students" />

      <div className="p-6">
        <Card>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-6">
            <label className="relative block sm:col-span-2">
              <span className="sr-only">Search feedback</span>
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" strokeWidth={2} />
              <TextInput
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                placeholder="Search message, user or FDB-00042"
                className="pl-9"
              />
            </label>
            <Select aria-label="Filter by type" value={type} onChange={changeFilter(setType)}>
              <option value="">All types</option>
              {Object.entries(TYPE_LABELS).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
            </Select>
            <Select aria-label="Filter by status" value={status} onChange={changeFilter(setStatus)}>
              <option value="">All statuses</option>
              {Object.entries(STATUS_LABELS).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
            </Select>
            <Select aria-label="Filter by platform" value={platform} onChange={changeFilter(setPlatform)}>
              <option value="">All platforms</option>
              {Object.entries(PLATFORM_LABELS).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
            </Select>
            <div className="flex items-center gap-2 sm:col-span-2 lg:col-span-1">
              <TextInput type="date" aria-label="From date" value={from} max={to || undefined} onChange={changeFilter(setFrom)} />
              <span className="text-xs text-ink-400">to</span>
              <TextInput type="date" aria-label="To date" value={to} min={from || undefined} onChange={changeFilter(setTo)} />
            </div>
          </div>
          {hasFilters && (
            <button type="button" onClick={clearFilters} className="mt-3 flex items-center gap-1 text-xs font-semibold text-secondary-500 hover:text-secondary-600">
              <X className="h-3.5 w-3.5" strokeWidth={2.5} /> Clear filters
            </button>
          )}
        </Card>

        {error && <div className="mt-4"><Alert>{error}</Alert></div>}

        <Card className="mt-4 overflow-hidden !p-0">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[920px] text-left text-sm">
              <thead className="border-b border-primary-100 bg-surface text-xs font-bold uppercase tracking-wide text-ink-400">
                <tr>
                  <th className="px-4 py-3">ID</th>
                  <th className="px-4 py-3">User</th>
                  <th className="px-4 py-3">Type</th>
                  <th className="px-4 py-3">Message</th>
                  <th className="px-4 py-3">Rating</th>
                  <th className="px-4 py-3">Platform</th>
                  <th className="px-4 py-3">Source</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Created</th>
                  <th className="px-4 py-3 text-right">Actions</th>
                </tr>
              </thead>
              <tbody>
                {loading && data.items.length === 0 && (
                  <tr><td colSpan={10} className="px-4 py-12 text-center text-ink-400"><Loader2 className="mx-auto h-5 w-5 animate-spin" /></td></tr>
                )}
                {!loading && !error && data.items.length === 0 && (
                  <tr>
                    <td colSpan={10} className="px-4 py-12 text-center text-ink-400">
                      <Inbox className="mx-auto mb-2 h-6 w-6" strokeWidth={1.75} />
                      {hasFilters ? "No feedback matches these filters." : "No feedback yet."}
                    </td>
                  </tr>
                )}
                {data.items.map((f) => (
                  <tr key={f.id} className={`border-b border-primary-50 hover:bg-surface/60 ${loading ? "opacity-60" : ""}`}>
                    <td className="whitespace-nowrap px-4 py-3 font-mono text-xs font-semibold text-ink-600">{f.feedbackCode}</td>
                    <td className="px-4 py-3">
                      <span className="block font-semibold text-ink-900">{f.userFullName}</span>
                      {f.username && <span className="block text-xs text-ink-400">@{f.username}</span>}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-ink-600">{TYPE_LABELS[f.feedbackType] || f.feedbackType}</td>
                    <td className="max-w-xs px-4 py-3 text-ink-600"><span className="line-clamp-2">{f.messagePreview}</span></td>
                    <td className="px-4 py-3"><Stars rating={f.rating} /></td>
                    <td className="whitespace-nowrap px-4 py-3 text-ink-600">{PLATFORM_LABELS[f.platform] || f.platform}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-ink-600">{f.source || "—"}</td>
                    <td className="px-4 py-3"><Badge value={f.status} labels={STATUS_LABELS} styles={STATUS_STYLES} /></td>
                    <td className="whitespace-nowrap px-4 py-3 text-xs text-ink-600">{formatDate(f.createdAt)}</td>
                    <td className="px-4 py-3 text-right">
                      <Button variant="secondary" className="!px-3 !py-1.5 text-xs" onClick={() => setSelected(f)}>View</Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-2 border-t border-primary-100 px-4 py-3 text-xs text-ink-600">
            <span>{data.totalCount} feedback item{data.totalCount === 1 ? "" : "s"}</span>
            <div className="flex items-center gap-2">
              <Button variant="ghost" className="!px-2.5 !py-1.5 text-xs" disabled={page <= 1 || loading} onClick={() => setPage((p) => p - 1)}>
                <ChevronLeft className="h-4 w-4" strokeWidth={2.5} /> Prev
              </Button>
              <span className="font-semibold">Page {page} of {totalPages}</span>
              <Button variant="ghost" className="!px-2.5 !py-1.5 text-xs" disabled={page >= totalPages || loading} onClick={() => setPage((p) => p + 1)}>
                Next <ChevronRight className="h-4 w-4" strokeWidth={2.5} />
              </Button>
            </div>
          </div>
        </Card>
      </div>

      {selected && <FeedbackDetail token={token} feedback={selected} onClose={() => setSelected(null)} onSaved={handleStatusSaved} />}
    </div>
  );
}

// Detail panel. The status select only stages a choice; "Save status" (disabled until it differs
// from the saved status) is what actually sends it.
function FeedbackDetail({ token, feedback, onClose, onSaved }) {
  const [newStatus, setNewStatus] = useState(feedback.status);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);
  const [saved, setSaved] = useState(false);
  const savingRef = useRef(false);

  useEffect(() => {
    setNewStatus(feedback.status);
    setSaved(false);
    setError(null);
  }, [feedback.id, feedback.status]);

  useEffect(() => {
    function onKey(e) { if (e.key === "Escape") onClose(); }
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  async function handleSave() {
    if (savingRef.current || newStatus === feedback.status) return;
    savingRef.current = true;
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      const updated = await updateFeedbackStatus(token, feedback.id, newStatus);
      setSaved(true);
      onSaved(updated);
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex justify-end" role="dialog" aria-modal="true" aria-label="Feedback details">
      <div className="absolute inset-0 bg-ink-900/40" onClick={onClose} />
      <div className="relative flex h-full w-full max-w-md flex-col overflow-y-auto bg-white shadow-cardHover">
        <div className="flex items-center justify-between border-b border-primary-100 px-5 py-4">
          <div>
            <h2 className="text-base font-extrabold text-ink-900">{feedback.feedbackCode}</h2>
            <p className="text-xs text-ink-400">{formatDate(feedback.createdAt)}</p>
          </div>
          <button type="button" onClick={onClose} aria-label="Close" className="flex h-8 w-8 items-center justify-center rounded-full text-ink-400 hover:bg-surface">
            <X className="h-4 w-4" strokeWidth={2.25} />
          </button>
        </div>

        <div className="space-y-5 px-5 py-5">
          <dl className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
            <Field label="User">{feedback.userFullName}{feedback.username && <span className="block text-xs text-ink-400">@{feedback.username}</span>}</Field>
            <Field label="Feedback type">{TYPE_LABELS[feedback.feedbackType] || feedback.feedbackType}</Field>
            <Field label="Platform">{PLATFORM_LABELS[feedback.platform] || feedback.platform}</Field>
            <Field label="Source">{feedback.source || "—"}</Field>
            <Field label="Rating"><Stars rating={feedback.rating} /></Field>
            <Field label="Current status"><Badge value={feedback.status} labels={STATUS_LABELS} styles={STATUS_STYLES} /></Field>
          </dl>

          <div>
            <p className="text-xs font-bold uppercase tracking-wide text-ink-400">Message</p>
            <p className="mt-1.5 whitespace-pre-wrap break-words rounded-xl2 bg-surface p-3.5 text-sm text-ink-900">{feedback.message}</p>
          </div>

          <div className="rounded-xl2 border border-primary-100 p-4">
            <p className="text-xs font-bold uppercase tracking-wide text-ink-400">Update status</p>
            <div className="mt-2 flex items-center gap-2">
              <Select aria-label="New status" value={newStatus} onChange={(e) => { setNewStatus(e.target.value); setSaved(false); }} disabled={saving}>
                {Object.entries(STATUS_LABELS).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
              </Select>
              <Button isLoading={saving} disabled={newStatus === feedback.status} onClick={handleSave} className="shrink-0">
                Save status
              </Button>
            </div>
            {error && <div className="mt-3"><Alert>{error}</Alert></div>}
            {saved && <div className="mt-3"><Alert type="success">Status updated.</Alert></div>}
          </div>
        </div>
      </div>
    </div>
  );
}

function Field({ label, children }) {
  return (
    <div>
      <dt className="text-xs font-bold uppercase tracking-wide text-ink-400">{label}</dt>
      <dd className="mt-0.5 text-ink-900">{children}</dd>
    </div>
  );
}
