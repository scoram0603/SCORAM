import { useEffect, useState } from "react";
import { MessageCircle, ThumbsUp, ThumbsDown, MessageSquare, Loader2, Send, Pin, ShieldCheck, CheckCircle2, Flag } from "lucide-react";
import {
  getQuestionComments, createComment, replyToComment, upvoteComment, downvoteComment,
  toggleCommentResolved, reportComment,
} from "../../api/discussions";
import { useAuth } from "../../context/AuthContext";
import { timeAgo } from "../../utils/format";

function countAll(comments) {
  return comments.reduce((sum, c) => sum + 1 + countAll(c.replies || []), 0);
}

// Applies a pure update function to whichever comment in the tree matches commentId, at any depth.
function updateInTree(comments, commentId, updater) {
  return comments.map((c) => {
    if (c.id === commentId) return updater(c);
    if (c.replies?.length) return { ...c, replies: updateInTree(c.replies, commentId, updater) };
    return c;
  });
}

// questionType: "paper" (default -- legacy PYQ question) or "bank" (Question Bank question). Routes
// to /api/questions/{id}/comments vs /api/question-bank/{id}/comments -- see api/discussions.js. Both
// share the exact same QuestionComment table/moderation tools on the backend.
export default function CommentThread({ questionId, onRequireLogin, questionType = "paper" }) {
  const { isAuthenticated } = useAuth();
  const [comments, setComments] = useState([]);
  const [status, setStatus] = useState("loading");

  const [draft, setDraft] = useState("");
  const [posting, setPosting] = useState(false);

  const [openReplyFor, setOpenReplyFor] = useState(null);
  const [replyText, setReplyText] = useState("");
  const [replySubmitting, setReplySubmitting] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setStatus("loading");
    getQuestionComments(questionId, { auth: true }, questionType)
      .then((data) => {
        if (cancelled) return;
        setComments(data);
        setStatus("success");
      })
      .catch(() => {
        if (!cancelled) setStatus("error");
      });
    return () => {
      cancelled = true;
    };
  }, [questionId, questionType]);

  function refresh() {
    getQuestionComments(questionId, { auth: true }, questionType).then(setComments).catch(() => {});
  }

  async function handlePost() {
    if (!isAuthenticated) return onRequireLogin?.();
    if (!draft.trim()) return;
    setPosting(true);
    try {
      await createComment(questionId, draft.trim(), questionType);
      setDraft("");
      refresh();
    } catch (err) {
      window.alert(err.message || "Couldn't post your comment. Please try again.");
    } finally {
      setPosting(false);
    }
  }

  // Applies the server's authoritative counts/myVote once the toggle call resolves -- a plain
  // optimistic +1/-1 doesn't work here since clicking the opposite reaction swings both counters at
  // once (server-side toggle semantics; see DiscussionsController.ApplyCommentVoteAsync).
  async function handleVote(commentId, isUpvote) {
    if (!isAuthenticated) return onRequireLogin?.();
    try {
      const result = isUpvote ? await upvoteComment(commentId) : await downvoteComment(commentId);
      setComments((prev) => updateInTree(prev, commentId, (c) => ({
        ...c, upvoteCount: result.upvoteCount, downvoteCount: result.downvoteCount, myVote: result.myVote,
      })));
    } catch (err) {
      window.alert(err.message || "Couldn't record your vote. Please try again.");
    }
  }

  async function handleToggleResolved(commentId) {
    setComments((prev) => updateInTree(prev, commentId, (c) => ({ ...c, isResolved: !c.isResolved })));
    try {
      await toggleCommentResolved(commentId);
    } catch {
      setComments((prev) => updateInTree(prev, commentId, (c) => ({ ...c, isResolved: !c.isResolved })));
    }
  }

  async function handleReport(commentId) {
    if (!isAuthenticated) return onRequireLogin?.();
    const reason = window.prompt("What's wrong with this comment? (optional)");
    if (reason === null) return; // cancelled
    try {
      await reportComment(commentId, reason || undefined);
      window.alert("Thanks -- a moderator will take a look.");
    } catch (err) {
      window.alert(err.message || "Couldn't report this right now.");
    }
  }

  function toggleReply(commentId) {
    if (!isAuthenticated) return onRequireLogin?.();
    setOpenReplyFor((prev) => (prev === commentId ? null : commentId));
    setReplyText("");
  }

  async function submitReply(commentId) {
    if (!replyText.trim()) return;
    setReplySubmitting(true);
    try {
      await replyToComment(commentId, replyText.trim());
      setOpenReplyFor(null);
      setReplyText("");
      refresh();
    } catch (err) {
      window.alert(err.message || "Couldn't post your reply. Please try again.");
    } finally {
      setReplySubmitting(false);
    }
  }

  const totalCount = countAll(comments);

  return (
    <section className="mt-6">
      <div className="flex items-center gap-2">
        <MessageCircle className="h-[18px] w-[18px] text-primary-600" strokeWidth={2.25} />
        <h2 className="text-[15px] font-bold text-ink-900">
          Discussion {status === "success" ? `(${totalCount})` : ""}
        </h2>
      </div>

      <div className="mt-3 flex items-start gap-2">
        <input
          type="text"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") handlePost();
          }}
          placeholder={isAuthenticated ? "Ask a doubt or share a tip... (@username to mention someone)" : "Log in to join the discussion"}
          className="h-11 flex-1 rounded-xl2 border border-primary-100 bg-white px-3.5 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500"
        />
        <button
          type="button"
          onClick={handlePost}
          disabled={posting}
          aria-label="Post comment"
          className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl2 bg-primary-600 text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
        >
          {posting ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} /> : <Send className="h-4 w-4" strokeWidth={2.25} />}
        </button>
      </div>

      {status === "loading" && (
        <div className="flex justify-center py-10 text-ink-400">
          <Loader2 className="h-5 w-5 animate-spin" strokeWidth={2.25} />
        </div>
      )}

      {status === "error" && (
        <p className="mt-4 rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-600">
          Couldn't load the discussion right now.
        </p>
      )}

      {status === "success" && comments.length === 0 && (
        <p className="mt-4 rounded-xl2 border border-primary-100 bg-white p-4 text-sm text-ink-400">
          No discussion yet — ask the first question about this one.
        </p>
      )}

      {status === "success" && comments.length > 0 && (
        <ul className="mt-4 flex flex-col gap-3">
          {comments.map((c) => (
            <li key={c.id}>
              <CommentThreadNode
                comment={c}
                depth={0}
                onVote={handleVote}
                onToggleResolved={handleToggleResolved}
                onReport={handleReport}
                onToggleReply={toggleReply}
                openReplyFor={openReplyFor}
                replyText={replyText}
                onReplyTextChange={setReplyText}
                onSubmitReply={submitReply}
                replySubmitting={replySubmitting}
              />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

// Renders one comment plus its reply form (if open) plus every descendant, recursively -- depth only
// affects visual nesting (indent + border), never how deep a reply can actually go.
function CommentThreadNode({
  comment, depth, onVote, onToggleResolved, onReport,
  onToggleReply, openReplyFor, replyText, onReplyTextChange, onSubmitReply, replySubmitting,
}) {
  return (
    <div>
      <CommentRow
        comment={comment}
        compact={depth > 0}
        onVote={onVote}
        onToggleResolved={comment.parentCommentId == null ? onToggleResolved : null}
        onReport={onReport}
        onToggleReply={() => onToggleReply(comment.id)}
      />

      {openReplyFor === comment.id && (
        <div className="mt-2 ml-6 flex items-center gap-2 pl-4">
          <input
            type="text"
            value={replyText}
            onChange={(e) => onReplyTextChange(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") onSubmitReply(comment.id);
            }}
            placeholder="Write a reply..."
            className="h-10 flex-1 rounded-lg border border-primary-100 bg-surface px-3 text-sm focus:border-secondary-500 focus:bg-white"
          />
          <button
            type="button"
            onClick={() => onSubmitReply(comment.id)}
            disabled={replySubmitting}
            aria-label="Send reply"
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary-600 text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
          >
            {replySubmitting ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} /> : <Send className="h-4 w-4" strokeWidth={2.25} />}
          </button>
        </div>
      )}

      {comment.replies?.length > 0 && (
        <ul className="ml-6 mt-2 flex flex-col gap-2 border-l-2 border-primary-100 pl-4">
          {comment.replies.map((r) => (
            <li key={r.id}>
              <CommentThreadNode
                comment={r}
                depth={depth + 1}
                onVote={onVote}
                onToggleResolved={onToggleResolved}
                onReport={onReport}
                onToggleReply={onToggleReply}
                openReplyFor={openReplyFor}
                replyText={replyText}
                onReplyTextChange={onReplyTextChange}
                onSubmitReply={onSubmitReply}
                replySubmitting={replySubmitting}
              />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function CommentRow({ comment, onVote, onToggleResolved, onReport, onToggleReply, compact }) {
  return (
    <div className={`rounded-xl2 border bg-white p-4 shadow-card ${compact ? "sm:p-3.5" : "sm:p-5"} ${comment.isResolved ? "border-mint-200" : "border-primary-100"}`}>
      <div className="flex items-start gap-3">
        <span className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary-50 text-[13px] font-bold text-primary-600">
          {initials(comment.authorName)}
        </span>

        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-1.5">
            <span className="text-[13px] font-semibold text-ink-900">{comment.authorName}</span>
            <span className="text-[12px] text-ink-400">· {timeAgo(comment.createdAt)}</span>
            {comment.isPinned && (
              <span className="flex items-center gap-1 rounded-md bg-accent-50 px-1.5 py-0.5 text-[10px] font-bold text-accent-600">
                <Pin className="h-3 w-3" strokeWidth={2.5} />
                Pinned
              </span>
            )}
            {comment.isAdminHighlighted && (
              <span className="flex items-center gap-1 rounded-md bg-secondary-50 px-1.5 py-0.5 text-[10px] font-bold text-secondary-500">
                <ShieldCheck className="h-3 w-3" strokeWidth={2.5} />
                {comment.authorIsAdmin ? "Scoram Team" : "Verified"}
              </span>
            )}
            {comment.isResolved && (
              <span className="flex items-center gap-1 rounded-md bg-mint-50 px-1.5 py-0.5 text-[10px] font-bold text-mint-500">
                <CheckCircle2 className="h-3 w-3" strokeWidth={2.5} />
                Solved
              </span>
            )}
          </div>

          <p className="mt-1.5 whitespace-pre-line text-sm leading-snug text-ink-600">{highlightMentions(comment.commentText)}</p>

          <div className="mt-2.5 flex flex-wrap items-center gap-4 text-xs text-ink-400">
            <button
              type="button"
              onClick={() => onVote(comment.id, true)}
              className={`flex items-center gap-1 transition-colors hover:text-secondary-500 ${comment.myVote === true ? "font-bold text-secondary-500" : ""}`}
            >
              <ThumbsUp className="h-3.5 w-3.5" strokeWidth={comment.myVote === true ? 2.5 : 2} />
              {comment.upvoteCount}
            </button>
            <button
              type="button"
              onClick={() => onVote(comment.id, false)}
              className={`flex items-center gap-1 transition-colors hover:text-red-500 ${comment.myVote === false ? "font-bold text-red-500" : ""}`}
            >
              <ThumbsDown className="h-3.5 w-3.5" strokeWidth={comment.myVote === false ? 2.5 : 2} />
              {comment.downvoteCount}
            </button>
            <button type="button" onClick={onToggleReply} className="flex items-center gap-1 transition-colors hover:text-secondary-500">
              <MessageSquare className="h-3.5 w-3.5" strokeWidth={2} />
              Reply
            </button>
            {onToggleResolved && comment.isMine && (
              <button type="button" onClick={() => onToggleResolved(comment.id)} className="flex items-center gap-1 transition-colors hover:text-mint-500">
                <CheckCircle2 className="h-3.5 w-3.5" strokeWidth={2} />
                {comment.isResolved ? "Mark unsolved" : "Mark solved"}
              </button>
            )}
            <button type="button" onClick={() => onReport(comment.id)} className="ml-auto flex items-center gap-1 transition-colors hover:text-red-500">
              <Flag className="h-3.5 w-3.5" strokeWidth={2} />
              Report
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

// Wraps every @username token in a highlighted span -- purely visual (no autocomplete while typing;
// the backend resolves and notifies real usernames on submit regardless of how the text looks here).
function highlightMentions(text) {
  const parts = text.split(/(@[a-z0-9._]{3,30})/gi);
  return parts.map((part, i) =>
    /^@[a-z0-9._]{3,30}$/i.test(part) ? (
      <span key={i} className="font-semibold text-secondary-500">{part}</span>
    ) : (
      part
    )
  );
}

function initials(fullName) {
  if (!fullName) return "?";
  const parts = fullName.trim().split(/\s+/);
  return ((parts[0]?.[0] || "") + (parts[1]?.[0] || "")).toUpperCase() || fullName[0].toUpperCase();
}
