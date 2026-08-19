import { useEffect, useState } from "react";
import { CheckCircle2, Trash2, Lightbulb } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { listPendingSolutions, approveSolution, removeSolution } from "../api/solutions";
import { useAdminAuth } from "../context/AdminAuthContext";
import { PageHeader, Card, Button, Alert, friendlyError } from "../components/AdminUI";

export default function SolutionsQueue() {
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
    listPendingSolutions(token, { page: 1, pageSize: 50 })
      .then(setResult)
      .catch((err) => setLoadError(err.message))
      .finally(() => setIsLoading(false));
  }

  useEffect(refresh, [token]);

  async function handleApprove(id) {
    setBusyId(id);
    setActionError(null);
    try {
      await approveSolution(token, id);
      setResult((prev) => ({ ...prev, items: prev.items.filter((s) => s.id !== id) }));
    } catch (err) {
      setActionError(friendlyError(err));
    } finally {
      setBusyId(null);
    }
  }

  async function handleReject(id) {
    const reason = window.prompt("Optional: why is this being rejected? (shown in the audit log only)");
    if (reason === null) return; // cancelled
    setBusyId(id);
    setActionError(null);
    try {
      await removeSolution(token, id, reason || undefined);
      setResult((prev) => ({ ...prev, items: prev.items.filter((s) => s.id !== id) }));
    } catch (err) {
      setActionError(friendlyError(err));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div>
      <PageHeader title="Solutions Queue" subtitle="Student-submitted solutions waiting on approval before they go live." />

      <div className="p-6">
        {isLoading && <p className="text-sm text-ink-400">Loading…</p>}
        {loadError && <Alert>{loadError}</Alert>}
        {actionError && <Alert>{actionError}</Alert>}

        {!isLoading && !loadError && result?.items.length === 0 && (
          <Card className="flex flex-col items-center gap-2 py-10 text-center">
            <Lightbulb className="h-8 w-8 text-ink-400" strokeWidth={1.75} />
            <p className="text-sm text-ink-400">Nothing waiting on review right now.</p>
          </Card>
        )}

        {!isLoading && !loadError && result?.items.length > 0 && (
          <div className="flex flex-col gap-3">
            {result.items.map((s) => (
              <Card key={s.id}>
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <button
                      type="button"
                      onClick={() => navigate(s.isQuestionBank ? `/question-bank/${s.questionBankQuestionId}` : `/questions/${s.questionId}`)}
                      className="text-xs font-semibold text-secondary-500 hover:underline"
                    >
                      {s.isQuestionBank && <span className="mr-1 rounded bg-primary-50 px-1.5 py-0.5 text-[10px] font-bold text-primary-600">QB</span>}
                      {s.examName} — {s.questionTextSnippet}
                    </button>
                    <h3 className="mt-1 text-sm font-bold text-ink-900">{s.title}</h3>
                    <p className="mt-0.5 text-xs text-ink-400">
                      {s.solutionType} · by {s.submittedByName}
                    </p>
                    <p className="mt-2 whitespace-pre-line text-sm text-ink-600">{s.solutionText}</p>
                    {s.imageUrl && (
                      <img src={s.imageUrl} alt="" className="mt-2 max-h-48 rounded-lg border border-primary-100" />
                    )}
                  </div>
                  <div className="flex shrink-0 gap-2">
                    <Button isLoading={busyId === s.id} onClick={() => handleApprove(s.id)}>
                      <CheckCircle2 className="h-4 w-4" strokeWidth={2.25} />
                      Approve
                    </Button>
                    <Button variant="secondary" isLoading={busyId === s.id} onClick={() => handleReject(s.id)}>
                      <Trash2 className="h-4 w-4" strokeWidth={2.25} />
                      Reject
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
