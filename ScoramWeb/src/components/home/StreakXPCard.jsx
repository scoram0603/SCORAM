import { useEffect, useState } from "react";
import { Flame, Zap, ArrowRight } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { getGamificationSummary } from "../../api/gamification";
import { useAuth } from "../../context/AuthContext";

// Live -- wired to GET /api/gamification/me. Mobile-only (lg:hidden) by original design; desktop
// users get the full picture on the "My Progress" page this card links to. Renders nothing for
// guests or on error -- this is a small motivational widget, not critical information, so failing
// silently beats cluttering the home page with a login prompt for a decorative card.
export default function StreakXPCard() {
  const { isAuthenticated } = useAuth();
  const navigate = useNavigate();
  const [summary, setSummary] = useState(null);
  const [status, setStatus] = useState(isAuthenticated ? "loading" : "guest");

  useEffect(() => {
    if (!isAuthenticated) {
      setStatus("guest");
      return;
    }
    const controller = new AbortController();
    setStatus("loading");
    getGamificationSummary({ signal: controller.signal })
      .then((res) => {
        setSummary(res);
        setStatus("success");
      })
      .catch((err) => {
        if (err.name !== "AbortError") setStatus("error");
      });
    return () => controller.abort();
  }, [isAuthenticated]);

  if (status === "guest" || status === "error") return null;

  return (
    <div className="px-4 pb-6 sm:px-6 lg:hidden">
      <div className="flex items-center gap-4 rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
        <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-accent-50 text-accent-500">
          <Flame className="h-6 w-6" strokeWidth={2.25} />
        </span>

        <div className="flex-1">
          {status === "loading" ? (
            <>
              <div className="h-4 w-28 animate-pulse rounded bg-primary-50" />
              <div className="mt-1.5 h-3 w-16 animate-pulse rounded bg-primary-50" />
            </>
          ) : (
            <>
              <p className="text-sm font-bold text-ink-900">{summary.currentStreak} Day Streak</p>
              <p className="mt-0.5 flex items-center gap-1 text-xs font-semibold text-secondary-500">
                <Zap className="h-3.5 w-3.5" strokeWidth={2.25} fill="currentColor" />
                {summary.totalXP} XP · {summary.currentLevel}
              </p>
            </>
          )}
        </div>

        <button
          type="button"
          onClick={() => navigate("/progress")}
          className="flex shrink-0 items-center gap-1 rounded-lg bg-primary-50 px-3 py-2 text-xs font-semibold text-primary-600 transition-colors hover:bg-primary-100"
        >
          View Progress
          <ArrowRight className="h-3.5 w-3.5" strokeWidth={2.5} />
        </button>
      </div>
    </div>
  );
}
