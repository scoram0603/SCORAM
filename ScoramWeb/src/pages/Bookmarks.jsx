import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Bookmark, HelpCircle, MessageCircle, FileQuestion, Trophy, Loader2, ChevronLeft, ChevronRight } from "lucide-react";
import { getBookmarks } from "../api/bookmarks";
import BookmarkButton from "../components/questions/BookmarkButton";
import { timeAgo } from "../utils/format";

const PAGE_SIZE = 20;

// Query param for BookmarksController.List, one per tab -- "all" first since that's the default view.
const TABS = [
  { key: "all", label: "All" },
  { key: "questions", label: "Questions" },
  { key: "discussions", label: "Discussions" },
  { key: "papers", label: "Papers" },
  { key: "mocktests", label: "Mock Tests" },
];

// Maps a BookmarkListItemDto.type (see DTOs/BookmarkDTOs.cs's BookmarkType enum, serialized as a
// string) to how this page displays + toggles + routes to it. `toggleType` is one of
// bookmarks.js's TOGGLE_PATH keys, which use different names than the backend's own List `type`
// query param above -- singular/camelCase there vs the plural/lowercase tab keys here.
const TYPE_META = {
  Question: {
    icon: HelpCircle,
    accent: "bg-secondary-50 text-secondary-500",
    toggleType: "question",
    title: (i) => i.questionText,
    subtitle: (i) => i.subject,
    route: (i) => `/questions/${i.targetId}`,
  },
  QuestionBankQuestion: {
    icon: HelpCircle,
    accent: "bg-violet-50 text-violet-500",
    toggleType: "questionBank",
    title: (i) => i.questionText,
    subtitle: (i) => i.subject,
    route: (i) => `/question-bank/${i.targetId}`,
  },
  Discussion: {
    icon: MessageCircle,
    accent: "bg-mint-50 text-mint-500",
    toggleType: "discussion",
    title: (i) => i.commentText,
    subtitle: (i) => `${i.authorName} · ${i.replyCount} ${i.replyCount === 1 ? "Reply" : "Replies"}`,
    route: (i) => (i.discussionQuestionBankQuestionId ? `/question-bank/${i.discussionQuestionBankQuestionId}` : `/questions/${i.discussionQuestionId}`),
  },
  Paper: {
    icon: FileQuestion,
    accent: "bg-primary-50 text-primary-600",
    toggleType: "paper",
    title: (i) => [i.examName, i.year].filter(Boolean).join(" "),
    subtitle: (i) => i.paperCode,
    route: (i) => `/tests/instructions/paper/${i.targetId}`,
  },
  MockTest: {
    icon: Trophy,
    accent: "bg-accent-50 text-accent-600",
    toggleType: "mockTest",
    title: (i) => i.title,
    subtitle: (i) => [i.examName, i.durationMinutes ? `${i.durationMinutes} min` : null].filter(Boolean).join(" · "),
    route: (i) => `/tests/instructions/mock/${i.targetId}`,
  },
};

export default function Bookmarks() {
  const navigate = useNavigate();
  const [tab, setTab] = useState("all");
  const [page, setPage] = useState(1);
  const [result, setResult] = useState(null);
  const [status, setStatus] = useState("loading");

  useEffect(() => {
    setStatus("loading");
    getBookmarks({ type: tab, page, pageSize: PAGE_SIZE })
      .then((res) => {
        setResult(res);
        setStatus("success");
      })
      .catch(() => setStatus("error"));
  }, [tab, page]);

  function handleTabChange(key) {
    setTab(key);
    setPage(1);
  }

  function handleRemove(bookmarkId) {
    setResult((prev) => ({
      ...prev,
      items: prev.items.filter((i) => i.bookmarkId !== bookmarkId),
      totalCount: prev.totalCount - 1,
    }));
  }

  const totalPages = result ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : 1;

  return (
    <div className="px-4 pb-8 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <h1 className="text-xl font-extrabold text-ink-900 sm:text-2xl">Bookmarks</h1>
      <p className="mt-1 text-sm text-ink-400">Everything you've saved for later, in one place.</p>

      <div className="mt-4 flex gap-2 overflow-x-auto pb-1">
        {TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => handleTabChange(t.key)}
            className={`shrink-0 rounded-xl2 border px-3.5 py-2 text-sm font-semibold transition-colors ${
              tab === t.key
                ? "border-primary-600 bg-primary-600 text-white"
                : "border-primary-100 bg-white text-ink-600 hover:border-primary-300"
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      <div className="mt-4">
        {status === "loading" && (
          <div className="flex justify-center py-16 text-ink-400">
            <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
          </div>
        )}

        {status === "error" && (
          <p className="rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-600">
            Couldn't load your bookmarks right now.
          </p>
        )}

        {status === "success" && result.items.length === 0 && (
          <div className="flex flex-col items-center gap-2 rounded-xl2 border border-dashed border-primary-100 py-16 text-center">
            <span className="flex h-11 w-11 items-center justify-center rounded-full bg-primary-50 text-primary-600">
              <Bookmark className="h-5 w-5" strokeWidth={2} />
            </span>
            <p className="text-sm font-semibold text-ink-900">Nothing saved here yet</p>
            <p className="max-w-xs text-xs text-ink-400">
              Tap the bookmark icon on any question, discussion, paper, or mock test to save it here.
            </p>
          </div>
        )}

        {status === "success" && result.items.length > 0 && (
          <>
            <ul className="flex flex-col gap-3">
              {result.items.map((item) => {
                const meta = TYPE_META[item.type];
                if (!meta) return null;
                const Icon = meta.icon;
                const subtitle = meta.subtitle(item);
                return (
                  <li key={item.bookmarkId} className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card sm:p-5">
                    <div className="flex items-start gap-3">
                      <span className={`mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-full ${meta.accent}`}>
                        <Icon className="h-[18px] w-[18px]" strokeWidth={2} />
                      </span>
                      <div className="min-w-0 flex-1">
                        <button
                          type="button"
                          onClick={() => navigate(meta.route(item))}
                          className="text-left text-sm font-semibold leading-snug text-ink-900 hover:text-primary-600"
                        >
                          {meta.title(item)}
                        </button>
                        {subtitle && <p className="mt-1 text-xs text-ink-400">{subtitle}</p>}
                        <p className="mt-1 text-[11px] text-ink-400">Saved {timeAgo(item.createdAt)}</p>
                      </div>
                      <BookmarkButton
                        type={meta.toggleType}
                        id={item.targetId}
                        isBookmarked={true}
                        size="sm"
                        onChange={(isBookmarked) => {
                          if (!isBookmarked) handleRemove(item.bookmarkId);
                        }}
                      />
                    </div>
                  </li>
                );
              })}
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
    </div>
  );
}
