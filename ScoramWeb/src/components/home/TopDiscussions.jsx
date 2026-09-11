import { useEffect, useState } from "react";
import { ChevronRight, MessageCircle, ThumbsUp, MessageSquare, Loader2 } from "lucide-react";
import { Link, useNavigate } from "react-router-dom";
import { getTopDiscussions, upvoteComment } from "../../api/discussions";
import { useAuth } from "../../context/AuthContext";
import { timeAgo, isRecent } from "../../utils/format";

// Live — wired to GET /api/discussions (DiscussionsController). Replaced the
// earlier mock data now that this endpoint exists.
//
// QUALITY FILTER (redesign brief, section 16) -- the backend returns whatever's most recent, which
// can include a low-effort one-word reply with zero engagement ("ok" / 0 Upvotes / 0 Comments).
// Rather than inventing fake discussions to pad the list, this filters those specific low-quality
// items out client-side and falls back to the "start a discussion" empty state if nothing
// meaningful is left -- it never fabricates content, only hides noise from real API data.
function isLowQuality(discussion) {
  const textLength = (discussion.commentText || "").trim().length;
  const hasEngagement = discussion.upvoteCount > 0 || discussion.replyCount > 0;
  return textLength < 12 && !hasEngagement;
}

export default function TopDiscussions() {
  const { isAuthenticated } = useAuth();
  const navigate = useNavigate();
  const [items, setItems] = useState([]);
  const [status, setStatus] = useState("loading");

  useEffect(() => {
    const controller = new AbortController();
    setStatus("loading");
    getTopDiscussions({ page: 1, pageSize: 6 }, { signal: controller.signal })
      .then((res) => {
        setItems(res.items.filter((d) => !isLowQuality(d)).slice(0, 3));
        setStatus("success");
      })
      .catch((err) => {
        if (err.name !== "AbortError") setStatus("error");
      });
    return () => controller.abort();
  }, []);

  async function handleUpvote(commentId) {
    if (!isAuthenticated) {
      navigate("/login?redirect=/");
      return;
    }
    // Optimistic update, reverted if the request fails
    setItems((prev) => prev.map((d) => (d.commentId === commentId ? { ...d, upvoteCount: d.upvoteCount + 1 } : d)));
    try {
      await upvoteComment(commentId);
    } catch {
      setItems((prev) => prev.map((d) => (d.commentId === commentId ? { ...d, upvoteCount: d.upvoteCount - 1 } : d)));
    }
  }

  return (
    <section className="px-4 pb-6 sm:px-6 lg:px-0">
      <div className="mb-3 flex items-center justify-between sm:mb-4">
        <h3 className="text-[17px] font-bold text-ink-900 sm:text-lg">Top Discussions</h3>
        <Link to="/discussions" className="flex items-center gap-0.5 text-sm font-semibold text-secondary-500">
          View All
          <ChevronRight className="h-4 w-4" strokeWidth={2.5} />
        </Link>
      </div>

      {status === "loading" && (
        <div className="flex justify-center py-8 text-ink-400">
          <Loader2 className="h-5 w-5 animate-spin" strokeWidth={2.25} />
        </div>
      )}

      {status === "error" && (
        <p className="rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-600">
          Couldn't load discussions right now.
        </p>
      )}

      {status === "success" && items.length === 0 && (
        <div className="flex flex-col items-center gap-2 rounded-xl2 border border-primary-100 bg-white p-5 text-center">
          <p className="text-sm text-ink-600">Start a discussion with fellow aspirants</p>
          <Link
            to="/discussions"
            className="mt-1 rounded-lg bg-primary-600 px-3.5 py-1.5 text-xs font-bold text-white hover:bg-primary-700"
          >
            Explore Discussions
          </Link>
        </div>
      )}

      {status === "success" && items.length > 0 && (
        <ul className="flex flex-col gap-2.5 sm:gap-3">
          {items.map((d) => (
            <li key={d.commentId}>
              <div className="flex w-full items-start gap-3 rounded-xl2 border border-primary-100 bg-white p-3.5 shadow-card sm:p-4">
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
                    <span className="shrink-0 text-[11px] text-ink-400">{timeAgo(d.createdAt)}</span>
                  </div>

                  <p className="mt-1.5 text-[14px] font-semibold leading-snug text-ink-900">{d.commentText}</p>
                  <p className="mt-0.5 text-[12px] text-ink-400">— {d.authorName}</p>

                  <div className="mt-2 flex items-center gap-3 text-[12px] text-ink-400">
                    <button
                      type="button"
                      onClick={() => handleUpvote(d.commentId)}
                      className="flex items-center gap-1 transition-colors hover:text-secondary-500"
                    >
                      <ThumbsUp className="h-3.5 w-3.5" strokeWidth={2} />
                      {d.upvoteCount} Upvotes
                    </button>
                    <span className="flex items-center gap-1">
                      <MessageSquare className="h-3.5 w-3.5" strokeWidth={2} />
                      {d.replyCount} Comments
                    </span>
                  </div>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
