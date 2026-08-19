import { useEffect, useState } from "react";
import {
  Home, Search, BookOpen, MessageCircle, ClipboardCheck, HelpCircle,
  MessageSquare, Trophy, Bookmark, BarChart3, User, Settings, LogOut, LogIn,
  ChevronsLeft, ChevronsRight, Library,
} from "lucide-react";
import { NavLink } from "react-router-dom";
import logo from "../../assets/scoram-logo-horizontal.png";
import logoMark from "../../assets/scoram-logo-square.png";
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

const COLLAPSE_KEY = "scoram_sidebar_collapsed";

function initialsFor(fullName) {
  if (!fullName) return "?";
  const parts = fullName.trim().split(/\s+/);
  return ((parts[0]?.[0] || "") + (parts[1]?.[0] || "")).toUpperCase() || fullName[0].toUpperCase();
}

export default function Sidebar({ items, isAuthenticated, user, onLogout, hasUnseenChat }) {
  // Persisted so the choice sticks across reloads/sessions, same idea as any real SaaS sidebar.
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(COLLAPSE_KEY) === "1");

  useEffect(() => {
    localStorage.setItem(COLLAPSE_KEY, collapsed ? "1" : "0");
  }, [collapsed]);

  return (
    <aside
      className={`sticky top-0 hidden h-screen shrink-0 flex-col overflow-y-auto overflow-x-hidden border-r border-primary-100 bg-white transition-[width] duration-200 lg:flex ${
        collapsed ? "w-[76px]" : "w-[280px]"
      }`}
    >
      <div className={`flex items-center pb-2 pt-6 ${collapsed ? "justify-center px-2" : "justify-between px-5"}`}>
        {collapsed ? (
          <img src={logoMark} alt="Scoram" className="h-8 w-8 object-contain" />
        ) : (
          <img src={logo} alt="Scoram — Learn, Discuss, Score" className="h-9 w-auto object-contain" />
        )}

        {!collapsed && (
          <button
            type="button"
            onClick={() => setCollapsed(true)}
            aria-label="Collapse sidebar"
            className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg text-ink-400 transition-colors hover:bg-primary-50 hover:text-ink-600"
          >
            <ChevronsLeft className="h-4 w-4" strokeWidth={2.25} />
          </button>
        )}
      </div>

      {collapsed && (
        <button
          type="button"
          onClick={() => setCollapsed(false)}
          aria-label="Expand sidebar"
          className="mx-auto mt-2 flex h-7 w-7 items-center justify-center rounded-lg text-ink-400 transition-colors hover:bg-primary-50 hover:text-ink-600"
        >
          <ChevronsRight className="h-4 w-4" strokeWidth={2.25} />
        </button>
      )}

      <nav className="flex flex-1 flex-col gap-1 px-3 py-4">
        {items.map((group, groupIndex) => (
          <div key={group.section ?? `group-${groupIndex}`} className={groupIndex > 0 ? "mt-3" : undefined}>
            {group.section && !collapsed && (
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
                  title={collapsed ? item.label : undefined}
                  className={({ isActive }) =>
                    `flex items-center gap-3 rounded-xl px-3 py-2.5 text-[14px] font-medium transition-colors ${
                      collapsed ? "justify-center" : ""
                    } ${
                      isActive
                        ? "bg-primary-600 text-white shadow-card"
                        : item.highlight === "accent"
                        ? "bg-accent-50 text-accent-600 hover:bg-accent-100"
                        : item.highlight === "mint"
                        ? "bg-mint-50 text-mint-500 hover:bg-mint-100"
                        : "text-ink-600 hover:bg-primary-50"
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
                      {!collapsed && item.label}
                    </>
                  )}
                </NavLink>
              );
            })}
          </div>
        ))}
      </nav>

      {/* Profile card — real auth state from AuthContext, no mocked XP/level for a real account */}
      <div className={`m-3 rounded-xl2 border border-primary-100 bg-primary-50/60 ${collapsed ? "p-2" : "p-4"}`}>
        {isAuthenticated ? (
          <>
            <div className={`flex items-center gap-3 ${collapsed ? "justify-center" : ""}`}>
              {user.photoUrl ? (
                <img
                  src={photoSrc(user.photoUrl)}
                  alt={user.fullName}
                  title={collapsed ? user.fullName : undefined}
                  className="h-11 w-11 shrink-0 rounded-full object-cover"
                />
              ) : (
                <span
                  className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-primary-600 text-sm font-bold text-white"
                  title={collapsed ? user.fullName : undefined}
                >
                  {initialsFor(user.fullName)}
                </span>
              )}
              {!collapsed && (
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-bold text-ink-900">{user.fullName}</span>
                  <span className="block truncate text-xs text-ink-400">{user.email}</span>
                </span>
              )}
            </div>

            <button
              type="button"
              onClick={onLogout}
              title={collapsed ? "Log Out" : undefined}
              className={`mt-3 flex w-full items-center justify-center gap-1.5 rounded-lg bg-white px-3 py-2 text-xs font-semibold text-ink-600 shadow-card transition-colors hover:bg-red-50 hover:text-red-600`}
            >
              <LogOut className="h-3.5 w-3.5 shrink-0" strokeWidth={2.25} />
              {!collapsed && "Log Out"}
            </button>
          </>
        ) : (
          <>
            <div className={`flex items-center gap-3 ${collapsed ? "justify-center" : ""}`}>
              <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-primary-100 text-sm font-bold text-primary-600">
                <User className="h-5 w-5" strokeWidth={2} />
              </span>
              {!collapsed && (
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-bold text-ink-900">Guest</span>
                  <span className="block truncate text-xs text-ink-400">Log in to save your progress</span>
                </span>
              )}
            </div>

            <NavLink
              to="/login"
              title={collapsed ? "Log In" : undefined}
              className="mt-3 flex w-full items-center justify-center gap-1.5 rounded-lg bg-primary-600 px-3 py-2 text-xs font-semibold text-white transition-colors hover:bg-primary-700"
            >
              <LogIn className="h-3.5 w-3.5 shrink-0" strokeWidth={2.25} />
              {!collapsed && "Log In"}
            </NavLink>
          </>
        )}
      </div>

      {!collapsed && <Footer variant="sidebar" />}
    </aside>
  );
}
