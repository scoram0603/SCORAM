import { Navigate, Outlet } from "react-router-dom";
import { useAdminAuth } from "../context/AdminAuthContext";

// Mirrors the guard AdminShell used to do inline: the backend enforces all of this
// independently regardless of what the sidebar/routes show, this just keeps the UI honest.
export default function RequireAdminPermission({ permission, superAdminOnly }) {
  const { isSuperAdmin, hasPermission } = useAdminAuth();
  const allowed = superAdminOnly ? isSuperAdmin : hasPermission(permission);

  if (!allowed) return <Navigate to="/admin" replace />;

  return <Outlet />;
}
