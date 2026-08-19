import { useEffect, useState } from "react";
import { Eye, User } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { useAdminAuth } from "../context/AdminAuthContext";
import { listPendingPapers } from "../api/papers";
import { API_BASE_URL } from "../../api/client";
import { PageHeader, Card, Button, Alert } from "../components/AdminUI";

function logoSrc(logoUrl) {
  if (!logoUrl) return null;
  return logoUrl.startsWith("http") ? logoUrl : `${API_BASE_URL}${logoUrl}`;
}

export default function ReviewQueue() {
  const navigate = useNavigate();
  const { token } = useAdminAuth();
  const [papers, setPapers] = useState([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    listPendingPapers(token)
      .then(setPapers)
      .catch((err) => setError(err.message))
      .finally(() => setIsLoading(false));
  }, [token]);

  return (
    <div>
      <PageHeader title="Review Queue" subtitle="Papers waiting to be approved and published for students." />

      <div className="p-6">
        {isLoading && <p className="text-sm text-ink-400">Loading…</p>}
        {error && <Alert>{error}</Alert>}
        {!isLoading && !error && papers.length === 0 && (
          <p className="text-sm text-ink-400">Nothing waiting for review right now.</p>
        )}

        <div className="flex flex-col gap-3">
          {papers.map((paper) => (
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
                  <h3 className="text-sm font-bold text-ink-900">
                    {paper.examName} · {paper.year}
                  </h3>
                  <p className="mt-0.5 text-xs text-ink-400">
                    {paper.language}{paper.paperCode ? ` · ${paper.paperCode}` : ""} · {paper.questionCount} question{paper.questionCount === 1 ? "" : "s"}
                    {" · "}
                    <span className="inline-flex items-center gap-1"><User className="inline h-3 w-3" strokeWidth={2.25} />{paper.createdByAdminName}</span>
                  </p>
                </div>
              </div>

              <Button onClick={() => navigate(`/admin/papers/${paper.id}`)}>
                <Eye className="h-4 w-4" strokeWidth={2.25} />
                Review
              </Button>
            </Card>
          ))}
        </div>
      </div>
    </div>
  );
}
