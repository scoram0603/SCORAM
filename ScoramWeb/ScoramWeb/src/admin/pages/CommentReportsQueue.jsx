import { useEffect, useState } from "react";
import { XCircle, Trash2, Flag } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { listPendingCommentReports, dismissCommentReport, removeReportedComment } from "../api/discussions";
import { useAdminAuth } from "../context/AdminAuthContext";
import { PageHeader, Card, Button, Alert, friendlyError } from "../components/AdminUI";

export default function CommentReportsQueue() {
  const navigate = useNavigate();
  const { token } = useAdminAuth();
  const [result, setResult] = useState(null);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState(null);
  const [busyId, setBusyId] = useState(null);
  const [actionError, setActionError] = useState(null);

  function refresh() {
    setIsLoading(true);
    setLoadError(null);
    listPendingCommentReports(token, { page: 1, pageSize: 50 })
      .then(setResult)
      .catch((err) => setLoadError(err.message))
      .finally(() => setIsLoading(false));
  }

  useEffect(refresh, [token]);

  async function handleDismiss(reportId) {
    setBusyId(reportId);
    setActionError(null);
    try {
      await dismissCommentReport(token, reportId);
      setResult((prev) => ({ ...prev, items: prev.items.filter((r) => r.reportId !== reportId) }));
    } catch (err) {
      setActionError(friendlyError(err));
    } finally {
      setBusyId(null);
    }
  }

  async function handleRemove(reportId) {
    if (!window.confirm("Remove this comment (and any replies under it)? This can't be undone.")) return;
    setBusyId(reportId);
    setActionError(null);
    try {
      await removeReportedComment(token, reportId);
      setResult((prev) => ({ ...prev, items: prev.items.filter((r) => r.reportId !== reportId) }));
    } catch (err) {
      setActionError(friendlyError(err));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div>
      <PageHeader title="Reported Comments" subtitle="Student-reported discussion comments waiting on a moderation decision." />

      <div className="p-6">
        {isLoading && <p className="text-sm text-ink-400">Loading…</p>}
        {loadError && <Alert>{loadError}</Alert>}
        {actionError && <Alert>{actionError}</Alert>}

        {!isLoading && !loadError && result?.items.length === 0 && (
          <Card className="flex flex-col items-center gap-2 py-10 text-center">
            <Flag className="h-8 w-8 text-ink-400" strokeWidth={1.75} />
            <p className="text-sm text-ink-400">No open reports right now.</p>
          </Card>
        )}

        {!isLoading && !loadError && result?.items.length > 0 && (
          <div className="flex flex-col gap-3">
            {result.items.map((r) => (
              <Card key={r.reportId}>
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <button
                      type="button"
                      onClick={() => navigate(r.isQuestionBank ? `/question-bank/${r.questionBankQuestionId}` : `/questions/${r.questionId}`)}
                      className="text-xs font-semibold text-secondary-500 hover:underline"
                    >
                      {r.isQuestionBank && <span className="mr-1 rounded bg-primary-50 px-1.5 py-0.5 text-[10px] font-bold text-primary-600">QB</span>}
                      {r.questionTextSnippet}
                    </button>
                    <p className="mt-1 whitespace-pre-line text-sm text-ink-900">{r.commentText}</p>
                    <p className="mt-1 text-xs text-ink-400">by {r.authorName}</p>
                    <p className="mt-2 rounded-lg bg-red-50 px-3 py-2 text-xs text-red-600">
                      Reported by {r.reportedByName}{r.reason ? `: "${r.reason}"` : " (no reason given)"}
                    </p>
                  </div>
                  <div className="flex shrink-0 gap-2">
                    <Button variant="secondary" isLoading={busyId === r.reportId} onClick={() => handleDismiss(r.reportId)}>
                      <XCircle className="h-4 w-4" strokeWidth={2.25} />
                      Dismiss
                    </Button>
                    <Button isLoading={busyId === r.reportId} onClick={() => handleRemove(r.reportId)}>
                      <Trash2 className="h-4 w-4" strokeWidth={2.25} />
                      Remove
                    </Button>
                  </div>
                </div>
              </Card>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
