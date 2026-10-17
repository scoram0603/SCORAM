import { useEffect, useMemo, useState } from "react";
import {
  LayoutDashboard, UploadCloud, FileStack, ClipboardCheck, ListChecks, Users, MessageSquare, LogOut,
  ShieldCheck, ShieldAlert, Lightbulb, Flag, Library, ChevronsLeft, ChevronsRight, PenLine, Trophy,
  GraduationCap, Zap, Building2, LockKeyhole, BookOpen, MessageSquareText, Layers, PencilRuler,
  BarChart3, UserCog, FileDown, Megaphone, SlidersHorizontal, MonitorSmartphone, LifeBuoy, HeartPulse,
  ShieldQuestion, Fingerprint, ChevronDown, Search, X, Gavel,
} from "lucide-react";
import { NavLink, useLocation } from "react-router-dom";
import logoMark from "../../assets/scoram-logo-square.png";
import logo from "../../assets/scoram-logo-horizontal.png";
import { useAdminAuth } from "../context/AdminAuthContext";
import { getBadges } from "../api/analytics";
import Footer from "../../components/layout/Footer";

// ADMIN NAVIGATION v2 -- grouped, collapsible, searchable, permission-aware, with unresolved-work badges.
//
// EVERY pre-existing route is still here under the same URL (only the grouping/labels changed), so no
// bookmark or link breaks. New entries point at the new pages. `permission` / `superAdminOnly` only decide
// what is SHOWN -- the API enforces the same rule on every call regardless of what this menu displays.
//
// `anyOf` shows an item when the admin holds at least one of the listed permissions (used by the
// Moderation overview, which lists whichever queues the admin can open).
//
// `badge` names a key in GET /api/admin/analytics/badges; the server only returns counts for queues the
// admin is allowed to open, so a badge can never reveal work an admin has no access to.
const NAV_GROUPS = [
  {
    id: "overview",
    section: "Overview",
    items: [
      { to: "/admin", end: true, label: "Dashboard", icon: LayoutDashboard },
      { to: "/admin/analytics", label: "Analytics", icon: BarChart3, permission: "ViewAnalytics" },
    ],
  },
  {
    id: "content",
    section: "Exam Content",
    items: [
      { to: "/admin/question-bank", label: "PYQs", icon: Library, permission: "ManageQuestionBank" },
      { subheader: "PYP Management" },
      { to: "/admin/upload", label: "Add Paper", icon: UploadCloud, indent: true },
      { to: "/admin/papers", label: "All Papers", icon: FileStack, indent: true },
      { to: "/admin/shared-content", label: "Shared Content", icon: Layers, indent: true, permission: "EditPaper" },
      { to: "/admin/bulk-editor", label: "Bulk Question Editor", icon: PencilRuler, indent: true, permission: "EditPaper" },
      { to: "/admin/exams", label: "Manage Exams", icon: GraduationCap },
      { to: "/admin/subjects", label: "Subjects", icon: BookOpen, permission: "ManageSubjects" },
    ],
  },
  {
    id: "tests",
    section: "Tests & Learning",
    items: [
      { to: "/admin/practice-tests", label: "Practice Tests", icon: PenLine, permission: "ManageTests" },
      { to: "/admin/mock-tests", label: "Mock Tests", icon: Trophy, permission: "ManageTests" },
      { to: "/admin/quizzes", label: "Quizzes", icon: Zap, permission: "ManageTests" },
    ],
  },
  {
    id: "community",
    section: "Users & Community",
    items: [
      { to: "/admin/users", label: "Students", icon: Users, permission: "ManageUsers" },
      { to: "/admin/chat", label: "Group Chat", icon: MessageSquare },
      { to: "/admin/support", label: "Support Tickets", icon: LifeBuoy, permission: "ManageFeedback", badge: "support" },
      { to: "/admin/feedback", label: "Feedback (classic)", icon: MessageSquareText, permission: "ManageFeedback" },
    ],
  },
  {
    id: "review",
    section: "Review & Quality",
    items: [
      { to: "/admin/moderation", label: "Moderation Overview", icon: Gavel, anyOf: ["PublishPaper", "ModerateQuestionReports", "ModerateSolutions", "ModerateDiscussions", "HandleChatReports"] },
      { to: "/admin/review", label: "Review Queue", icon: ClipboardCheck, permission: "PublishPaper", badge: "review" },
      { to: "/admin/question-bank/reports", label: "Question Reports", icon: Flag, permission: "ModerateQuestionReports", badge: "questionReports" },
      { to: "/admin/solutions", label: "Solutions Queue", icon: Lightbulb, permission: "ModerateSolutions", badge: "solutions" },
      { to: "/admin/comment-reports", label: "Reported Comments", icon: Flag, permission: "ModerateDiscussions", badge: "commentReports" },
    ],
  },
  {
    id: "business",
    section: "Business",
    items: [
      // "Partner management" in the checklist maps to the existing organisations (coaching institutes / boards).
      { to: "/admin/organizations", label: "Partners / Organizations", icon: Building2 },
      { to: "/admin/reports", label: "Reports & Exports", icon: FileDown, permission: "ExportReports" },
    ],
  },
  {
    id: "communication",
    section: "Communication",
    items: [
      { to: "/admin/notifications", label: "Notifications", icon: Megaphone, permission: "PostNotices" },
    ],
  },
  {
    id: "administration",
    section: "Administration",
    items: [
      { to: "/admin/tasks", label: "Tasks", icon: ListChecks },
      { to: "/admin/app-config", label: "App Configuration", icon: SlidersHorizontal, permission: "ManageAppConfig" },
      { to: "/admin/system-health", label: "System Health", icon: HeartPulse, permission: "ViewAnalytics" },
      { to: "/admin/audit-log", label: "Audit Log", icon: ShieldAlert, permission: "Audit" },
      { to: "/admin/admins", label: "Manage Admins", icon: UserCog, superAdminOnly: true },
      { to: "/admin/roles", label: "Roles & Permissions", icon: ShieldQuestion, superAdminOnly: true },
      { to: "/admin/sessions", label: "Admin Sessions", icon: MonitorSmartphone, superAdminOnly: true },
    ],
  },
  {
    id: "account",
    section: "Account",
    items: [
      { to: "/admin/mfa-settings", label: "Two-Factor Auth", icon: Fingerprint },
      // Any admin can rotate their own password; the backend takes identity from the JWT.
      { to: "/admin/security", label: "Security", icon: LockKeyhole },
    ],
  },
];

const COLLAPSE_KEY = "scoram_admin_sidebar_collapsed";
const GROUPS_KEY = "scoram_admin_sidebar_groups_closed";
const BADGE_REFRESH_MS = 90_000;

function readStore(key, fallback) {
  try {
    const raw = localStorage.getItem(key);
    return raw === null ? fallback : raw;
  } catch {
    return fallback;
  }
}
function writeStore(key, value) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // storage unavailable -- the menu just won't remember its state
  }
}

function initialsFor(fullName) {
  if (!fullName) return "?";
  const parts = fullName.trim().split(/\s+/);
  return ((parts[0]?.[0] || "") + (parts[1]?.[0] || "")).toUpperCase() || fullName[0].toUpperCase();
}

function Badge({ count }) {
  if (!count) return null;
  return (
    <span className="ml-auto min-w-[20px] rounded-full bg-accent-500 px-1.5 py-0.5 text-center text-[10px] font-bold leading-none text-white" aria-label={`${count} waiting`}>
      {count > 99 ? "99+" : count}
    </span>
  );
}

// `mobile`: rendered inside the slide-over drawer on small screens (never collapsed to icons there).
export default function AdminSidebar({ mobile = false, onNavigate }) {
  const { admin, token, isSuperAdmin, hasPermission, logout } = useAdminAuth();
  const location = useLocation();
  const [collapsedPref, setCollapsed] = useState(() => readStore(COLLAPSE_KEY, "0") === "1");
  const collapsed = mobile ? false : collapsedPref;
  const [closedGroups, setClosedGroups] = useState(() => {
    try {
      return new Set(JSON.parse(readStore(GROUPS_KEY, "[]")));
    } catch {
      return new Set();
    }
  });
  const [query, setQuery] = useState("");
  const [badges, setBadges] = useState({});

  useEffect(() => {
    writeStore(COLLAPSE_KEY, collapsedPref ? "1" : "0");
  }, [collapsedPref]);

  useEffect(() => {
    writeStore(GROUPS_KEY, JSON.stringify([...closedGroups]));
  }, [closedGroups]);

  // Unresolved-work counters. Failures are silent: a missing badge must never break navigation.
  useEffect(() => {
    if (!token) return undefined;
    let cancelled = false;
    const load = () => {
      if (document.hidden) return;
      getBadges(token).then((b) => { if (!cancelled) setBadges(b || {}); }).catch(() => {});
    };
    load();
    const id = setInterval(load, BADGE_REFRESH_MS);
    return () => { cancelled = true; clearInterval(id); };
  }, [token]);

  const visible = (item) => {
    if (item.superAdminOnly && !isSuperAdmin) return false;
    if (item.permission && !hasPermission(item.permission)) return false;
    if (item.anyOf && !item.anyOf.some((p) => hasPermission(p))) return false;
    return true;
  };

  const needle = query.trim().toLowerCase();

  const groups = useMemo(() => {
    return NAV_GROUPS.map((group) => {
      let items = group.items.filter((item) => item.subheader || visible(item));
      if (needle) {
        items = items.filter((item) => !item.subheader && (item.label.toLowerCase().includes(needle) || group.section.toLowerCase().includes(needle)));
      }
      const real = items.filter((i) => !i.subheader);
      return { ...group, items, hasItems: real.length > 0 };
    }).filter((g) => g.hasItems);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [needle, admin, isSuperAdmin]);

  // A group that contains the current page is always shown open, so you never land on a page whose menu
  // entry is hidden inside a collapsed section.
  const containsCurrent = (group) =>
    group.items.some((i) => i.to && (i.end ? location.pathname === i.to : location.pathname === i.to || location.pathname.startsWith(i.to + "/")));

  const toggleGroup = (id) =>
    setClosedGroups((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  return (
    <aside
      aria-label="Admin navigation"
      className={`flex h-screen shrink-0 flex-col overflow-y-auto overflow-x-hidden bg-primary-900 transition-[width] duration-200 ${
        mobile ? "w-[280px]" : `sticky top-0 ${collapsed ? "w-[76px]" : "w-[260px]"}`
      }`}
    >
      <div className={`flex items-center pb-2 pt-6 ${collapsed ? "justify-center px-2" : "justify-between px-5"}`}>
        {collapsed ? (
          <div className="rounded-lg bg-white p-1">
            <img src={logoMark} alt="Scoram" className="h-6 w-6 object-contain" />
          </div>
        ) : (
          <div className="flex items-center gap-2">
            {/* The source PNG has an opaque white background, hence the white card (see AdminLogin.jsx). */}
            <div className="rounded-lg bg-white px-2 py-1">
              <img src={logo} alt="Scoram" className="h-6 w-auto object-contain" />
            </div>
            <span className="rounded-md bg-white/10 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wide text-primary-100">
              Admin
            </span>
          </div>
        )}

        {!collapsed && !mobile && (
          <button
            type="button"
            onClick={() => setCollapsed(true)}
            aria-label="Collapse sidebar"
            className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg text-primary-100 transition-colors hover:bg-white/10"
          >
            <ChevronsLeft className="h-4 w-4" strokeWidth={2.25} />
          </button>
        )}
        {mobile && (
          <button
            type="button"
            onClick={onNavigate}
            aria-label="Close menu"
            className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-primary-100 transition-colors hover:bg-white/10"
          >
            <X className="h-4 w-4" strokeWidth={2.25} />
          </button>
        )}
      </div>

      {collapsed && (
        <button
          type="button"
          onClick={() => setCollapsed(false)}
          aria-label="Expand sidebar"
          className="mx-auto mt-2 flex h-7 w-7 items-center justify-center rounded-lg text-primary-100 transition-colors hover:bg-white/10"
        >
          <ChevronsRight className="h-4 w-4" strokeWidth={2.25} />
        </button>
      )}

      {!collapsed && (
        <div className="px-3 pt-3">
          <label className="relative block">
            <span className="sr-only">Search menu</span>
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-primary-100/60" strokeWidth={2.25} />
            <input
              type="search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search menu…"
              className="w-full rounded-xl bg-white/10 py-2 pl-9 pr-3 text-[13px] text-white placeholder:text-primary-100/60 focus:bg-white/15 focus:outline-none"
            />
          </label>
        </div>
      )}

      <nav className="flex flex-1 flex-col gap-1 px-3 py-4">
        {groups.length === 0 && !collapsed && (
          <p className="px-3 py-2 text-xs text-primary-100/60">No menu items match “{query}”.</p>
        )}

        {groups.map((group, groupIndex) => {
          const open = Boolean(needle) || collapsed || !closedGroups.has(group.id) || containsCurrent(group);
          const groupBadge = group.items.reduce((sum, i) => sum + (i.badge && badges[i.badge] ? badges[i.badge] : 0), 0);

          return (
            <div key={group.id} className={groupIndex > 0 ? "mt-2" : undefined}>
              {!collapsed && (
                <button
                  type="button"
                  onClick={() => toggleGroup(group.id)}
                  aria-expanded={open}
                  className="flex w-full items-center gap-1 rounded-lg px-3 pb-1.5 pt-1 text-left text-[11px] font-bold uppercase tracking-wider text-primary-100/50 transition-colors hover:text-primary-100"
                >
                  <span>{group.section}</span>
                  {!open && groupBadge > 0 && <span className="ml-1 h-1.5 w-1.5 rounded-full bg-accent-500" aria-label="Has items waiting" />}
                  <ChevronDown className={`ml-auto h-3.5 w-3.5 transition-transform ${open ? "" : "-rotate-90"}`} strokeWidth={2.5} />
                </button>
              )}

              {open &&
                group.items.map((item) => {
                  if (item.subheader) {
                    return collapsed ? null : (
                      <p key={item.subheader} className="px-3 pb-1 pt-2 text-[11px] font-semibold text-primary-100/70">
                        {item.subheader}
                      </p>
                    );
                  }

                  const Icon = item.icon;
                  const count = item.badge ? badges[item.badge] : 0;
                  return (
                    <NavLink
                      key={item.to}
                      to={item.to}
                      end={item.end}
                      onClick={mobile ? onNavigate : undefined}
                      title={collapsed ? item.label : undefined}
                      className={({ isActive }) =>
                        `relative flex items-center gap-3 rounded-xl py-2.5 text-[14px] font-medium transition-colors ${
                          collapsed ? "justify-center px-3" : item.indent ? "pl-7 pr-3" : "px-3"
                        } ${isActive ? "bg-white text-primary-700 shadow-card" : "text-primary-100 hover:bg-white/10"}`
                      }
                    >
                      {({ isActive }) => (
                        <>
                          <Icon className="h-5 w-5 shrink-0" strokeWidth={isActive ? 2.5 : 2} />
                          {!collapsed && <span className="truncate">{item.label}</span>}
                          {!collapsed && <Badge count={count} />}
                          {collapsed && count > 0 && <span className="absolute right-1.5 top-1.5 h-2 w-2 rounded-full bg-accent-500" aria-label={`${count} waiting`} />}
                        </>
                      )}
                    </NavLink>
                  );
                })}
            </div>
          );
        })}
      </nav>

      <div className={`m-3 rounded-xl2 bg-white/10 ${collapsed ? "p-2" : "p-4"}`}>
        <div className={`flex items-center gap-3 ${collapsed ? "justify-center" : ""}`}>
          <span
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-white text-sm font-bold text-primary-700"
            title={collapsed ? admin?.fullName : undefined}
          >
            {initialsFor(admin?.fullName)}
          </span>
          {!collapsed && (
            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-bold text-white">{admin?.fullName}</span>
              <span className="flex items-center gap-1 text-xs text-primary-100">
                <ShieldCheck className="h-3 w-3" strokeWidth={2.5} />
                {admin?.role}
              </span>
            </span>
          )}
        </div>

        <button
          type="button"
          onClick={logout}
          title={collapsed ? "Log Out" : undefined}
          className="mt-3 flex w-full items-center justify-center gap-1.5 rounded-lg bg-white/10 px-3 py-2 text-xs font-semibold text-white transition-colors hover:bg-red-500/80"
        >
          <LogOut className="h-3.5 w-3.5 shrink-0" strokeWidth={2.25} />
          {!collapsed && "Log Out"}
        </button>
      </div>

      {!collapsed && <Footer variant="sidebar-dark" />}
    </aside>
  );
}
