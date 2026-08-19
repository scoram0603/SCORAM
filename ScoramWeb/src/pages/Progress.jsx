import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, Loader2, Flame, Zap, Snowflake, Lock, Award } from "lucide-react";
import { getGamificationSummary, getBadges } from "../api/gamification";

// Mirrors GamificationService.LevelForXp on the backend -- used only to draw the XP progress bar
// (the API tells us xpToNextLevel, but not this level's starting point, which is needed to compute
// a percentage). If the backend thresholds ever change, this must be updated to match.
const LEVEL_FLOOR = { Beginner: 0, Intermediate: 500, Expert: 2000, Master: 5000 };

export default function Progress() {
  const navigate = useNavigate();
  const [summary, setSummary] = useState(null);
  const [badges, setBadges] = useState(null);
  const [status, setStatus] = useState("loading");

  useEffect(() => {
    setStatus("loading");
    Promise.all([getGamificationSummary(), getBadges()])
      .then(([s, b]) => {
        setSummary(s);
        setBadges(b);
        setStatus("success");
      })
      .catch(() => setStatus("error"));
  }, []);

  const floor = summary ? LEVEL_FLOOR[summary.currentLevel] ?? 0 : 0;
  const nextFloor = summary?.nextLevel ? LEVEL_FLOOR[summary.nextLevel] : null;
  const levelPercent = nextFloor
    ? Math.min(100, Math.max(0, Math.round(((summary.totalXP - floor) / (nextFloor - floor)) * 100)))
    : 100;

  return (
    <div className="px-4 pb-10 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <button
        type="button"
        onClick={() => navigate("/")}
        className="flex items-center gap-1.5 text-sm font-semibold text-secondary-500"
      >
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
        Home
      </button>

      <h1 className="mt-3 text-xl font-extrabold text-ink-900 sm:text-2xl">My Progress</h1>

      {status === "loading" && (
        <div className="flex justify-center py-16 text-ink-400">
          <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
        </div>
      )}

      {status === "error" && (
        <p className="mt-4 rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-600">
          Couldn't load your progress right now.
        </p>
      )}

      {status === "success" && (
        <>
          <div className="mt-4 grid gap-3 sm:grid-cols-2">
            <div className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
              <div className="flex items-center gap-3">
                <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-accent-50 text-accent-500">
                  <Flame className="h-5 w-5" strokeWidth={2.25} />
                </span>
                <div>
                  <p className="text-2xl font-extrabold text-ink-900">{summary.currentStreak}</p>
                  <p className="text-xs text-ink-400">Day streak</p>
                </div>
              </div>
              <div className="mt-3 flex items-center justify-between border-t border-primary-50 pt-3 text-xs text-ink-400">
                <span>Longest: {summary.longestStreak} days</span>
                <span className="flex items-center gap-1">
                  <Snowflake className="h-3.5 w-3.5" strokeWidth={2.25} />
                  {summary.freezesAvailableThisWeek} freeze left this week
                </span>
              </div>
            </div>

            <div className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
              <div className="flex items-center gap-3">
                <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-secondary-50 text-secondary-500">
                  <Zap className="h-5 w-5" strokeWidth={2.25} fill="currentColor" />
                </span>
                <div>
                  <p className="text-2xl font-extrabold text-ink-900">{summary.totalXP} XP</p>
                  <p className="text-xs text-ink-400">{summary.currentLevel} level</p>
                </div>
              </div>
              <div className="mt-3 border-t border-primary-50 pt-3">
                <div className="h-2 w-full overflow-hidden rounded-full bg-primary-50">
                  <div className="h-full rounded-full bg-primary-600" style={{ width: `${levelPercent}%` }} />
                </div>
                <p className="mt-1.5 text-xs text-ink-400">
                  {summary.nextLevel ? `${summary.xpToNextLevel} XP to ${summary.nextLevel}` : "Highest level reached"}
                </p>
              </div>
            </div>
          </div>

          <h2 className="mt-6 text-sm font-bold text-ink-900">
            Badges ({summary.badgeCount}/{badges.length})
          </h2>
          <div className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-4">
            {badges.map((b) => (
              <div
                key={b.id}
                className={`flex flex-col items-center gap-2 rounded-xl2 border p-4 text-center ${
                  b.earned ? "border-primary-100 bg-white shadow-card" : "border-dashed border-primary-100 bg-primary-50/40"
                }`}
              >
                <span
                  className={`flex h-12 w-12 items-center justify-center rounded-2xl ${
                    b.earned ? "bg-accent-50 text-accent-500" : "bg-primary-50 text-ink-300"
                  }`}
                >
                  {b.earned ? <Award className="h-6 w-6" strokeWidth={2.25} /> : <Lock className="h-5 w-5" strokeWidth={2.25} />}
                </span>
                <p className={`text-xs font-bold ${b.earned ? "text-ink-900" : "text-ink-400"}`}>{b.name}</p>
                <p className="text-[11px] leading-snug text-ink-400">{b.criteriaDescription}</p>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
