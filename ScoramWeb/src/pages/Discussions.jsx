import { useEffect, useState } from "react";
import {
  MessageCircle, ThumbsUp, MessageSquare, Loader2,
  ChevronLeft, ChevronRight, Send, ArrowRight,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { getTopDiscussions, upvoteComment, replyToComment } from "../api/discussions";
import { useAuth } from "../context/AuthContext";
import { timeAgo, isRecent } from "../utils/format";

const PAGE_SIZE = 10;

export default function Discussions() {
  const { isAuthenticated } = useAuth();
  const navigate = useNavigate();
  const [page, setPage] = useState(1);
  const [result, setResult] = useState(null);
  const [status, setStatus] = useState("loading");
  const [openReplyFor, setOpenReplyFor] = useState(null);
  const [replyText, setReplyText] = useState("");
  const [replySubmitting, setReplySubmitting] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    setStatus("loading");
    getTopDiscussions({ page, pageSize: PAGE_SIZE }, { signal: controller.signal })
      .then((res) => {
        setResult(res);
        setStatus("success");
      })
      .catch((err) => {
        if (err.name !== "AbortError") setStatus("error");
      });
    return () => controller.abort();
  }, [page]);

  async function handleUpvote(commentId) {
    if (!isAuthenticated) {
      navigate("/login?redirect=/discussions");
      return;
    }
    setResult((prev) => ({
      ...prev,
      items: prev.items.map((d) => (d.commentId === commentId ? { ...d, upvoteCount: d.upvoteCount + 1 } : d)),
    }));
    try {
      await upvoteComment(commentId);
    } catch {
      setResult((prev) => ({
        ...prev,
        items: prev.items.map((d) => (d.commentId === commentId ? { ...d, upvoteCount: d.upvoteCount - 1 } : d)),
      }));
    }
  }

  function toggleReply(commentId) {
    if (!isAuthenticated) {
      navigate("/login?redirect=/discussions");
      return;
    }
    setOpenReplyFor((prev) => (prev === commentId ? null : commentId));
    setReplyText("");
  }

  async function submitReply(commentId) {
    if (!replyText.trim()) return;
    setReplySubmitting(true);
    try {
      await replyToComment(commentId, replyText.trim());
      setResult((prev) => ({
        ...prev,
        items: prev.items.map((d) => (d.commentId === commentId ? { ...d, replyCount: d.replyCount + 1 } : d)),
      }));
      setOpenReplyFor(null);
      setReplyText("");
    } catch (err) {
      window.alert(err.message || "Couldn't post your reply. Please try again.");
    } finally {
      setReplySubmitting(false);
    }
  }

  const totalPages = result ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : 1;

  return (
    <div className="px-4 pb-8 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <h1 className="text-xl font-extrabold text-ink-900 sm:text-2xl">Discussions</h1>
      <p className="mt-1 text-sm text-ink-400">
        The most active discussions across every question, live from the Scoram community.
      </p>

      {status === "loading" && (
        <div className="flex justify-center py-16 text-ink-400">
          <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
        </div>
      )}

      {status === "error" && (
        <p className="mt-4 rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-600">
          Couldn't load discussions right now.
        </p>
      )}

      {status === "success" && result.items.length === 0 && (
        <p className="mt-4 rounded-xl2 border border-primary-100 bg-white p-4 text-sm text-ink-400">
          No discussions yet.
        </p>
      )}

      {status === "success" && result.items.length > 0 && (
        <>
          <ul className="mt-4 flex flex-col gap-3">
            {result.items.map((d) => (
              <li key={d.commentId} className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card sm:p-5">
                <div className="flex items-start gap-3">
                  <span className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-mint-50 text-mint-500">
                    <MessageCircle className="h-[18px] w-[18px]" strokeWidth={2} />
                  </span>

                  <div className="min-w-0 flex-1">
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex flex-wrap items-center gap-1.5">
                        <span className="rounded-md bg-secondary-50 px-1.5 py-0.5 text-[10px] font-semibold text-secondary-500">
                          {d.examName}
                        </span>
                        <span className="rounded-md bg-violet-50 px-1.5 py-0.5 text-[10px] font-semibold text-violet-500">
                          {d.subject}
                        </span>
                        {isRecent(d.createdAt) && (
                          <span className="rounded-md bg-mint-50 px-1.5 py-0.5 text-[10px] font-bold text-mint-500">
                            New
                          </span>
                        )}
                      </div>
                      <span className="shrink-0 text-[12px] text-ink-400">{timeAgo(d.createdAt)}</span>
                    </div>

                    <p className="mt-2 text-sm font-semibold leading-snug text-ink-900">{d.commentText}</p>
                    <p className="mt-1 text-xs text-ink-400">— {d.authorName}</p>

                    <div className="mt-3 flex items-center gap-4 text-xs text-ink-400">
                      <button
                        type="button"
                        onClick={() => handleUpvote(d.commentId)}
                        className="flex items-center gap-1 transition-colors hover:text-secondary-500"
                      >
                        <ThumbsUp className="h-3.5 w-3.5" strokeWidth={2} />
                        {d.upvoteCount} Upvotes
                      </button>
                      <button
                        type="button"
                        onClick={() => toggleReply(d.commentId)}
                        className="flex items-center gap-1 transition-colors hover:text-secondary-500"
                      >
                        <MessageSquare className="h-3.5 w-3.5" strokeWidth={2} />
                        {d.replyCount} {d.replyCount === 1 ? "Reply" : "Replies"}
                      </button>
                      <button
                        type="button"
                        onClick={() => navigate(`/questions/${d.questionId}`)}
                        className="ml-auto flex items-center gap-1 font-semibold text-secondary-500 hover:text-secondary-600"
                      >
                        View question
                        <ArrowRight className="h-3.5 w-3.5" strokeWidth={2.5} />
                      </button>
                    </div>

                    {openReplyFor === d.commentId && (
                      <div className="mt-3 flex items-center gap-2">
                        <input
                          type="text"
                          value={replyText}
                          onChange={(e) => setReplyText(e.target.value)}
                          onKeyDown={(e) => {
                            if (e.key === "Enter") submitReply(d.commentId);
                          }}
                          placeholder="Write a reply..."
                          className="h-10 flex-1 rounded-lg border border-primary-100 bg-surface px-3 text-sm focus:border-secondary-500 focus:bg-white"
                        />
                        <button
                          type="button"
                          onClick={() => submitReply(d.commentId)}
                          disabled={replySubmitting}
                          aria-label="Send reply"
                          className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary-600 text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
                        >
                          {replySubmitting ? (
                            <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />
                          ) : (
                            <Send className="h-4 w-4" strokeWidth={2.25} />
                          )}
                        </button>
                      </div>
                    )}
                  </div>
                </div>
              </li>
            ))}
          </ul>

          {totalPages > 1 && (
            <div className="mt-6 flex items-center justify-center gap-3">
              <button
                type="button"
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="flex h-9 w-9 items-center justify-center rounded-lg border border-primary-100 text-primary-600 disabled:opacity-40"
                aria-label="Previous page"
              >
                <ChevronLeft className="h-4 w-4" strokeWidth={2.5} />
              </button>
              <span className="text-sm font-medium text-ink-600">
                Page {page} of {totalPages}
              </span>
              <button
                type="button"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                className="flex h-9 w-9 items-center justify-center rounded-lg border border-primary-100 text-primary-600 disabled:opacity-40"
                aria-label="Next page"
              >
                <ChevronRight className="h-4 w-4" strokeWidth={2.5} />
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
