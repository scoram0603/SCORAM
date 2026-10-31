import { Loader2 } from "lucide-react";

// Shown while a lazily-loaded route chunk downloads. A fixed minimum height keeps the layout from
// collapsing and the footer/bottom-nav from jumping up and down as pages stream in.
export default function RouteFallback() {
  return (
    <div role="status" aria-live="polite" className="flex min-h-[60vh] items-center justify-center">
      <Loader2 className="h-6 w-6 animate-spin text-primary-600" strokeWidth={2.25} aria-hidden="true" />
      <span className="sr-only">Loading…</span>
    </div>
  );
}
