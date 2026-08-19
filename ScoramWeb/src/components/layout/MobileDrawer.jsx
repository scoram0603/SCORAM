import {
  Home, Search, BookOpen, MessageCircle, ClipboardCheck, HelpCircle,
  MessageSquare, Trophy, Bookmark, BarChart3, User, Settings, LogOut, LogIn, X, Library,
} from "lucide-react";
import { NavLink } from "react-router-dom";
import logo from "../../assets/scoram-logo-horizontal.png";
import Footer from "./Footer";
import { API_BASE_URL } from "../../api/client";

const ICONS = {
  Home, Search, BookOpen, MessageCircle, ClipboardCheck, HelpCircle,
  MessageSquare, Trophy, Bookmark, BarChart3, User, Settings, Library,
};

function photoSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

function initialsFor(fullName) {
  if (!fullName) return "?";
  const parts = fullName.trim().split(/\s+/);
  return ((parts[0]?.[0] || "") + (parts[1]?.[0] || "")).toUpperCase() || fullName[0].toUpperCase();
}

// Mirrors Sidebar's nav + profile card for mobile, where there's no room for a
// permanent rail. BottomNav only fits 5 items, so this is the only way a phone
// user reaches Discussions, Leaderboard, Bookmarks, Progress, or Settings.
export default function MobileDrawer({ items, isOpen, onClose, isAuthenticated, user, onLogout, hasUnseenChat }) {
  return (
    <div
      className={`fixed inset-0 z-40 lg:hidden ${isOpen ? "" : "pointer-events-none"}`}
      aria-hidden={!isOpen}
    >
      <div
        onClick={onClose}
        className={`absolute inset-0 bg-ink-900/40 transition-opacity ${isOpen ? "opacity-100" : "opacity-0"}`}
      />

      <aside
        className={`absolute left-0 top-0 flex h-full w-[280px] flex-col overflow-y-auto bg-white shadow-floating transition-transform duration-200 ${
          isOpen ? "translate-x-0" : "-translate-x-full"
        }`}
      >
        <div className="flex items-center justify-between px-5 pb-2 pt-6">
          <img src={logo} alt="Scoram — Learn, Discuss, Score" className="h-9 w-auto object-contain" />
          <button
            type="button"
            onClick={onClose}
            aria-label="Close menu"
            className="flex h-9 w-9 items-center justify-center rounded-full text-ink-400 hover:bg-primary-50"
          >
            <X className="h-5 w-5" strokeWidth={2.25} />
          </button>
        </div>

        <nav className="flex flex-1 flex-col gap-1 px-3 py-4">
          {items.map((group, groupIndex) => (
            <div key={group.section ?? `group-${groupIndex}`} className={groupIndex > 0 ? "mt-3" : undefined}>
              {group.section && (
                <p className="px-3 pb-1.5 pt-1 text-[11px] font-bold uppercase tracking-wider text-ink-300">
                  {group.section}
                </p>
              )}
              {group.items.map((item) => {
                const Icon = ICONS[item.icon];
                return (
                  <NavLink
                    key={item.to}
                    to={item.to}
                    end={item.to === "/"}
                    onClick={onClose}
                    className={({ isActive }) =>
                      `flex items-center gap-3 rounded-xl px-3 py-2.5 text-[14px] font-medium transition-colors ${
                        isActive ? "bg-primary-600 text-white shadow-card" : "text-ink-600 hover:bg-primary-50"
                      }`
                    }
                  >
                    {({ isActive }) => (
                      <>
                        <span className="relative shrink-0">
                          <Icon className="h-5 w-5" strokeWidth={isActive ? 2.5 : 2} />
                          {item.to === "/chat" && hasUnseenChat && (
                            <span className="absolute -right-0.5 -top-0.5 h-2 w-2 rounded-full bg-red-500" />
                          )}
                        </span>
                        {item.label}
                      </>
                    )}
                  </NavLink>
                );
              })}
            </div>
          ))}
        </nav>

        <div className="m-3 rounded-xl2 border border-primary-100 bg-primary-50/60 p-4">
          {isAuthenticated ? (
            <>
              <div className="flex items-center gap-3">
                {user.photoUrl ? (
                  <img src={photoSrc(user.photoUrl)} alt={user.fullName} className="h-11 w-11 shrink-0 rounded-full object-cover" />
                ) : (
                  <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-primary-600 text-sm font-bold text-white">
                    {initialsFor(user.fullName)}
                  </span>
                )}
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-bold text-ink-900">{user.fullName}</span>
                  <span className="block truncate text-xs text-ink-400">{user.email}</span>
                </span>
              </div>
              <button
                type="button"
                onClick={() => {
                  onClose();
                  onLogout();
                }}
                className="mt-3 flex w-full items-center justify-center gap-1.5 rounded-lg bg-white px-3 py-2 text-xs font-semibold text-ink-600 shadow-card transition-colors hover:bg-red-50 hover:text-red-600"
              >
                <LogOut className="h-3.5 w-3.5" strokeWidth={2.25} />
                Log Out
              </button>
            </>
          ) : (
            <NavLink
              to="/login"
              onClick={onClose}
              className="flex w-full items-center justify-center gap-1.5 rounded-lg bg-primary-600 px-3 py-2 text-xs font-semibold text-white transition-colors hover:bg-primary-700"
            >
              <LogIn className="h-3.5 w-3.5" strokeWidth={2.25} />
              Log In
            </NavLink>
          )}
        </div>

        <Footer variant="sidebar" />
      </aside>
    </div>
  );
}
