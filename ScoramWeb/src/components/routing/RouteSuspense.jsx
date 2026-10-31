import { Suspense } from "react";
import { Outlet, useLocation } from "react-router-dom";
import ChunkErrorBoundary from "./ChunkErrorBoundary";
import RouteFallback from "./RouteFallback";

// Suspense + error boundary for lazily-loaded routes. Used INSIDE the layouts (around their
// <Outlet />) so the header/sidebar/footer stay mounted while only the page area swaps.
export default function RouteSuspense({ children, fallback = <RouteFallback /> }) {
  const { pathname } = useLocation();
  return (
    <ChunkErrorBoundary resetKey={pathname}>
      <Suspense fallback={fallback}>{children}</Suspense>
    </ChunkErrorBoundary>
  );
}

// For pathless layout routes that have no chrome of their own (public pages such as /our-team).
export function SuspenseOutlet() {
  return (
    <RouteSuspense>
      <Outlet />
    </RouteSuspense>
  );
}
