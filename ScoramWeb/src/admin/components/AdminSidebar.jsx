import { useEffect, useState } from "react";
import {
  LayoutDashboard, UploadCloud, FileStack, ClipboardCheck, ListChecks, Users, MessageSquare, LogOut,
  ShieldCheck, ShieldAlert, Lightbulb, Flag, Library, ChevronsLeft, ChevronsRight, PenLine, Trophy,
  GraduationCap, Zap,
} from "lucide-react";
import { NavLink } from "react-router-dom";
import logoMark from "../../assets/scoram-logo-square.png";
import logo from "../../assets/scoram-logo-horizontal.png";
import { useAdminAuth } from "../context/AdminAuthContext";
import Footer from "../../components/layout/Footer";

// MASTER PROMPT -- Admin Navigation Redesign: grouped into Overview/Content/Review & Moderation/
// Tests/Community/Operations (the target information architecture) instead of one long flat list.
// No routes changed -- only how they're labelled/grouped, per "rename the navigation label while
// keeping the existing internal route" (safer than a route migration).
//
// "PYP Management" below is a small indented sub-heading (not a real route of its own) grouping
// the two existing paper-management routes -- previously two unrelated-looking top-level items
// ("Upload PYQ" / "Uploaded Papers"), now visibly one feature area with two entry points, matching
// student-facing "Previous Year Paper Practice" terminology instead of the old "PYQ" wording.
const NAV_GROUPS = [
  {
    section: "Overview",
    items: [
      { to: "/admin", end: true, label: "Dashboard", icon: LayoutDashboard },
    ],
  },
  {
    section: "Content",
    items: [
      { to: "/admin/question-bank", label: "PYQs", icon: Library, permission: "ManageQuestionBank" },
      { subheader: "PYP Management" },
      { to: "/admin/upload", label: "Add Paper", icon: UploadCloud, indent: true },
      { to: "/admin/papers", label: "All Papers", icon: FileStack, indent: true },
      { to: "/admin/exams", label: "Manage Exams", icon: GraduationCap },
    ],
  },
  {
    section: "Review & Moderation",
    items: [
      { to: "/admin/review", label: "Review Queue", icon: ClipboardCheck, permission: "PublishPaper" },
      { to: "/admin/question-bank/reports", label: "Question Reports", icon: Flag, permission: "ModerateQuestionReports" },
      { to: "/admin/solutions", label: "Solutions Queue", icon: Lightbulb, permission: "ModerateSolutions" },
      { to: "/admin/comment-reports", label: "Reported Comments", icon: Flag, permission: "ModerateDiscussions" },
    ],
  },
  {
    section: "Tests",
    items: [
      { to: "/admin/practice-tests", label: "Practice Tests", icon: PenLine, permission: "ManageTests" },
      { to: "/admin/mock-tests", label: "Mock Tests", icon: Trophy, permission: "ManageTests" },
      { to: "/admin/quizzes", label: "Quizzes", icon: Zap, permission: "ManageTests" },
    ],
  },
  {
    section: "Community",
    items: [
      { to: "/admin/chat", label: "Group Chat", icon: MessageSquare },
    ],
  },
  {
    section: "Operations",
    items: [
      { to: "/admin/tasks", label: "Tasks", icon: ListChecks },
      { to: "/admin/audit-log", label: "Audit Log", icon: ShieldAlert, permission: "Audit" },
      { to: "/admin/admins", label: "Manage Admins", icon: Users, superAdminOnly: true },
    ],
  },
];

const COLLAPSE_KEY = "scoram_admin_sidebar_collapsed";

function initialsFor(fullName) {
  if (!fullName) return "?";
  const parts = fullName.trim().split(/\s+/);
  return ((parts[0]?.[0] || "") + (parts[1]?.[0] || "")).toUpperCase() || fullName[0].toUpperCase();
}

export default function AdminSidebar() {
  const { admin, isSuperAdmin, hasPermission, logout } = useAdminAuth();
  // Persisted separately from the student sidebar's own collapse state (different key) so
  // collapsing one doesn't affect the other for anyone testing both in the same browser.
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(COLLAPSE_KEY) === "1");

  useEffect(() => {
    localStorage.setItem(COLLAPSE_KEY, collapsed ? "1" : "0");
  }, [collapsed]);

  const visible = (item) => {
    if (item.superAdminOnly && !isSuperAdmin) return false;
    if (item.permission && !hasPermission(item.permission)) return false;
    return true;
  };

  return (
    <aside
      className={`sticky top-0 flex h-screen shrink-0 flex-col overflow-y-auto overflow-x-hidden bg-primary-900 transition-[width] duration-200 ${
        collapsed ? "w-[76px]" : "w-[260px]"
      }`}
    >
      <div className={`flex items-center pb-2 pt-6 ${collapsed ? "justify-center px-2" : "justify-between px-5"}`}>
        {collapsed ? (
          <div className="rounded-lg bg-white p-1">
            <img src={logoMark} alt="Scoram" className="h-6 w-6 object-contain" />
          </div>
        ) : (
          <div className="flex items-center gap-2">
            {/* See AdminLogin.jsx for why this sits on a white card instead of using
                brightness-0/invert -- the source PNG has an opaque white background, not a
                transparent one, so inverting it used to produce an invisible white-on-navy blob. */}
            <div className="rounded-lg bg-white px-2 py-1">
              <img src={logo} alt="Scoram" className="h-6 w-auto object-contain" />
            </div>
            <span className="rounded-md bg-white/10 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wide text-primary-100">
              Admin
            </span>
          </div>
        )}

        {!collapsed && (
          <button
            type="button"
            onClick={() => setCollapsed(true)}
            aria-label="Collapse sidebar"
            className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg text-primary-100 transition-colors hover:bg-white/10"
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
          className="mx-auto mt-2 flex h-7 w-7 items-center justify-center rounded-lg text-primary-100 transition-colors hover:bg-white/10"
        >
          <ChevronsRight className="h-4 w-4" strokeWidth={2.25} />
        </button>
      )}

      <nav className="flex flex-1 flex-col gap-1 px-3 py-4">
        {NAV_GROUPS.map((group, groupIndex) => {
          const groupItems = group.items.filter((item) => item.subheader || visible(item));
          // A group can end up empty once permission-gated items are filtered out (e.g. a
          // moderator with no Test-management permission sees no "Tests" section at all) --
          // skip it entirely rather than rendering a heading over nothing.
          if (groupItems.every((item) => item.subheader)) return null;

          return (
            <div key={group.section} className={groupIndex > 0 ? "mt-3" : undefined}>
              {!collapsed && (
                <p className="px-3 pb-1.5 pt-1 text-[11px] font-bold uppercase tracking-wider text-primary-100/50">
                  {group.section}
                </p>
              )}
              {groupItems.map((item) => {
                if (item.subheader) {
                  return collapsed ? null : (
                    <p key={item.subheader} className="px-3 pb-1 pt-2 text-[11px] font-semibold text-primary-100/70">
                      {item.subheader}
                    </p>
                  );
                }

                const Icon = item.icon;
                return (
                  <NavLink
                    key={item.to}
                    to={item.to}
                    end={item.end}
                    title={collapsed ? item.label : undefined}
                    className={({ isActive }) =>
                      `flex items-center gap-3 rounded-xl py-2.5 text-[14px] font-medium transition-colors ${
                        collapsed ? "justify-center px-3" : item.indent ? "pl-7 pr-3" : "px-3"
                      } ${isActive ? "bg-white text-primary-700 shadow-card" : "text-primary-100 hover:bg-white/10"}`
                    }
                  >
                    {({ isActive }) => (
                      <>
                        <Icon className="h-5 w-5 shrink-0" strokeWidth={isActive ? 2.5 : 2} />
                        {!collapsed && item.label}
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
