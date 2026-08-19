import { apiFetch } from "./client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") query.set(key, value);
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// GET /api/gamification/me -- streak, XP, level, badge count. Requires login (it's the
// current student's own profile), same as everything else in this file.
export function getGamificationSummary(opts = {}) {
  return apiFetch("/api/gamification/me", { ...opts, auth: true });
}

// GET /api/gamification/badges -- full master list, each flagged earned/locked for this student.
export function getBadges(opts = {}) {
  return apiFetch("/api/gamification/badges", { ...opts, auth: true });
}

// GET /api/gamification/leaderboard?scope=global|exam|friends&period=alltime|weekly|monthly&examName=
export function getLeaderboard(params = {}, opts = {}) {
  return apiFetch(`/api/gamification/leaderboard${toQueryString(params)}`, { ...opts, auth: true });
}
