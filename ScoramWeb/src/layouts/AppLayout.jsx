import { useEffect, useState } from "react";
import { Navigate, Outlet, useLocation, useNavigate } from "react-router-dom";
import Header from "../components/layout/Header";
import Sidebar from "../components/layout/Sidebar";
import BottomNav from "../components/layout/BottomNav";
import MobileDrawer from "../components/layout/MobileDrawer";
import ChatNotificationBridge from "../components/layout/ChatNotificationBridge";
import Footer from "../components/layout/Footer";
import Landing from "../pages/Landing";
import { useAuth } from "../context/AuthContext";
import { useMyExams } from "../context/MyExamsContext";
import { sidebarNavItems, bottomNavItems } from "../data/mockData";

export default function AppLayout() {
  const { isAuthenticated, user, logout } = useAuth();
  const { hasLoaded, hasConfigured } = useMyExams();
  const navigate = useNavigate();
  const location = useLocation();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [hasUnseenChat, setHasUnseenChat] = useState(false);

  // Clears the moment the person actually looks at Chat -- not before, so the dot persists
  // across every other page until they do.
  useEffect(() => {
    if (location.pathname.startsWith("/chat")) setHasUnseenChat(false);
  }, [location.pathname]);

  // PUBLIC LANDING PAGE -- a signed-out visitor hitting "/" gets the marketing landing page
  // (its own navbar/footer, no app chrome) instead of the student home feed. Anyone signed in
  // is completely unaffected here or on any other route -- Home.jsx, the index route mapping in
  // App.jsx, and every other page in this layout are untouched.
  if (!isAuthenticated && location.pathname === "/") {
    return <Landing />;
  }

  // "MY EXAMS" onboarding (spec section 4) -- once MyExamsContext has actually finished checking
  // (hasLoaded), an authenticated student with zero exams configured is sent to "What are you
  // preparing for?" before anything else, from any route they land on (a fresh login, a deep
  // link, reopening the app). Checked before the fullscreen-attempt branch below since a test
  // attempt link itself would never be reachable without exams configured in the first place.
  // Excludes /select-exams itself (avoid a redirect loop) and /my-exams (a student already deep
  // in the management screen removing their way down isn't blocked mid-edit by this check --
  // though the backend's own last-exam guard means they can never actually reach zero that way).
  if (
    isAuthenticated && hasLoaded && !hasConfigured &&
    location.pathname !== "/select-exams" && location.pathname !== "/my-exams"
  ) {
    const target = location.pathname + location.search;
    return <Navigate to={target === "/" ? "/select-exams" : `/select-exams?redirect=${encodeURIComponent(target)}`} replace />;
  }

  // FULLSCREEN ONBOARDING -- same reasoning as the fullscreen test-attempt route below: the
  // sidebar/header/footer/bottom-nav would just be a distraction from a focused, one-time setup
  // screen. SelectExams has its own back-less, minimal header.
  if (location.pathname === "/select-exams") {
    return (
      <div className="min-h-screen bg-surface">
        <Outlet />
      </div>
    );
  }

  // FULLSCREEN TEST ATTEMPT -- Practice, Mock, PYP, and Quiz attempts all converge on this one
  // route (SCORAM_TESTS' shared attempt backbone -- see TestRunner.jsx). While a timed attempt is
  // actually in progress, the sidebar/header/footer/bottom-nav would just be a distraction (and an
  // easy accidental way to lose focus mid-test), so this route renders with none of it -- TestRunner
  // already has its own compact header with the countdown and an explicit exit control.
  if (/^\/tests\/attempt\/[^/]+$/.test(location.pathname)) {
    return (
      <div className="min-h-screen bg-surface">
        <Outlet />
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-surface lg:flex">
      {isAuthenticated && <ChatNotificationBridge onUnseenChatActivity={() => setHasUnseenChat(true)} />}

      <Sidebar
        items={sidebarNavItems}
        isAuthenticated={isAuthenticated}
        user={user}
        onLogout={logout}
        hasUnseenChat={hasUnseenChat}
      />

      <MobileDrawer
        items={sidebarNavItems}
        isOpen={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        isAuthenticated={isAuthenticated}
        user={user}
        onLogout={logout}
        hasUnseenChat={hasUnseenChat}
      />

      <div className="flex min-h-screen flex-1 flex-col">
        {/* NotificationBell (inside Header) fetches its own real unread count now --
            no more hardcoded placeholder. */}
        <Header onMenuClick={() => setDrawerOpen(true)} isAuthenticated={isAuthenticated} />

        <div className="flex-1">
          <Outlet />
        </div>

        <Footer />

        <BottomNav
          items={bottomNavItems}
          onAskClick={() => navigate(isAuthenticated ? "/discussions" : "/login?redirect=/discussions")}
        />
      </div>
    </div>
  );
}
