import { useEffect, useState } from "react";
import { Outlet, useLocation, useNavigate } from "react-router-dom";
import Header from "../components/layout/Header";
import Sidebar from "../components/layout/Sidebar";
import BottomNav from "../components/layout/BottomNav";
import MobileDrawer from "../components/layout/MobileDrawer";
import ChatNotificationBridge from "../components/layout/ChatNotificationBridge";
import Footer from "../components/layout/Footer";
import Landing from "../pages/Landing";
import { useAuth } from "../context/AuthContext";
import { sidebarNavItems, bottomNavItems } from "../data/mockData";

export default function AppLayout() {
  const { isAuthenticated, user, logout } = useAuth();
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
