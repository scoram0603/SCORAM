import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, Loader2, Flame, Zap, Snowflake, Lock, Award } from "lucide-react";
import { Bar, BarChart, CartesianGrid, Cell, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { getGamificationSummary, getBadges, getProgressAnalytics } from "../api/gamification";

// Mirrors GamificationService.LevelForXp on the backend -- used only to draw the XP progress bar
// (the API tells us xpToNextLevel, but not this level's starting point, which is needed to compute
// a percentage). If the backend thresholds ever change, this must be updated to match.
const LEVEL_FLOOR = { Beginner: 0, Intermediate: 500, Expert: 2000, Master: 5000 };

// Same activity -> color convention as the Home page's Quick Access tiles (mint=Mock,
// accent=Practice, violet=Quizzes, secondary=PYQs) -- a student already associates these colors
// with these activities from the rest of the app, so the chart doesn't need its own legend to learn.
const ACTIVITY_META = {
  Mock: { label: "Mock Tests", color: "#1E9E5A" },
  Practice: { label: "Practice", color: "#FF6B00" },
  Quiz: { label: "Quizzes", color: "#7C3AED" },
  PreviousYearPaper: { label: "PYQs", color: "#1E63D5" },
};

// Traffic-light coding for "where do I need to improve" -- deliberately NOT the activity colors
// above, since this chart is about subject strength, not activity type.
function accuracyColor(pct) {
  if (pct < 50) return "#DC2626";
  if (pct < 70) return "#FF6B00";
  return "#1E9E5A";
}

export default function Progress() {
  const navigate = useNavigate();
  const [summary, setSummary] = useState(null);
  const [badges, setBadges] = useState(null);
  const [analytics, setAnalytics] = useState(null);
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

    // Fetched independently -- if this newer endpoint ever fails, the core streak/XP/badges view
    // (which already works today) shouldn't go down with it.
    getProgressAnalytics()
      .then(setAnalytics)
      .catch(() => setAnalytics(null));
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

          {analytics && (analytics.byActivity.length > 0 || analytics.bySubject.length > 0) && (
            <>
              <h2 className="mt-6 text-sm font-bold text-ink-900">Performance analytics</h2>
              <p className="mt-1 text-xs text-ink-400">See where you're doing well, and where to focus next.</p>

              {analytics.byActivity.length > 0 && (
                <div className="mt-3 rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
                  <h3 className="text-xs font-bold text-ink-900">Average score by activity</h3>
                  <div className="mt-3 h-56">
                    <ResponsiveContainer width="100%" height="100%">
                      <BarChart
                        data={analytics.byActivity.map((a) => ({ ...a, label: ACTIVITY_META[a.testKind]?.label || a.testKind }))}
                        margin={{ top: 4, right: 4, left: -20, bottom: 0 }}
                      >
                        <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#EAEEF6" />
                        <XAxis dataKey="label" tick={{ fontSize: 11, fill: "#8A93A6" }} axisLine={false} tickLine={false} />
                        <YAxis domain={[0, 100]} tick={{ fontSize: 11, fill: "#8A93A6" }} axisLine={false} tickLine={false} unit="%" />
                        <Tooltip
                          cursor={{ fill: "#EAEEF6" }}
                          contentStyle={{ borderRadius: 10, border: "1px solid #EAEEF6", fontSize: 12 }}
                          formatter={(value, _name, props) => [`${value}%`, `${props.payload.attemptCount} attempt${props.payload.attemptCount === 1 ? "" : "s"}`]}
                        />
                        <Bar dataKey="avgScorePercent" radius={[6, 6, 0, 0]}>
                          {analytics.byActivity.map((a) => (
                            <Cell key={a.testKind} fill={ACTIVITY_META[a.testKind]?.color || "#8A93A6"} />
                          ))}
                        </Bar>
                      </BarChart>
                    </ResponsiveContainer>
                  </div>
                </div>
              )}

              {analytics.bySubject.length > 0 && (
                <div className="mt-3 rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
                  <h3 className="text-xs font-bold text-ink-900">Accuracy by subject</h3>
                  <p className="mt-0.5 text-[11px] text-ink-400">Weakest first — these are worth extra practice.</p>
                  <div className="mt-3" style={{ height: Math.max(160, analytics.bySubject.length * 40) }}>
                    <ResponsiveContainer width="100%" height="100%">
                      <BarChart data={analytics.bySubject} layout="vertical" margin={{ top: 4, right: 28, left: 8, bottom: 0 }}>
                        <CartesianGrid strokeDasharray="3 3" horizontal={false} stroke="#EAEEF6" />
                        <XAxis type="number" domain={[0, 100]} tick={{ fontSize: 11, fill: "#8A93A6" }} axisLine={false} tickLine={false} unit="%" />
                        <YAxis type="category" dataKey="subject" width={110} tick={{ fontSize: 11, fill: "#1B1F2A" }} axisLine={false} tickLine={false} />
                        <Tooltip
                          cursor={{ fill: "#EAEEF6" }}
                          contentStyle={{ borderRadius: 10, border: "1px solid #EAEEF6", fontSize: 12 }}
                          formatter={(value, _name, props) => [`${value}% (${props.payload.correct}/${props.payload.attempted} correct)`, "Accuracy"]}
                        />
                        <Bar dataKey="accuracyPercent" radius={[0, 6, 6, 0]}>
                          {analytics.bySubject.map((s) => (
                            <Cell key={s.subject} fill={accuracyColor(s.accuracyPercent)} />
                          ))}
                        </Bar>
                      </BarChart>
                    </ResponsiveContainer>
                  </div>
                </div>
              )}

              {analytics.recentScoreTrend.length >= 2 && (
                <div className="mt-3 rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
                  <h3 className="text-xs font-bold text-ink-900">Score trend</h3>
                  <p className="mt-0.5 text-[11px] text-ink-400">Your last {analytics.recentScoreTrend.length} attempts.</p>
                  <div className="mt-3 h-48">
                    <ResponsiveContainer width="100%" height="100%">
                      <LineChart
                        data={analytics.recentScoreTrend.map((p, i) => ({ ...p, index: i + 1 }))}
                        margin={{ top: 4, right: 8, left: -20, bottom: 0 }}
                      >
                        <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#EAEEF6" />
                        <XAxis dataKey="index" tick={{ fontSize: 11, fill: "#8A93A6" }} axisLine={false} tickLine={false} />
                        <YAxis domain={[0, 100]} tick={{ fontSize: 11, fill: "#8A93A6" }} axisLine={false} tickLine={false} unit="%" />
                        <Tooltip
                          contentStyle={{ borderRadius: 10, border: "1px solid #EAEEF6", fontSize: 12 }}
                          formatter={(value, _name, props) => [`${value}%`, ACTIVITY_META[props.payload.testKind]?.label || props.payload.testKind]}
                          labelFormatter={(_, payload) => (payload?.[0] ? new Date(payload[0].payload.date).toLocaleDateString() : "")}
                        />
                        <Line type="monotone" dataKey="scorePercent" stroke="#1E63D5" strokeWidth={2.5} dot={{ r: 3, fill: "#1E63D5" }} />
                      </LineChart>
                    </ResponsiveContainer>
                  </div>
                </div>
              )}
            </>
          )}
        </>
      )}
    </div>
  );
}
