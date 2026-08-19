import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, Loader2, Medal } from "lucide-react";
import { getLeaderboard } from "../api/gamification";
import { listExams } from "../api/exams";
import { API_BASE_URL } from "../api/client";

const SCOPE_TABS = [
  { key: "global", label: "Global" },
  { key: "exam", label: "By Exam" },
  { key: "friends", label: "Friends" },
];

const PERIOD_TABS = [
  { key: "alltime", label: "All-time" },
  { key: "weekly", label: "Weekly" },
  { key: "monthly", label: "Monthly" },
];

function photoSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

function initialsFor(fullName) {
  if (!fullName) return "?";
  const parts = fullName.trim().split(/\s+/);
  return ((parts[0]?.[0] || "") + (parts[1]?.[0] || "")).toUpperCase() || fullName[0].toUpperCase();
}

// Live -- wired to GET /api/gamification/leaderboard. Global/all-time and Friends have no time
// dimension (see GamificationController), so the Period row only shows for Global/Exam scopes.
export default function Leaderboard() {
  const navigate = useNavigate();
  const [scope, setScope] = useState("global");
  const [period, setPeriod] = useState("alltime");
  const [exams, setExams] = useState([]);
  const [examName, setExamName] = useState("");
  const [data, setData] = useState(null);
  const [status, setStatus] = useState("loading");

  useEffect(() => {
    listExams()
      .then((res) => {
        setExams(res);
        setExamName((prev) => prev || res[0]?.name || "");
      })
      .catch(() => {
        // exam-wise tab just won't have anything to pick from -- Global/Friends still work fine
      });
  }, []);

  useEffect(() => {
    if (scope === "exam" && !examName) return; // wait for the exam list above to resolve first
    setStatus("loading");
    const controller = new AbortController();
    getLeaderboard(
      {
        scope,
        period: scope === "friends" ? "alltime" : period,
        examName: scope === "exam" ? examName : undefined,
      },
      { signal: controller.signal }
    )
      .then((res) => {
        setData(res);
        setStatus("success");
      })
      .catch((err) => {
        if (err.name !== "AbortError") setStatus("error");
      });
    return () => controller.abort();
  }, [scope, period, examName]);

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

      <h1 className="mt-3 text-xl font-extrabold text-ink-900 sm:text-2xl">Leaderboard</h1>

      <div className="mt-4 flex gap-2">
        {SCOPE_TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setScope(t.key)}
            className={`rounded-xl2 px-3.5 py-2 text-xs font-semibold transition-colors ${
              scope === t.key ? "bg-primary-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {scope === "exam" && exams.length > 0 && (
        <select
          value={examName}
          onChange={(e) => setExamName(e.target.value)}
          className="mt-3 w-full rounded-xl2 border border-primary-100 bg-white px-3.5 py-2.5 text-sm font-medium text-ink-900 sm:w-64"
        >
          {exams.map((e) => (
            <option key={e.id} value={e.name}>
              {e.name}
            </option>
          ))}
        </select>
      )}

      {scope !== "friends" && (
        <div className="mt-3 flex gap-2">
          {PERIOD_TABS.map((t) => (
            <button
              key={t.key}
              type="button"
              onClick={() => setPeriod(t.key)}
              className={`rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors ${
                period === t.key ? "bg-ink-900 text-white" : "bg-primary-50 text-ink-600 hover:bg-primary-100"
              }`}
            >
              {t.label}
            </button>
          ))}
        </div>
      )}

      <div className="mt-4">
        {status === "loading" && (
          <div className="flex justify-center py-16 text-ink-400">
            <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
          </div>
        )}

        {status === "error" && (
          <p className="rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-600">
            Couldn't load the leaderboard right now.
          </p>
        )}

        {status === "success" && data.entries.length === 0 && (
          <p className="rounded-xl2 border border-primary-100 bg-white p-4 text-sm text-ink-400">
            {scope === "friends" ? "Refer a friend to see them here." : "No XP earned in this period yet."}
          </p>
        )}

        {status === "success" && data.entries.length > 0 && (
          <div className="flex flex-col gap-2">
            {data.entries.map((entry) => (
              <div
                key={entry.userId}
                className={`flex items-center gap-3 rounded-xl2 border p-3.5 shadow-card ${
                  entry.isCurrentUser ? "border-primary-300 bg-primary-50/60" : "border-primary-100 bg-white"
                }`}
              >
                <span
                  className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-xs font-extrabold ${
                    entry.rank === 1
                      ? "bg-yellow-100 text-yellow-700"
                      : entry.rank === 2
                      ? "bg-gray-100 text-gray-600"
                      : entry.rank === 3
                      ? "bg-orange-100 text-orange-700"
                      : "bg-primary-50 text-primary-600"
                  }`}
                >
                  {entry.rank <= 3 ? <Medal className="h-4 w-4" strokeWidth={2.25} /> : entry.rank}
                </span>

                {entry.photoUrl ? (
                  <img
                    src={photoSrc(entry.photoUrl)}
                    alt={entry.fullName}
                    className="h-9 w-9 shrink-0 rounded-full object-cover"
                  />
                ) : (
                  <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary-600 text-xs font-bold text-white">
                    {initialsFor(entry.fullName)}
                  </span>
                )}

                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-bold text-ink-900">
                    {entry.fullName}
                    {entry.isCurrentUser ? " (You)" : ""}
                  </p>
                  <p className="truncate text-xs text-ink-400">@{entry.username}</p>
                </div>

                <span className="shrink-0 text-sm font-extrabold text-primary-600">{entry.xp} XP</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
