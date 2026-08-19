import { NavLink } from "react-router-dom";
import { PenLine, Trophy, History } from "lucide-react";

export default function Tests() {
  return (
    <div className="px-4 pb-8 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <h1 className="text-xl font-extrabold text-ink-900 sm:text-2xl">Tests</h1>
      <p className="mt-1 text-sm text-ink-400">Practice at your own pace, or take on a full exam simulation.</p>

      <div className="mt-5 grid grid-cols-1 gap-4 sm:grid-cols-2">
        <NavLink
          to="/tests/practice"
          className="group rounded-xl2 border border-primary-100 bg-white p-5 shadow-card transition-colors hover:border-primary-300"
        >
          <span className="flex h-11 w-11 items-center justify-center rounded-xl2 bg-mint-50 text-mint-500">
            <PenLine className="h-5 w-5" strokeWidth={2.25} />
          </span>
          <h2 className="mt-3 text-[15px] font-bold text-ink-900">Practice Tests</h2>
          <p className="mt-1 text-sm text-ink-400">
            Choose a subject, topic or exam and practice questions at your own pace.
          </p>
        </NavLink>

        <NavLink
          to="/tests/mock"
          className="group rounded-xl2 border border-primary-100 bg-white p-5 shadow-card transition-colors hover:border-primary-300"
        >
          <span className="flex h-11 w-11 items-center justify-center rounded-xl2 bg-accent-50 text-accent-600">
            <Trophy className="h-5 w-5" strokeWidth={2.25} />
          </span>
          <h2 className="mt-3 text-[15px] font-bold text-ink-900">Mock Tests</h2>
          <p className="mt-1 text-sm text-ink-400">
            Experience the real exam pattern with timed mock tests.
          </p>
        </NavLink>
      </div>

      <NavLink
        to="/tests/my"
        className="mt-4 flex items-center gap-2 rounded-xl2 border border-primary-100 bg-white px-4 py-3 text-sm font-semibold text-ink-600 shadow-card transition-colors hover:border-primary-300"
      >
        <History className="h-4 w-4 text-ink-400" strokeWidth={2.25} />
        My Tests — in-progress &amp; past attempts
      </NavLink>
    </div>
  );
}
