import { useEffect, useRef, useState } from "react";
import { AlertCircle, CheckCircle2, Loader2, Star, X } from "lucide-react";
import { submitFeedback } from "../../api/feedback";

const TYPES = [
  { value: "Suggestion", label: "Suggestion" },
  { value: "Improvement", label: "Improvement" },
  { value: "BugReport", label: "Bug Report" },
  { value: "ContentIssue", label: "Content Issue" },
  { value: "UiUx", label: "UI/UX" },
  { value: "Other", label: "Other" },
];
const MAX_LENGTH = 2000;
const AUTO_CLOSE_MS = 2200;

// Feedback form. Required: Feedback Type + Message. Rating (1-5 stars) is optional -- tap the
// selected star again to clear it. Submit is disabled while the request runs AND guarded by a ref,
// so a double click can never send two requests (the server also de-duplicates an identical repeat).
// Failure shows a friendly message, never the raw server error, and keeps what the student typed.
export default function FeedbackModal({ source = "Home", onClose }) {
  const [type, setType] = useState("");
  const [message, setMessage] = useState("");
  const [rating, setRating] = useState(0);
  const [status, setStatus] = useState("idle"); // idle | submitting | success | error
  const [validation, setValidation] = useState({});
  const submittingRef = useRef(false);
  const textareaRef = useRef(null);
  const closeTimer = useRef(null);

  useEffect(() => {
    function onKey(e) {
      if (e.key === "Escape" && !submittingRef.current) onClose();
    }
    document.addEventListener("keydown", onKey);
    // Lock page scroll behind the modal.
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = previousOverflow;
      clearTimeout(closeTimer.current);
    };
  }, [onClose]);

  async function handleSubmit(e) {
    e.preventDefault();
    if (submittingRef.current) return;

    const trimmed = message.trim();
    const problems = {};
    if (!type) problems.type = "Please choose a feedback type.";
    if (!trimmed) problems.message = "Please write your feedback.";
    if (Object.keys(problems).length > 0) {
      setValidation(problems);
      if (problems.message && type) textareaRef.current?.focus();
      return;
    }

    submittingRef.current = true;
    setValidation({});
    setStatus("submitting");
    try {
      await submitFeedback({ feedbackType: type, message: trimmed, rating: rating || null, source });
      setStatus("success");
      closeTimer.current = setTimeout(onClose, AUTO_CLOSE_MS);
    } catch {
      setStatus("error"); // deliberately generic -- never surface a raw server message
    } finally {
      submittingRef.current = false;
    }
  }

  const submitting = status === "submitting";

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center sm:items-center" role="dialog" aria-modal="true" aria-labelledby="feedback-title">
      <div className="absolute inset-0 bg-ink-900/50" onClick={() => !submitting && onClose()} />

      <div className="relative flex max-h-[92vh] w-full flex-col overflow-hidden rounded-t-2xl bg-white shadow-cardHover sm:max-w-md sm:rounded-2xl">
        <div className="flex items-center justify-between border-b border-primary-50 px-5 py-3.5">
          <h2 id="feedback-title" className="text-base font-extrabold text-ink-900">Send Feedback</h2>
          <button
            type="button"
            onClick={onClose}
            disabled={submitting}
            aria-label="Close"
            className="flex h-8 w-8 items-center justify-center rounded-full text-ink-400 hover:bg-surface disabled:opacity-40"
          >
            <X className="h-4 w-4" strokeWidth={2.25} />
          </button>
        </div>

        {status === "success" ? (
          <div role="status" className="flex flex-col items-center gap-3 px-6 py-12 text-center">
            <span className="flex h-14 w-14 items-center justify-center rounded-full bg-mint-50 text-mint-500">
              <CheckCircle2 className="h-7 w-7" strokeWidth={2.25} />
            </span>
            <p className="text-base font-bold text-ink-900">Thank you for your feedback!</p>
            <p className="text-sm text-ink-600">It helps us make SCORAM better.</p>
            <button type="button" onClick={onClose} className="mt-2 rounded-xl2 bg-primary-600 px-6 py-2.5 text-sm font-bold text-white hover:bg-primary-700">
              Back to Home
            </button>
          </div>
        ) : (
          <form onSubmit={handleSubmit} noValidate className="flex min-h-0 flex-1 flex-col">
            <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-5 py-4">
              <fieldset>
                <legend className="text-xs font-bold uppercase tracking-wide text-ink-400">Feedback type</legend>
                <div className="mt-2 flex flex-wrap gap-2">
                  {TYPES.map((t) => (
                    <button
                      key={t.value}
                      type="button"
                      aria-pressed={type === t.value}
                      disabled={submitting}
                      onClick={() => { setType(t.value); setValidation((v) => ({ ...v, type: undefined })); }}
                      className={`rounded-full px-3.5 py-1.5 text-xs font-semibold transition-colors ${
                        type === t.value ? "bg-primary-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"
                      }`}
                    >
                      {t.label}
                    </button>
                  ))}
                </div>
                {validation.type && <p role="alert" className="mt-1.5 text-xs font-semibold text-accent-600">{validation.type}</p>}
              </fieldset>

              <div>
                <label htmlFor="feedback-message" className="text-xs font-bold uppercase tracking-wide text-ink-400">Message</label>
                <textarea
                  id="feedback-message"
                  ref={textareaRef}
                  value={message}
                  onChange={(e) => { setMessage(e.target.value); if (validation.message) setValidation((v) => ({ ...v, message: undefined })); }}
                  maxLength={MAX_LENGTH}
                  rows={5}
                  disabled={submitting}
                  placeholder="Tell us what you think, what could be better, or what went wrong..."
                  className="mt-2 w-full resize-none rounded-xl2 border border-primary-100 px-3 py-2.5 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500 disabled:bg-primary-50"
                />
                <div className="mt-1 flex items-start justify-between gap-2">
                  <p role="alert" className="text-xs font-semibold text-accent-600">{validation.message || ""}</p>
                  <span className="shrink-0 text-[11px] text-ink-400">{message.length}/{MAX_LENGTH}</span>
                </div>
              </div>

              <div>
                <span className="text-xs font-bold uppercase tracking-wide text-ink-400">Rating <span className="font-medium normal-case">(optional)</span></span>
                <div className="mt-2 flex items-center gap-1" role="group" aria-label="Rating">
                  {[1, 2, 3, 4, 5].map((n) => (
                    <button
                      key={n}
                      type="button"
                      disabled={submitting}
                      onClick={() => setRating((r) => (r === n ? 0 : n))}
                      aria-label={`${n} star${n === 1 ? "" : "s"}`}
                      aria-pressed={rating === n}
                      className="rounded-md p-1 text-amber-400 hover:scale-110 disabled:opacity-50"
                    >
                      <Star className="h-7 w-7" strokeWidth={1.75} fill={n <= rating ? "currentColor" : "none"} />
                    </button>
                  ))}
                </div>
              </div>

              {status === "error" && (
                <div role="alert" className="flex items-center gap-2 rounded-xl2 border border-accent-100 bg-accent-50 p-3 text-sm text-accent-600">
                  <AlertCircle className="h-4 w-4 shrink-0" strokeWidth={2.25} />
                  Unable to submit feedback. Please try again.
                </div>
              )}
            </div>

            <div className="border-t border-primary-50 px-5 py-3.5">
              <button
                type="submit"
                disabled={submitting}
                className="flex w-full items-center justify-center gap-2 rounded-xl2 bg-primary-600 py-3 text-sm font-bold text-white transition-colors hover:bg-primary-700 disabled:cursor-not-allowed disabled:opacity-60"
              >
                {submitting && <Loader2 className="h-4 w-4 animate-spin" />}
                {submitting ? "Submitting..." : "Submit Feedback"}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
