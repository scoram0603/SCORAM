import { useState } from "react";
import { ThumbsUp, ThumbsDown } from "lucide-react";
import { voteOnQuestion } from "../../api/votes";
import { useAuth } from "../../context/AuthContext";

// questionType: "paper" (legacy PYQ question) or "bank" (Question Bank question). Clicking the
// active reaction again retracts it; clicking the other one switches it -- one vote per user,
// enforced server-side (Controllers/QuestionVotesController.cs).
export default function LikeButton({ questionId, likeCount, dislikeCount, myVote, questionType = "paper", onChange, onRequireLogin }) {
  const { isAuthenticated } = useAuth();
  const [busy, setBusy] = useState(false);

  async function handleVote(isLike) {
    if (!isAuthenticated) return onRequireLogin?.();
    if (busy) return;
    setBusy(true);
    try {
      const result = await voteOnQuestion(questionId, isLike, questionType);
      onChange?.(result);
    } catch (err) {
      window.alert(err.message || "Couldn't record your vote. Please try again.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex items-center gap-2">
      <button
        type="button"
        onClick={() => handleVote(true)}
        disabled={busy}
        className={`flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-xs font-semibold transition-colors disabled:opacity-60 ${
          myVote === true
            ? "border-mint-500 bg-mint-50 text-mint-600"
            : "border-primary-100 text-ink-600 hover:border-mint-200 hover:text-mint-600"
        }`}
      >
        <ThumbsUp className="h-3.5 w-3.5" strokeWidth={myVote === true ? 2.5 : 2} />
        {likeCount}
      </button>
      <button
        type="button"
        onClick={() => handleVote(false)}
        disabled={busy}
        className={`flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-xs font-semibold transition-colors disabled:opacity-60 ${
          myVote === false
            ? "border-red-300 bg-red-50 text-red-600"
            : "border-primary-100 text-ink-600 hover:border-red-200 hover:text-red-600"
        }`}
      >
        <ThumbsDown className="h-3.5 w-3.5" strokeWidth={myVote === false ? 2.5 : 2} />
        {dislikeCount}
      </button>
    </div>
  );
}
