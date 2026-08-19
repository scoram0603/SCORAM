import { useEffect, useState } from "react";
import { ChevronLeft, ChevronRight, ShieldAlert } from "lucide-react";
import { listAuditLogs } from "../api/auditLogs";
import { useAdminAuth } from "../context/AdminAuthContext";
import { PageHeader, Card, Button, Alert } from "../components/AdminUI";

const PAGE_SIZE = 25;

function formatWhen(iso) {
  return new Date(iso).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" });
}

// "Paper.Publish" -> "Paper · Publish" -- readable without needing a lookup table for every action
// string that gets added over time.
function formatAction(action) {
  return action.replace(".", " · ");
}

export default function AuditLog() {
  const { token } = useAdminAuth();
  const [page, setPage] = useState(1);
  const [result, setResult] = useState(null);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState(null);

  useEffect(() => {
    setIsLoading(true);
    setLoadError(null);
    listAuditLogs(token, { page, pageSize: PAGE_SIZE })
      .then(setResult)
      .catch((err) => setLoadError(err.message))
      .finally(() => setIsLoading(false));
  }, [token, page]);

  const totalPages = result ? Math.max(1, Math.ceil(result.totalCount / PAGE_SIZE)) : 1;

  return (
    <div>
      <PageHeader title="Audit Log" subtitle="Who did what, across papers, questions, and admin accounts." />

      <div className="p-6">
        {isLoading && <p className="text-sm text-ink-400">Loading…</p>}
        {loadError && <Alert>{loadError}</Alert>}

        {!isLoading && !loadError && result?.items.length === 0 && (
          <Card className="flex flex-col items-center gap-2 py-10 text-center">
            <ShieldAlert className="h-8 w-8 text-ink-400" strokeWidth={1.75} />
            <p className="text-sm text-ink-400">Nothing logged yet.</p>
          </Card>
        )}

        {!isLoading && !loadError && result?.items.length > 0 && (
          <>
            <div className="flex flex-col gap-2">
              {result.items.map((entry) => (
                <Card key={entry.id} className="flex flex-wrap items-start justify-between gap-3 py-3.5">
                  <div className="min-w-0">
                    <p className="text-sm font-bold text-ink-900">{formatAction(entry.action)}</p>
                    <p className="mt-0.5 text-xs text-ink-400">
                      {entry.adminName}
                      {entry.targetType && <> · {entry.targetType}</>}
                    </p>
                    {entry.detail && <p className="mt-1 text-xs text-ink-600">{entry.detail}</p>}
                  </div>
                  <span className="shrink-0 text-xs text-ink-400">{formatWhen(entry.createdAt)}</span>
                </Card>
              ))}
            </div>

            <div className="mt-4 flex items-center justify-between">
              <span className="text-xs text-ink-400">
                Page {page} of {totalPages} · {result.totalCount} total
              </span>
              <div className="flex gap-2">
                <Button variant="secondary" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
                  <ChevronLeft className="h-4 w-4" strokeWidth={2.25} />
                  Previous
                </Button>
                <Button variant="secondary" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
                  Next
                  <ChevronRight className="h-4 w-4" strokeWidth={2.25} />
                </Button>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
