import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, CheckCircle2, XCircle, Eye, ExternalLink } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { listPendingReports, updateReportStatus } from "../api/reports";
import { PageHeader, Card, Button, Alert, friendlyError } from "../components/AdminUI";

const REPORT_TYPE_LABELS = {
  WrongAnswer: "Wrong Answer",
  WrongOption: "Wrong Option",
  WrongQuestionStatement: "Wrong Question",
  IncorrectExplanation: "Wrong Explanation",
  TypingMistake: "Typographical Error",
  Duplicate: "Duplicate Question",
  IncorrectExamYear: "Incorrect Exam/Year",
  Other: "Other",
};

// Spec section 28-C: admin review queue for "Report Question" submissions -- built to work for BOTH
// the legacy Paper-based Question and Question Bank questions from day one (they share one table;
// see Controllers/QuestionReportsController.cs), rather than a Question Bank-only queue.
export default function QuestionBankReportsQueue() {
  const { token } = useAdminAuth();
  const navigate = useNavigate();
  const [items, setItems] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [busyId, setBusyId] = useState(null);

  useEffect(refresh, [token]);

  function refresh() {
    setLoading(true);
    listPendingReports(token, { page: 1, pageSize: 50 })
      .then((res) => setItems(res.items))
      .catch((err) => setError(friendlyError(err)))
      .finally(() => setLoading(false));
  }

  async function handleUpdate(id, status) {
    setBusyId(id);
    try {
      await updateReportStatus(token, id, status);
      setItems((prev) => prev.filter((r) => r.id !== id));
    } catch (err) {
      window.alert(friendlyError(err));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div>
      <PageHeader
        title="Question Reports"
        subtitle="Students flagging a wrong question, option, answer, or explanation"
        action={
          <Button variant="ghost" onClick={() => navigate("/admin/question-bank")}>
            <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
            Back to Question Bank
          </Button>
        }
      />

      <div className="p-6">
        {error && <div className="mb-4"><Alert>{error}</Alert></div>}

        {loading && <p className="text-sm text-ink-400">Loading…</p>}
        {!loading && items.length === 0 && <p className="text-sm text-ink-400">No pending reports. Nicely caught up.</p>}

        <div className="flex flex-col gap-3">
          {items.map((r) => (
            <Card key={r.id}>
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2 text-xs">
                    <span className="rounded-full bg-red-50 px-2.5 py-1 font-semibold text-red-600">
                      {REPORT_TYPE_LABELS[r.reportType] || r.reportType}
                    </span>
                    <span className="rounded-full bg-primary-50 px-2.5 py-1 font-semibold text-primary-600">
                      {r.isQuestionBank ? "Question Bank" : "PYQ Paper"}
                    </span>
                    <span className="text-ink-400">{r.contextLabel}</span>
                  </div>
                  <p className="mt-2 text-sm font-medium text-ink-900">{r.questionTextSnippet}</p>
                  {r.description && <p className="mt-1 text-xs text-ink-600">"{r.description}"</p>}
                  <p className="mt-1.5 text-xs text-ink-400">
                    Reported by {r.reportedByName} · {new Date(r.createdAt).toLocaleDateString()}
                  </p>
                </div>

                <div className="flex shrink-0 flex-col items-end gap-2">
                  <button
                    type="button"
                    onClick={() => navigate(r.isQuestionBank ? `/question-bank/${r.questionBankQuestionId}` : `/questions/${r.questionId}`)}
                    className="flex items-center gap-1 text-xs font-semibold text-secondary-500 hover:underline"
                  >
                    <ExternalLink className="h-3.5 w-3.5" strokeWidth={2.25} />
                    View question
                  </button>
                  <div className="flex gap-2">
                    <Button variant="secondary" isLoading={busyId === r.id} onClick={() => handleUpdate(r.id, "UnderReview")}>
                      <Eye className="h-3.5 w-3.5" strokeWidth={2.25} />
                      Reviewing
                    </Button>
                    <Button variant="secondary" isLoading={busyId === r.id} onClick={() => handleUpdate(r.id, "Resolved")}>
                      <CheckCircle2 className="h-3.5 w-3.5" strokeWidth={2.25} />
                      Resolve
                    </Button>
                    <Button variant="danger" isLoading={busyId === r.id} onClick={() => handleUpdate(r.id, "Rejected")}>
                      <XCircle className="h-3.5 w-3.5" strokeWidth={2.25} />
                      Reject
                    </Button>
                  </div>
                </div>
              </div>
            </Card>
          ))}
        </div>
      </div>
    </div>
  );
}
