import { apiFetch } from "./client";

// PUBLIC landing content -- see ScoramAPI/Controllers/LandingContentController.cs.
// GET /api/public/landing-content -> { media: { [slotKey]: "/uploads/landing/..." }, smartProgress: {...} | null }
export function getPublicLandingContent({ signal } = {}) {
  return apiFetch("/api/public/landing-content", { signal });
}
