import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Flame, Zap, ChevronRight, Shuffle, History, Loader2, BookOpen } from "lucide-react";
import { getGamificationSummary } from "../../api/gamification";
import { useAuth } from "../../context/AuthContext";

// PREMIUM UI PASS -- fills the empty right-hand gutter on wide screens with content that's
// genuinely backed by real data (see the Question Bank redesign discussion): the student's own
// Progress snapshot (reuses GamificationService data, already built), Browse by Subject (real
// per-subject question counts already returned by GET /api/question-bank/subjects -- just wasn't
// being shown), and Quick Actions (Surprise Me / Recent Searches, moved here from the top of the
// page). Deliberately does NOT include a "Go Premium" card (no premium tier exists in this app) or
// fabricated "Most Practiced Topics" usage analytics (nothing tracks that).
export default function QuestionBankSidebar({ subjects, onPickSubject, recentSearches, onRunRecentSearch, onSurpriseMe, surpriseLoading, surpriseDisabled }) {
  const topSubjects = [...subjects].sort((a, b) => (b.questionCount ?? 0) - (a.questionCount ?? 0)).slice(0, 6);

  return (
    <aside className="hidden lg:sticky lg:top-6 lg:flex lg:w-72 lg:shrink-0 lg:flex-col lg:gap-4">
      <ProgressCard />

      {topSubjects.length > 0 && (
        <div className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
          <h3 className="flex items-center gap-1.5 text-sm font-bold text-ink-900">
            <BookOpen className="h-4 w-4 text-primary-600" strokeWidth={2.25} />
            Browse by Subject
          </h3>
          <div className="mt-3 flex flex-col gap-1">
            {topSubjects.map((s) => (
              <button
                key={s.id}
                type="button"
                onClick={() => onPickSubject(s.id)}
                className="flex items-center justify-between rounded-lg px-2 py-1.5 text-left text-sm text-ink-600 hover:bg-primary-50"
              >
                <span className="truncate">{s.name}</span>
                <span className="ml-2 shrink-0 rounded-full bg-primary-50 px-2 py-0.5 text-[11px] font-semibold text-primary-600">
                  {s.questionCount}
                </span>
              </button>
            ))}
          </div>
        </div>
      )}

      <div className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
        <h3 className="text-sm font-bold text-ink-900">Quick Actions</h3>
        <div className="mt-3 flex flex-col gap-1">
          <button
            type="button"
            onClick={onSurpriseMe}
            disabled={surpriseDisabled}
            className="flex items-center gap-2.5 rounded-lg px-2 py-2 text-left hover:bg-primary-50 disabled:opacity-60"
          >
            {surpriseLoading ? (
              <Loader2 className="h-4 w-4 shrink-0 animate-spin text-primary-600" strokeWidth={2.25} />
            ) : (
              <Shuffle className="h-4 w-4 shrink-0 text-primary-600" strokeWidth={2.25} />
            )}
            <span>
              <span className="block text-sm font-semibold text-ink-900">Surprise Me</span>
              <span className="block text-[11px] text-ink-400">Get a random question</span>
            </span>
          </button>

          {recentSearches.length > 0 && (
            <div className="mt-1 border-t border-primary-50 pt-2">
              <p className="flex items-center gap-1.5 px-2 text-[11px] font-semibold text-ink-400">
                <History className="h-3 w-3" strokeWidth={2.25} />
                Recent Searches
              </p>
              <div className="mt-1.5 flex flex-col gap-0.5">
                {recentSearches.map((term) => (
                  <button
                    key={term}
                    type="button"
                    onClick={() => onRunRecentSearch(term)}
                    className="truncate rounded-lg px-2 py-1.5 text-left text-sm text-ink-600 hover:bg-primary-50"
                  >
                    {term}
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    </aside>
  );
}

function ProgressCard() {
  const { isAuthenticated } = useAuth();
  const navigate = useNavigate();
  const [summary, setSummary] = useState(null);
  const [status, setStatus] = useState(isAuthenticated ? "loading" : "guest");

  useEffect(() => {
    if (!isAuthenticated) {
      setStatus("guest");
      return;
    }
    getGamificationSummary()
      .then((res) => {
        setSummary(res);
        setStatus("success");
      })
      .catch(() => setStatus("error"));
  }, [isAuthenticated]);

  if (status === "guest" || status === "error") return null;

  return (
    <button
      type="button"
      onClick={() => navigate("/progress")}
      className="rounded-xl2 border border-primary-100 bg-white p-4 text-left shadow-card transition-shadow hover:shadow-cardHover"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-bold text-ink-900">Your Progress</h3>
        <ChevronRight className="h-4 w-4 text-ink-300" strokeWidth={2.5} />
      </div>

      {status === "loading" ? (
        <div className="mt-3 h-10 animate-pulse rounded-lg bg-primary-50" />
      ) : (
        <div className="mt-3 flex gap-3">
          <div className="flex flex-1 items-center gap-2 rounded-lg bg-accent-50 px-2.5 py-2">
            <Flame className="h-4 w-4 shrink-0 text-accent-500" strokeWidth={2.25} />
            <div>
              <p className="text-sm font-extrabold leading-none text-ink-900">{summary.currentStreak}</p>
              <p className="mt-0.5 text-[10px] text-ink-400">Day streak</p>
            </div>
          </div>
          <div className="flex flex-1 items-center gap-2 rounded-lg bg-secondary-50 px-2.5 py-2">
            <Zap className="h-4 w-4 shrink-0 text-secondary-500" strokeWidth={2.25} fill="currentColor" />
            <div>
              <p className="text-sm font-extrabold leading-none text-ink-900">{summary.totalXP}</p>
              <p className="mt-0.5 text-[10px] text-ink-400">XP · {summary.currentLevel}</p>
            </div>
          </div>
        </div>
      )}
    </button>
  );
}
