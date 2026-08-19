import { apiFetch } from "../../api/client";

// GET /api/admin/dashboard/stats
export function getDashboardStats(token) {
  return apiFetch("/api/admin/dashboard/stats", { token });
}
