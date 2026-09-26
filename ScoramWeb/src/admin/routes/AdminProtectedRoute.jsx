import { Navigate, Outlet, useLocation } from "react-router-dom";
import { useAdminAuth } from "../context/AdminAuthContext";

export default function AdminProtectedRoute() {
  const { isAuthenticated, admin } = useAdminAuth();
  const location = useLocation();

  if (!isAuthenticated) {
    return <Navigate to="/admin/login" replace />;
  }

  // Mirrors the backend's MustChangePasswordFilter, which blocks every other admin endpoint while
  // this is true (see that filter's own comment) -- this redirect is the UX for that, not the
  // security boundary itself; the backend enforces it independently regardless of what this does.
  if (admin?.mustChangePassword && location.pathname !== "/admin/change-password") {
    return <Navigate to="/admin/change-password" replace />;
  }

  return <Outlet />;
}
