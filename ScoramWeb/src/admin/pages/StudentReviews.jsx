import { useEffect, useState } from "react";
import { Search, X, Loader2, Inbox, CheckCircle2, EyeOff, XCircle, Trash2, Undo2 } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { listReviews, setReviewStatus, deleteReview } from "../api/reviews";
import { PageHeader, Card, Button, Alert, Select, TextInput, friendlyError } from "../components/AdminUI";
import ConfirmDialog from "../components/ConfirmDialog";
import Pager from "../components/Pager";
import { StarDisplay } from "../../components/reviews/StarRating";

const PAGE_SIZE = 20;
const STATUS_LABELS = { Pending: "Pending", Approved: "Approved", Rejected: "Rejected", Hidden: "Hidden" };
const STATUS_STYLES = {
  Pending: "bg-accent-50 text-accent-600",
  Approved: "bg-mint-50 text-mint-500",
  Rejected: "bg-red-50 text-red-600",
  Hidden: "bg-ink-400/10 text-ink-600",
};

function formatDate(iso) {
  return new Date(iso).toLocaleString("en-IN", { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" });
}

function SummaryTile({ label, value, active, onClick }) {
  return (
    <button type="button" onClick={onClick} aria-pressed={active}
      className={`rounded-xl2 border px-4 py-3 text-left transition-colors ${active ? "border-secondary-500 bg-secondary-50" : "border-primary-100 bg-white hover:bg-primary-50"}`}>
      <span className="block text-xs font-semibold text-ink-400">{label}</span>
      <span className="block text-xl font-extrabold text-ink-900">{value}</span>
    </button>
  );
}

// Admin > Student Reviews: moderate the public "Rate SCORAM" reviews. Nothing reaches the landing page until it is
// Approved here, and the API only ever lists Approved reviews rated 3+ publicly -- a 1-2 star review stays in this
// list for internal tracking whatever its status. Every action is audited server-side (rating + id, never the text).
export default function StudentReviews() {
  const { token } = useAdminAuth();

  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("Pending");
  const [rating, setRating] = useState("");
  const [page, setPage] = useState(1);
  const [reloadKey, setReloadKey] = useState(0);

  const [data, setData] = useState({ items: [], totalCount: 0, summary: null });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [notice, setNotice] = useState(null);
  const [busyId, setBusyId] = useState(null);
  const [confirm, setConfirm] = useState(null); // { type: "delete" | "reject" | "hide", review }

  useEffect(() => {
    const handle = setTimeout(() => { setSearch(searchInput.trim()); setPage(1); }, 350);
    return () => clearTimeout(handle);
  }, [searchInput]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    listReviews(token, { status, rating, search, page, pageSize: PAGE_SIZE }, { signal: controller.signal })
      .then((res) => setData(res))
      .catch((err) => { if (err.name !== "AbortError") setError(friendlyError(err)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [token, status, rating, search, page, reloadKey]);

  async function changeStatus(review, newStatus, note) {
    setBusyId(review.id);
    setNotice(null);
    try {
      await setReviewStatus(token, review.id, newStatus, note);
      setNotice({ type: "success", message: `Review ${newStatus === "Approved" ? "approved" : newStatus.toLowerCase()}.` });
      setReloadKey((k) => k + 1);
    } catch (err) {
      setNotice({ type: "error", message: friendlyError(err) });
    } finally {
      setBusyId(null);
    }
  }

  async function confirmAction() {
    const { type, review } = confirm;
    if (type === "delete") {
      await deleteReview(token, review.id);
      setNotice({ type: "success", message: "Review deleted." });
      setReloadKey((k) => k + 1);
    } else {
      await setReviewStatus(token, review.id, type === "reject" ? "Rejected" : "Hidden");
      setNotice({ type: "success", message: type === "reject" ? "Review rejected." : "Review hidden." });
      setReloadKey((k) => k + 1);
    }
    setConfirm(null);
  }

  function pickStatus(value) { setStatus(value); setPage(1); }

  const s = data.summary;
  const hasFilters = search || rating || status !== "Pending";

  return (
    <div>
      <PageHeader title="Student Reviews" subtitle="Approve, reject or hide the reviews students write on the website" />

      <div className="space-y-4 p-6">
        {s && (
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
            <SummaryTile label="Pending" value={s.pending} active={status === "Pending"} onClick={() => pickStatus("Pending")} />
            <SummaryTile label="Approved" value={s.approved} active={status === "Approved"} onClick={() => pickStatus("Approved")} />
            <SummaryTile label="Rejected" value={s.rejected} active={status === "Rejected"} onClick={() => pickStatus("Rejected")} />
            <SummaryTile label="Hidden" value={s.hidden} active={status === "Hidden"} onClick={() => pickStatus("Hidden")} />
            <SummaryTile label="All reviews" value={s.total} active={status === ""} onClick={() => pickStatus("")} />
            <div className="rounded-xl2 border border-primary-100 bg-white px-4 py-3">
              <span className="block text-xs font-semibold text-ink-400">Shown on landing page</span>
              <span className="block text-xl font-extrabold text-ink-900">{s.publicOnLanding}</span>
              <span className="block text-[11px] text-ink-400">Approved, 3★ and above</span>
            </div>
          </div>
        )}
        {s?.averageRatingAll != null && (
          <p className="text-xs text-ink-400">Average of all {s.total} stored ratings (internal only, not published): <span className="font-semibold text-ink-600">{s.averageRatingAll} / 5</span></p>
        )}

        <Card>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <label className="relative block">
              <span className="sr-only">Search reviews</span>
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" strokeWidth={2} />
              <TextInput value={searchInput} onChange={(e) => setSearchInput(e.target.value)} placeholder="Search text, name or username" className="pl-9" />
            </label>
            <Select aria-label="Filter by status" value={status} onChange={(e) => pickStatus(e.target.value)}>
              <option value="">All statuses</option>
              {Object.entries(STATUS_LABELS).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
            </Select>
            <Select aria-label="Filter by rating" value={rating} onChange={(e) => { setRating(e.target.value); setPage(1); }}>
              <option value="">All ratings</option>
              {[5, 4, 3, 2, 1].map((n) => <option key={n} value={n}>{n} star{n > 1 ? "s" : ""}</option>)}
            </Select>
          </div>
          {hasFilters && (
            <button type="button" onClick={() => { setSearchInput(""); setSearch(""); setStatus("Pending"); setRating(""); setPage(1); }}
              className="mt-3 flex items-center gap-1 text-xs font-semibold text-secondary-500 hover:text-secondary-600">
              <X className="h-3.5 w-3.5" strokeWidth={2.5} /> Reset filters
            </button>
          )}
        </Card>

        {notice && <Alert type={notice.type}>{notice.message}</Alert>}
        {error && <Alert type="error">{error}</Alert>}

        {loading && data.items.length === 0 ? (
          <div className="flex justify-center py-16 text-ink-400" role="status" aria-label="Loading reviews"><Loader2 className="h-6 w-6 animate-spin" /></div>
        ) : data.items.length === 0 && !error ? (
          <Card className="flex flex-col items-center gap-2 py-12 text-center text-ink-400">
            <Inbox className="h-8 w-8" strokeWidth={1.5} />
            <p className="text-sm">No reviews match these filters.</p>
          </Card>
        ) : (
          <ul className={`space-y-3 ${loading ? "opacity-60" : ""}`}>
            {data.items.map((r) => (
              <li key={r.id}>
                <Card>
                  <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                    <StarDisplay rating={r.rating} />
                    <span className={`rounded-full px-2.5 py-1 text-xs font-semibold ${STATUS_STYLES[r.status] || ""}`}>{STATUS_LABELS[r.status] || r.status}</span>
                    {r.publicOnLanding && <span className="rounded-full bg-secondary-50 px-2.5 py-1 text-xs font-semibold text-secondary-600">On landing page</span>}
                    {r.status === "Approved" && r.rating < 3 && <span className="text-xs text-ink-400">Approved but under 3★ — not shown on the landing page</span>}
                    <span className="ml-auto text-xs text-ink-400">{formatDate(r.createdAt)}{r.updatedAt !== r.createdAt ? ` · edited ${formatDate(r.updatedAt)}` : ""}</span>
                  </div>

                  <p className="mt-3 whitespace-pre-line break-words text-sm text-ink-900">{r.text}</p>

                  <p className="mt-3 text-xs text-ink-400">
                    By <span className="font-semibold text-ink-600">{r.authorFullName}</span>{r.authorUsername ? ` (@${r.authorUsername})` : ""}
                    {!r.authorActive && " · account inactive"} · Shown publicly as <span className="font-semibold text-ink-600">{r.publicName}</span>
                    {r.moderatedAt && ` · Last moderated ${formatDate(r.moderatedAt)}${r.moderatedBy ? ` by ${r.moderatedBy}` : ""}`}
                  </p>
                  {r.moderationNote && <p className="mt-1 text-xs text-ink-400">Note: {r.moderationNote}</p>}

                  <div className="mt-4 flex flex-wrap gap-2">
                    {r.status !== "Approved" && (
                      <Button variant="secondary" className="px-3 py-1.5 text-xs" isLoading={busyId === r.id} onClick={() => changeStatus(r, "Approved")}>
                        <CheckCircle2 className="h-3.5 w-3.5" strokeWidth={2.5} /> Approve
                      </Button>
                    )}
                    {r.status !== "Rejected" && (
                      <Button variant="ghost" className="px-3 py-1.5 text-xs" disabled={busyId === r.id} onClick={() => setConfirm({ type: "reject", review: r })}>
                        <XCircle className="h-3.5 w-3.5" strokeWidth={2.5} /> Reject
                      </Button>
                    )}
                    {r.status !== "Hidden" && (
                      <Button variant="ghost" className="px-3 py-1.5 text-xs" disabled={busyId === r.id} onClick={() => setConfirm({ type: "hide", review: r })}>
                        <EyeOff className="h-3.5 w-3.5" strokeWidth={2.5} /> Hide
                      </Button>
                    )}
                    {r.status !== "Pending" && (
                      <Button variant="ghost" className="px-3 py-1.5 text-xs" disabled={busyId === r.id} onClick={() => changeStatus(r, "Pending")}>
                        <Undo2 className="h-3.5 w-3.5" strokeWidth={2.5} /> Back to pending
                      </Button>
                    )}
                    <Button variant="danger" className="ml-auto px-3 py-1.5 text-xs" disabled={busyId === r.id} onClick={() => setConfirm({ type: "delete", review: r })}>
                      <Trash2 className="h-3.5 w-3.5" strokeWidth={2.5} /> Delete
                    </Button>
                  </div>
                </Card>
              </li>
            ))}
          </ul>
        )}

        <Pager page={page} pageSize={PAGE_SIZE} totalCount={data.totalCount} onPage={setPage} />
      </div>

      <ConfirmDialog
        open={!!confirm}
        danger={confirm?.type === "delete"}
        title={confirm?.type === "delete" ? "Delete this review?" : confirm?.type === "reject" ? "Reject this review?" : "Hide this review?"}
        message={
          confirm?.type === "delete"
            ? "The review is permanently removed. The student's account and other data are not affected."
            : confirm?.type === "reject"
              ? "It won't be shown publicly. The student can edit and resubmit it."
              : "It is removed from public view and the student can't edit it."
        }
        confirmLabel={confirm?.type === "delete" ? "Delete review" : confirm?.type === "reject" ? "Reject" : "Hide"}
        onConfirm={confirmAction}
        onCancel={() => setConfirm(null)}
      />
    </div>
  );
}
