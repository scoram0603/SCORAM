import { useState } from "react";
import { Flag, Loader2, X } from "lucide-react";
import { reportQuestion, reportQuestionBankQuestion, REPORT_REASONS } from "../../api/reports";
import { useAuth } from "../../context/AuthContext";

// questionType: "paper" (legacy PYQ question) or "bank" (Question Bank question) -- picks which of
// the two report endpoints to call (both write to the same QuestionReport table on the backend, see
// Controllers/QuestionReportsController.cs).
export default function ReportQuestionModal({ questionId, questionType = "paper", open, onClose }) {
  const { isAuthenticated } = useAuth();
  const [reportType, setReportType] = useState(REPORT_REASONS[0].value);
  const [description, setDescription] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState(null);
  const [submitted, setSubmitted] = useState(false);

  if (!open) return null;

  async function handleSubmit(e) {
    e.preventDefault();
    if (!isAuthenticated) {
      setError("Please log in to report a question.");
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const submitFn = questionType === "bank" ? reportQuestionBankQuestion : reportQuestion;
      await submitFn(questionId, { reportType, description: description.trim() || undefined });
      setSubmitted(true);
    } catch (err) {
      setError(err.message || "Couldn't submit your report. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  function handleClose() {
    setSubmitted(false);
    setDescription("");
    setReportType(REPORT_REASONS[0].value);
    setError(null);
    onClose?.();
  }

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-ink-900/40 sm:items-center" onClick={handleClose}>
      <div
        className="w-full max-w-md rounded-t-2xl bg-white p-5 shadow-xl sm:rounded-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Flag className="h-[18px] w-[18px] text-red-500" strokeWidth={2.25} />
            <h2 className="text-[15px] font-bold text-ink-900">Report Question</h2>
          </div>
          <button type="button" onClick={handleClose} className="text-ink-400 hover:text-ink-600">
            <X className="h-5 w-5" strokeWidth={2} />
          </button>
        </div>

        {submitted ? (
          <div className="mt-4 rounded-xl2 border border-mint-100 bg-mint-50 p-4 text-sm font-semibold text-mint-600">
            Report submitted successfully.
            <button type="button" onClick={handleClose} className="mt-3 block text-xs font-semibold text-ink-400 underline">
              Close
            </button>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="mt-4 flex flex-col gap-3">
            <div>
              <label className="mb-1 block text-xs font-semibold text-ink-600">What's wrong with this question?</label>
              <select
                value={reportType}
                onChange={(e) => setReportType(e.target.value)}
                className="h-10 w-full rounded-lg border border-primary-100 bg-white px-3 text-sm focus:border-secondary-500"
              >
                {REPORT_REASONS.map((r) => (
                  <option key={r.value} value={r.value}>{r.label}</option>
                ))}
              </select>
            </div>

            <div>
              <label className="mb-1 block text-xs font-semibold text-ink-600">Description (optional)</label>
              <textarea
                rows={3}
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="e.g. Option B is marked correct, but the explanation points to Option D."
                className="w-full rounded-lg border border-primary-100 bg-white px-3 py-2 text-sm focus:border-secondary-500"
              />
            </div>

            {error && <p className="text-xs text-red-600">{error}</p>}

            <div className="mt-1 flex gap-2">
              <button
                type="button"
                onClick={handleClose}
                className="flex-1 rounded-xl2 border border-primary-100 bg-white px-4 py-2 text-sm font-semibold text-ink-600"
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={submitting}
                className="flex flex-1 items-center justify-center gap-1.5 rounded-xl2 bg-red-500 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-red-600 disabled:opacity-60"
              >
                {submitting && <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />}
                Submit report
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
