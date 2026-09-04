import { useEffect, useState } from "react";
import { Eye, PlayCircle, Trash2, User, AlertTriangle, CheckCircle2 } from "lucide-react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useAdminAuth } from "../context/AdminAuthContext";
import { listPapers, deletePaper } from "../api/papers";
import { API_BASE_URL } from "../../api/client";
import { PageHeader, Card, Button, Select, Alert, StatusBadge, friendlyError } from "../components/AdminUI";

const STATUS_FILTERS = ["", "Draft", "PendingReview", "Published"];
const STATUS_FILTER_LABELS = { "": "All statuses", Draft: "Draft", PendingReview: "Pending Review", Published: "Published" };

function logoSrc(logoUrl) {
  if (!logoUrl) return null;
  return logoUrl.startsWith("http") ? logoUrl : `${API_BASE_URL}${logoUrl}`;
}

// A paper only shows the "Start Paper" button to students once it's BOTH configured
// (durationMinutes/negativeMarkingRatio/requiredQuestionCount all set via Practice Settings) AND
// complete (enough questions mapped to satisfy requiredQuestionCount) -- see PapersController's
// MapToDto/IsConfiguredForPractice+IsComplete and StudentPapersController.Start. Publishing a paper
// does NOT require either of those, so a paper can go live and sit there un-attemptable with no
// error anywhere -- this badge is the only place that surfaces it.
function PracticeReadinessBadge({ paper }) {
  if (!paper.isConfiguredForPractice) {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-amber-50 px-2 py-0.5 text-[11px] font-bold text-amber-700">
        <AlertTriangle className="h-3 w-3" strokeWidth={2.5} />
        Not configured for practice
      </span>
    );
  }
  if (!paper.isComplete) {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-amber-50 px-2 py-0.5 text-[11px] font-bold text-amber-700">
        <AlertTriangle className="h-3 w-3" strokeWidth={2.5} />
        Incomplete ({paper.questionCount}/{paper.requiredQuestionCount} questions)
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-green-50 px-2 py-0.5 text-[11px] font-bold text-green-700">
      <CheckCircle2 className="h-3 w-3" strokeWidth={2.5} />
      Attemptable
    </span>
  );
}

export default function UploadedPapers() {
  const navigate = useNavigate();
  const { token, hasPermission } = useAdminAuth();
  const [searchParams] = useSearchParams();
  const initialStatus = STATUS_FILTERS.includes(searchParams.get("status")) ? searchParams.get("status") : "";
  const [status, setStatus] = useState(initialStatus);
  const [mineOnly, setMineOnly] = useState(false);
  const [notAttemptableOnly, setNotAttemptableOnly] = useState(false);
  const [papers, setPapers] = useState([]);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState(null);
  const [deletingId, setDeletingId] = useState(null);
  const [deleteError, setDeleteError] = useState(null);

  useEffect(() => {
    refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status, mineOnly]);

  function refresh() {
    setIsLoading(true);
    setLoadError(null);
    listPapers(token, { status: status || undefined, mine: mineOnly || undefined, pageSize: 50 })
      .then((res) => setPapers(res.items))
      .catch((err) => setLoadError(err.message))
      .finally(() => setIsLoading(false));
  }

  async function handleDelete(paperId) {
    setDeleteError(null);
    setDeletingId(paperId);
    try {
      await deletePaper(token, paperId);
      setPapers((prev) => prev.filter((p) => p.id !== paperId));
    } catch (err) {
      setDeleteError(friendlyError(err));
    } finally {
      setDeletingId(null);
    }
  }

  // Client-side only (the "not attemptable" state isn't a query param the API supports) -- applied
  // on top of whatever page of results came back, so it's a quick spot-check, not an exhaustive
  // audit across every page. Only meaningful for Published papers, since Draft/PendingReview papers
  // aren't attemptable by students yet regardless of practice config.
  const visiblePapers = notAttemptableOnly
    ? papers.filter((p) => p.status === "Published" && !(p.isConfiguredForPractice && p.isComplete))
    : papers;

  return (
    <div>
      <PageHeader title="Uploaded Papers" subtitle="Every PYQ paper, at every stage of the review pipeline." />

      <div className="p-6">
        <div className="mb-4 flex flex-wrap items-center gap-3">
          <Select value={status} onChange={(e) => setStatus(e.target.value)} className="w-auto">
            {STATUS_FILTERS.map((s) => <option key={s} value={s}>{STATUS_FILTER_LABELS[s]}</option>)}
          </Select>
          <label className="flex items-center gap-2 text-sm font-medium text-ink-600">
            <input type="checkbox" checked={mineOnly} onChange={(e) => setMineOnly(e.target.checked)} className="h-4 w-4 accent-primary-600" />
            Only my papers
          </label>
          <label className="flex items-center gap-2 text-sm font-medium text-ink-600">
            <input
              type="checkbox"
              checked={notAttemptableOnly}
              onChange={(e) => setNotAttemptableOnly(e.target.checked)}
              className="h-4 w-4 accent-primary-600"
            />
            Not attemptable only (Published, this page)
          </label>
        </div>

        {isLoading && <p className="text-sm text-ink-400">Loading papers…</p>}
        {loadError && <Alert>{loadError}</Alert>}
        {deleteError && <Alert>{deleteError}</Alert>}
        {!isLoading && !loadError && visiblePapers.length === 0 && (
          <p className="text-sm text-ink-400">
            {notAttemptableOnly && papers.length > 0 ? "Every paper on this page is attemptable." : "No papers here yet."}
          </p>
        )}

        <div className="flex flex-col gap-3">
          {visiblePapers.map((paper) => (
            <Card key={paper.id} className="flex flex-wrap items-center justify-between gap-4">
              <div className="flex min-w-0 items-center gap-3">
                {logoSrc(paper.examLogoUrl) ? (
                  <img src={logoSrc(paper.examLogoUrl)} alt="" className="h-10 w-10 shrink-0 rounded-lg object-cover" />
                ) : (
                  <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary-50 text-xs font-bold text-primary-600">
                    {paper.examName.slice(0, 2).toUpperCase()}
                  </span>
                )}
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <h3 className="text-sm font-bold text-ink-900">
                      {paper.examName} · {paper.year}
                    </h3>
                    {paper.tier && (
                      <span className="rounded-full bg-primary-50 px-2 py-0.5 text-[11px] font-bold text-primary-600">{paper.tier}</span>
                    )}
                    {paper.shift && (
                      <span className="rounded-full bg-secondary-50 px-2 py-0.5 text-[11px] font-bold text-secondary-600">{paper.shift}</span>
                    )}
                    <StatusBadge status={paper.status} />
                    {paper.status === "Published" && <PracticeReadinessBadge paper={paper} />}
                  </div>
                  <p className="mt-0.5 text-xs text-ink-400">
                    {paper.language}
                    {paper.examDate ? ` · ${paper.examDate}` : ""}
                    {paper.paperLabel ? ` · ${paper.paperLabel}` : ""}
                    {paper.paperCode ? ` · ${paper.paperCode}` : ""} · {paper.questionCount} question{paper.questionCount === 1 ? "" : "s"}
                    {" · "}
                    <span className="inline-flex items-center gap-1"><User className="inline h-3 w-3" strokeWidth={2.25} />{paper.createdByAdminName}</span>
                  </p>
                  {paper.status === "Draft" && paper.rejectionReason && (
                    <p className="mt-1 text-xs font-medium text-red-600">Rejected: {paper.rejectionReason}</p>
                  )}
                </div>
              </div>

              <div className="flex shrink-0 gap-2">
                {paper.status === "Draft" && hasPermission("UploadPaper") && (
                  <Button onClick={() => navigate(`/admin/upload?resume=${paper.id}`)}>
                    <PlayCircle className="h-4 w-4" strokeWidth={2.25} />
                    Continue
                  </Button>
                )}
                <Button variant="secondary" onClick={() => navigate(`/admin/papers/${paper.id}`)}>
                  <Eye className="h-4 w-4" strokeWidth={2.25} />
                  View
                </Button>
                {hasPermission("DeletePaper") && (
                  <Button
                    variant="danger"
                    isLoading={deletingId === paper.id}
                    onClick={() => {
                      if (window.confirm(`Delete this paper and all ${paper.questionCount} of its questions? This can't be undone.`)) {
                        handleDelete(paper.id);
                      }
                    }}
                  >
                    <Trash2 className="h-4 w-4" strokeWidth={2.25} />
                  </Button>
                )}
              </div>
            </Card>
          ))}
        </div>
      </div>
    </div>
  );
}
