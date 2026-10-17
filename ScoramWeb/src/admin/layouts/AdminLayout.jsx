import { useEffect, useState } from "react";
import { Menu } from "lucide-react";
import { Outlet, useLocation } from "react-router-dom";
import AdminSidebar from "../components/AdminSidebar";

// Desktop (lg+): sticky sidebar. Below lg: a slim top bar with a menu button that opens the same
// navigation in a slide-over drawer (closes on navigation, on backdrop click and on Escape).
export default function AdminLayout() {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const location = useLocation();

  useEffect(() => {
    setDrawerOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    if (!drawerOpen) return undefined;
    const onKey = (e) => e.key === "Escape" && setDrawerOpen(false);
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [drawerOpen]);

  return (
    <div className="flex min-h-screen bg-surface">
      <div className="hidden lg:block">
        <AdminSidebar />
      </div>

      {drawerOpen && (
        <div className="fixed inset-0 z-40 lg:hidden" role="dialog" aria-modal="true" aria-label="Admin menu">
          <button type="button" className="absolute inset-0 bg-black/50" aria-label="Close menu" onClick={() => setDrawerOpen(false)} />
          <div className="relative z-10 h-full w-[280px] max-w-[85vw]">
            <AdminSidebar mobile onNavigate={() => setDrawerOpen(false)} />
          </div>
        </div>
      )}

      <main className="min-w-0 flex-1">
        <div className="sticky top-0 z-30 flex items-center gap-3 border-b border-primary-100 bg-white px-4 py-3 lg:hidden">
          <button
            type="button"
            onClick={() => setDrawerOpen(true)}
            aria-label="Open menu"
            className="flex h-9 w-9 items-center justify-center rounded-lg text-primary-600 hover:bg-primary-50"
          >
            <Menu className="h-5 w-5" strokeWidth={2.25} />
          </button>
          <span className="text-sm font-extrabold text-primary-600">SCORAM Admin</span>
        </div>
        <Outlet />
      </main>
    </div>
  );
}
