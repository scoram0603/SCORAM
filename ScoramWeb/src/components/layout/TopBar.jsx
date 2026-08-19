import { Search, SlidersHorizontal, LogIn } from "lucide-react";
import { Link } from "react-router-dom";
import NotificationBell from "./NotificationBell";

function initialsFor(fullName) {
  if (!fullName) return "?";
  const parts = fullName.trim().split(/\s+/);
  return ((parts[0]?.[0] || "") + (parts[1]?.[0] || "")).toUpperCase() || fullName[0].toUpperCase();
}

export default function TopBar({ query, onQueryChange, onSearchSubmit, onFiltersClick, isAuthenticated, user }) {
  return (
    <div className="sticky top-0 z-30 hidden items-center gap-4 border-b border-primary-100 bg-white/95 px-8 py-4 backdrop-blur lg:flex">
      <div className="shrink-0">
        <p className="text-lg font-bold text-ink-900">
          {isAuthenticated ? `Welcome back, ${user.fullName.split(" ")[0]}` : "Welcome back,"} 👋
        </p>
        <p className="text-sm text-ink-400">Let's crack today's practice set.</p>
      </div>

      <label className="relative mx-auto w-full max-w-xl">
        <span className="sr-only">Search exam, subject, topic</span>
        <Search className="pointer-events-none absolute left-4 top-1/2 h-[18px] w-[18px] -translate-y-1/2 text-ink-400" strokeWidth={2} />
        <input
          type="search"
          value={query}
          onChange={(e) => onQueryChange?.(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") onSearchSubmit?.(query);
          }}
          placeholder="Search exam, subject, topic..."
          className="h-12 w-full rounded-xl2 border border-primary-100 bg-surface pl-11 pr-4 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500 focus:bg-white"
        />
      </label>

      <div className="flex shrink-0 items-center gap-3">
        <button
          type="button"
          onClick={onFiltersClick}
          className="flex h-11 shrink-0 items-center gap-1.5 rounded-xl border border-primary-100 bg-white px-3.5 text-sm font-medium text-primary-600 transition-colors hover:bg-primary-50"
        >
          <SlidersHorizontal className="h-4 w-4" strokeWidth={2.25} />
          Filters
        </button>

        {isAuthenticated && <NotificationBell variant="desktop" />}

        {isAuthenticated ? (
          <span className="relative flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-primary-600 text-sm font-bold text-white">
            {initialsFor(user.fullName)}
            <span className="absolute -right-0.5 -bottom-0.5 h-3 w-3 rounded-full border-2 border-white bg-mint-500" />
          </span>
        ) : (
          <Link
            to="/login"
            className="flex h-11 shrink-0 items-center gap-1.5 rounded-xl bg-primary-600 px-4 text-sm font-semibold text-white transition-colors hover:bg-primary-700"
          >
            <LogIn className="h-4 w-4" strokeWidth={2.25} />
            Log In
          </Link>
        )}
      </div>
    </div>
  );
}
