import { useState } from "react";
import { Bookmark } from "lucide-react";
import { toggleBookmark } from "../../api/bookmarks";
import { useAuth } from "../../context/AuthContext";

// type: one of bookmarks.js's TOGGLE_PATH keys -- "question" | "questionBank" | "discussion" |
// "paper" | "mockTest". `size` lets this drop into a tight card header (e.g. a paper/mock-test
// list card) as well as a roomier detail-page toolbar.
export default function BookmarkButton({ type, id, isBookmarked, onChange, onRequireLogin, size = "md" }) {
  const { isAuthenticated } = useAuth();
  const [busy, setBusy] = useState(false);

  async function handleToggle() {
    if (!isAuthenticated) return onRequireLogin?.();
    if (busy) return;
    setBusy(true);
    try {
      const result = await toggleBookmark(type, id);
      onChange?.(result.isBookmarked);
    } catch (err) {
      window.alert(err.message || "Couldn't update your bookmark. Please try again.");
    } finally {
      setBusy(false);
    }
  }

  const isSmall = size === "sm";

  return (
    <button
      type="button"
      onClick={handleToggle}
      disabled={busy}
      aria-pressed={isBookmarked}
      aria-label={isBookmarked ? "Remove bookmark" : "Save bookmark"}
      title={isBookmarked ? "Remove bookmark" : "Save for later"}
      className={`flex items-center justify-center rounded-lg border transition-colors disabled:opacity-60 ${
        isSmall ? "h-8 w-8" : "gap-1.5 px-3 py-1.5 text-xs font-semibold"
      } ${
        isBookmarked
          ? "border-amber-300 bg-amber-50 text-amber-600"
          : "border-primary-100 text-ink-600 hover:border-amber-200 hover:text-amber-600"
      }`}
    >
      <Bookmark className={isSmall ? "h-4 w-4" : "h-3.5 w-3.5"} strokeWidth={isBookmarked ? 2.5 : 2} fill={isBookmarked ? "currentColor" : "none"} />
      {!isSmall && (isBookmarked ? "Saved" : "Save")}
    </button>
  );
}
