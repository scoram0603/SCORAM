import { useEffect, useState } from "react";
import {
  LayoutDashboard, UploadCloud, FileStack, ClipboardCheck, ListChecks, Users, MessageSquare, LogOut,
  ShieldCheck, ShieldAlert, Lightbulb, Flag, Library, ChevronsLeft, ChevronsRight, PenLine, Trophy,
} from "lucide-react";
import { NavLink } from "react-router-dom";
import logoMark from "../../assets/scoram-logo-square.png";
import logo from "../../assets/scoram-logo-horizontal.png";
import { useAdminAuth } from "../context/AdminAuthContext";
import Footer from "../../components/layout/Footer";

const NAV_ITEMS = [
  { to: "/admin", end: true, label: "Dashboard", icon: LayoutDashboard },
  { to: "/admin/upload", label: "Upload PYQ", icon: UploadCloud },
  { to: "/admin/papers", label: "Uploaded Papers", icon: FileStack },
  { to: "/admin/review", label: "Review Queue", icon: ClipboardCheck, permission: "PublishPaper" },
  { to: "/admin/question-bank", label: "Question Bank", icon: Library, permission: "ManageQuestionBank" },
  { to: "/admin/question-bank/reports", label: "Question Reports", icon: Flag, permission: "ModerateQuestionReports" },
  { to: "/admin/practice-tests", label: "Practice Tests", icon: PenLine, permission: "ManageTests" },
  { to: "/admin/mock-tests", label: "Mock Tests", icon: Trophy, permission: "ManageTests" },
  { to: "/admin/solutions", label: "Solutions Queue", icon: Lightbulb, permission: "ModerateSolutions" },
  { to: "/admin/comment-reports", label: "Reported Comments", icon: Flag, permission: "ModerateDiscussions" },
  { to: "/admin/chat", label: "Group Chat", icon: MessageSquare },
  { to: "/admin/tasks", label: "Tasks", icon: ListChecks },
  { to: "/admin/audit-log", label: "Audit Log", icon: ShieldAlert, permission: "Audit" },
  { to: "/admin/admins", label: "Manage Admins", icon: Users, superAdminOnly: true },
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
        {NAV_ITEMS.filter((item) => {
          if (item.superAdminOnly && !isSuperAdmin) return false;
          if (item.permission && !hasPermission(item.permission)) return false;
          return true;
        }).map((item) => {
          const Icon = item.icon;
          return (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              title={collapsed ? item.label : undefined}
              className={({ isActive }) =>
                `flex items-center gap-3 rounded-xl px-3 py-2.5 text-[14px] font-medium transition-colors ${
                  collapsed ? "justify-center" : ""
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
