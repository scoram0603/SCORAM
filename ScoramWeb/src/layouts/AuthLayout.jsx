import { Outlet } from "react-router-dom";
import RouteSuspense from "../components/routing/RouteSuspense";

export default function AuthLayout() {
  return (
    <div className="min-h-screen bg-surface">
      <RouteSuspense>
        <Outlet />
      </RouteSuspense>
    </div>
  );
}
