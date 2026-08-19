import { Flame, Zap, ArrowRight } from "lucide-react";
import { streakPreview } from "../../data/mockData";

// Mock — see the note at the top of mockData.js. Gamification isn't wired to
// the backend yet, so this card shows placeholder numbers rather than real ones.
export default function StreakXPCard() {
  return (
    <div className="px-4 pb-6 sm:px-6 lg:hidden">
      <div className="flex items-center gap-4 rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
        <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-accent-50 text-accent-500">
          <Flame className="h-6 w-6" strokeWidth={2.25} />
        </span>

        <div className="flex-1">
          <p className="text-sm font-bold text-ink-900">{streakPreview.streakDays} Day Streak</p>
          <p className="mt-0.5 flex items-center gap-1 text-xs font-semibold text-secondary-500">
            <Zap className="h-3.5 w-3.5" strokeWidth={2.25} fill="currentColor" />+{streakPreview.streakXP} XP
          </p>
        </div>

        <button
          type="button"
          className="flex shrink-0 items-center gap-1 rounded-lg bg-primary-50 px-3 py-2 text-xs font-semibold text-primary-600 transition-colors hover:bg-primary-100"
        >
          View Progress
          <ArrowRight className="h-3.5 w-3.5" strokeWidth={2.5} />
        </button>
      </div>
    </div>
  );
}
