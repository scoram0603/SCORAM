import { useRef } from "react";
import { ThumbsUp, ThumbsDown } from "lucide-react";
import { voteOnQuestion } from "../../api/votes";
import { useAuth } from "../../context/AuthContext";

// Same toggle rules as the server (QuestionVotesController): tapping your own reaction retracts it,
// tapping the other one switches it.
function applyVote({ likeCount, dislikeCount, myVote }, isLike) {
  let likes = likeCount;
  let dislikes = dislikeCount;
  let mine = myVote ?? null;
  if (mine === isLike) {
    if (isLike) likes -= 1;
    else dislikes -= 1;
    mine = null;
  } else {
    if (mine === true) likes -= 1;
    if (mine === false) dislikes -= 1;
    if (isLike) likes += 1;
    else dislikes += 1;
    mine = isLike;
  }
  return { likeCount: Math.max(0, likes), dislikeCount: Math.max(0, dislikes), myVote: mine };
}

// questionType: "paper" (legacy PYQ question) or "bank" (Question Bank question). Clicking the
// active reaction again retracts it; clicking the other one switches it -- one vote per user,
// enforced server-side (Controllers/QuestionVotesController.cs).
//
// OPTIMISTIC: the counts/highlight change the instant a button is pressed (onChange is called with the
// predicted result straight away); the request runs in the background, one at a time so rapid clicks
// reach the server in order. When the last pending request settles the parent is given the server's real
// numbers -- or, if a request failed, the last server-confirmed ones (rolling the change back).
export default function LikeButton({ questionId, likeCount, dislikeCount, myVote, questionType = "paper", onChange, onRequireLogin }) {
  const { isAuthenticated } = useAuth();

  const latest = useRef(null);
  const pending = useRef(0);
  const confirmed = useRef(null);
  const chain = useRef(Promise.resolve());

  function handleVote(isLike) {
    if (!isAuthenticated) return onRequireLogin?.();

    // While requests are in flight the optimistic value in `latest` is the truth for the next click;
    // once idle, start from whatever the parent currently shows (props).
    const current = pending.current > 0 ? latest.current : { likeCount, dislikeCount, myVote };
    if (pending.current === 0) confirmed.current = current;
    pending.current += 1;

    const predicted = applyVote(current, isLike);
    latest.current = predicted;
    onChange?.(predicted);

    chain.current = chain.current.then(async () => {
      let failure = null;
      try {
        confirmed.current = await voteOnQuestion(questionId, isLike, questionType);
      } catch (err) {
        failure = err;
      }
      pending.current -= 1;
      if (pending.current === 0) {
        latest.current = confirmed.current;
        onChange?.(confirmed.current);
      }
      if (failure) window.alert(failure.message || "Couldn't record your vote. Please try again.");
    });
  }

  return (
    <div className="flex items-center gap-2">
      <button
        type="button"
        onClick={() => handleVote(true)}
        className={`flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-xs font-semibold transition-colors ${
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
        className={`flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-xs font-semibold transition-colors ${
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
