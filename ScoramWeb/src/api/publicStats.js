import { apiFetch } from "./client";

// GET /api/public-stats -- anonymous. Real, live counts (questions, exams, students) for the
// landing page hero/stats section. See ScoramAPI/Controllers/PublicStatsController.cs.
export function getPublicStats({ signal } = {}) {
  return apiFetch("/api/public-stats", { signal });
}
