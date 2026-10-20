import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { ArrowLeft, CheckCircle2, Loader2, AlertCircle, Trash2 } from "lucide-react";
import StarRatingInput from "../components/reviews/StarRating";
import { getMyReview, saveMyReview, withdrawMyReview } from "../api/reviews";
import { validateReview, MAX_TEXT, MIN_TEXT, REVIEW_STATUS_INFO } from "../utils/reviews";

const TONES = {
  amber: "bg-amber-50 text-amber-700",
  green: "bg-mint-50 text-mint-500",
  gray: "bg-ink-400/10 text-ink-600",
};

// "Rate SCORAM" -- authenticated students write / edit / withdraw their single public review (route is inside the
// ProtectedRoute block). Nothing is shown publicly until a moderator approves it, and only 3-5 star reviews are
// featured on the landing page; the student is told this up front so there are no surprises.
export default function RateScoram() {
  const [loadState, setLoadState] = useState("loading"); // loading | ready | error
  const [existing, setExisting] = useState(null);
  const [rating, setRating] = useState(0);
  const [text, setText] = useState("");
  const [showName, setShowName] = useState(false);
  const [errors, setErrors] = useState({});
  const [submitting, setSubmitting] = useState(false);
  const [withdrawing, setWithdrawing] = useState(false);
  const [confirmWithdraw, setConfirmWithdraw] = useState(false);
  const [notice, setNotice] = useState(null); // { type: "success" | "error", message }

  function applyReview(review) {
    setExisting(review);
    setRating(review?.rating ?? 0);
    setText(review?.text ?? "");
    setShowName(!!review?.showNamePublicly);
  }

  useEffect(() => {
    const controller = new AbortController();
    getMyReview({ signal: controller.signal })
      .then((res) => { applyReview(res?.review ?? null); setLoadState("ready"); })
      .catch((err) => { if (err.name !== "AbortError") setLoadState("error"); });
    return () => controller.abort();
  }, []);

  const locked = existing?.status === "Hidden";
  const dirty = !existing || existing.rating !== rating || existing.text !== text.trim() || existing.showNamePublicly !== showName;

  async function handleSubmit(e) {
    e.preventDefault();
    if (submitting) return; // no double submits
    setNotice(null);

    const result = validateReview({ rating, text });
    setErrors(result.errors);
    if (!result.valid) return;

    setSubmitting(true);
    try {
      const saved = await saveMyReview({ rating, text: result.text, showNamePublicly: showName });
      applyReview(saved);
      setNotice({ type: "success", message: existing ? "Your review was updated and will be checked again before it is shown." : "Thank you! Your review was submitted and will appear once our team has reviewed it." });
    } catch (err) {
      setNotice({ type: "error", message: err?.status === 429 ? "You've made several changes recently. Please try again in a little while." : err?.message || "We couldn't save your review. Please try again." });
    } finally {
      setSubmitting(false);
    }
  }

  async function handleWithdraw() {
    setWithdrawing(true);
    setNotice(null);
    try {
      await withdrawMyReview();
      applyReview(null);
      setConfirmWithdraw(false);
      setErrors({});
      setNotice({ type: "success", message: "Your review was withdrawn." });
    } catch (err) {
      setNotice({ type: "error", message: err?.message || "We couldn't withdraw your review. Please try again." });
    } finally {
      setWithdrawing(false);
    }
  }

  const info = existing ? REVIEW_STATUS_INFO[existing.status] : null;
  const remaining = MAX_TEXT - text.length;

  return (
    <div className="px-4 pb-12 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <Link to="/profile" className="inline-flex items-center gap-1.5 text-sm font-semibold text-secondary-500">
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} /> Profile
      </Link>

      <div className="mx-auto mt-4 max-w-2xl">
        <h1 className="text-xl font-extrabold text-ink-900 sm:text-2xl">Rate SCORAM</h1>
        <p className="mt-1 text-sm text-ink-600">
          Tell other aspirants what SCORAM is like for you. Reviews are checked by our team before they are shown, and only
          ratings of 3 stars and above are featured on the home page. Your email, phone number and username are never shown.
        </p>

        {loadState === "loading" && (
          <div className="flex justify-center py-16 text-ink-400" role="status" aria-label="Loading your review">
            <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
          </div>
        )}

        {loadState === "error" && (
          <div className="mt-6 flex items-start gap-2 rounded-xl2 bg-red-50 p-4 text-sm text-red-600" role="alert">
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
            <span>We couldn't load your review. Please refresh the page and try again.</span>
          </div>
        )}

        {loadState === "ready" && (
          <form onSubmit={handleSubmit} noValidate className="mt-6 rounded-2xl border border-primary-100 bg-white p-5 shadow-card sm:p-6">
            {info && (
              <p className={`mb-5 rounded-xl px-3.5 py-2.5 text-sm ${TONES[info.tone]}`}>
                <span className="font-bold">{info.label}.</span> {info.help}
              </p>
            )}

            <fieldset disabled={submitting || withdrawing || locked} className="space-y-5">
              <div>
                <legend className="mb-1 text-sm font-bold text-ink-900">Your rating</legend>
                <StarRatingInput value={rating} onChange={(n) => { setRating(n); setErrors((p) => ({ ...p, rating: undefined })); }} disabled={submitting || withdrawing || locked} invalid={!!errors.rating} describedBy={errors.rating ? "rating-error" : undefined} />
                {errors.rating && <p id="rating-error" role="alert" className="mt-1 text-xs font-medium text-red-600">{errors.rating}</p>}
              </div>

              <div>
                <label htmlFor="review-text" className="mb-1 block text-sm font-bold text-ink-900">Your review</label>
                <textarea
                  id="review-text"
                  rows={6}
                  maxLength={MAX_TEXT + 200}
                  value={text}
                  onChange={(e) => { setText(e.target.value); setErrors((p) => ({ ...p, text: undefined })); }}
                  aria-invalid={!!errors.text}
                  aria-describedby={errors.text ? "review-text-error review-text-count" : "review-text-count"}
                  placeholder="What do you like about SCORAM? What could be better?"
                  className="w-full rounded-xl2 border border-primary-100 bg-white px-3.5 py-3 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500 focus:outline-none disabled:bg-surface"
                />
                <div className="mt-1 flex items-start justify-between gap-3">
                  <p id="review-text-error" role="alert" className="text-xs font-medium text-red-600">{errors.text}</p>
                  <p id="review-text-count" className={`shrink-0 text-xs ${remaining < 0 ? "font-semibold text-red-600" : "text-ink-400"}`}>
                    {text.trim().length < MIN_TEXT ? `At least ${MIN_TEXT} characters · ` : ""}{remaining} left
                  </p>
                </div>
              </div>

              <label className="flex cursor-pointer items-start gap-3 rounded-xl border border-primary-100 bg-surface px-3.5 py-3 text-sm text-ink-900">
                <input type="checkbox" checked={showName} onChange={(e) => setShowName(e.target.checked)} className="mt-0.5 h-4 w-4 rounded border-primary-200 text-primary-600 focus:ring-secondary-500" />
                <span>
                  <span className="font-semibold">Show my first name and last initial</span>
                  <span className="block text-xs text-ink-400">If unchecked, your review is shown as "SCORAM Student".</span>
                </span>
              </label>
            </fieldset>

            {notice && (
              <div role={notice.type === "error" ? "alert" : "status"} className={`mt-5 flex items-start gap-2 rounded-xl2 p-3 text-sm font-medium ${notice.type === "error" ? "bg-red-50 text-red-600" : "bg-mint-50 text-mint-500"}`}>
                {notice.type === "error" ? <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} /> : <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />}
                <span>{notice.message}</span>
              </div>
            )}

            <div className="mt-6 flex flex-wrap items-center gap-3">
              {!locked && (
                <button
                  type="submit"
                  disabled={submitting || withdrawing || (!!existing && !dirty)}
                  className="inline-flex items-center justify-center gap-2 rounded-xl2 bg-primary-600 px-6 py-3 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
                >
                  {submitting && <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />}
                  {existing ? "Update review" : "Submit review"}
                </button>
              )}

              {existing && !confirmWithdraw && (
                <button type="button" onClick={() => setConfirmWithdraw(true)} disabled={submitting || withdrawing}
                  className="inline-flex items-center gap-1.5 rounded-xl2 px-4 py-3 text-sm font-semibold text-red-600 hover:bg-red-50 disabled:opacity-60">
                  <Trash2 className="h-4 w-4" strokeWidth={2.25} /> Withdraw review
                </button>
              )}

              {existing && confirmWithdraw && (
                <span className="flex flex-wrap items-center gap-2 text-sm">
                  <span className="font-semibold text-ink-900">Delete your review permanently?</span>
                  <button type="button" onClick={handleWithdraw} disabled={withdrawing}
                    className="inline-flex items-center gap-1.5 rounded-xl2 bg-red-600 px-4 py-2 text-sm font-semibold text-white hover:bg-red-700 disabled:opacity-60">
                    {withdrawing && <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />} Yes, withdraw
                  </button>
                  <button type="button" onClick={() => setConfirmWithdraw(false)} disabled={withdrawing}
                    className="rounded-xl2 px-3 py-2 text-sm font-semibold text-ink-600 hover:bg-primary-50">Keep it</button>
                </span>
              )}
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
