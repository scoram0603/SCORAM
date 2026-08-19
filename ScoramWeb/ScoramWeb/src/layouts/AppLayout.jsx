import { useEffect, useState } from "react";
import { Outlet, useLocation, useNavigate } from "react-router-dom";
import Header from "../components/layout/Header";
import Sidebar from "../components/layout/Sidebar";
import BottomNav from "../components/layout/BottomNav";
import MobileDrawer from "../components/layout/MobileDrawer";
import ChatNotificationBridge from "../components/layout/ChatNotificationBridge";
import Footer from "../components/layout/Footer";
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
