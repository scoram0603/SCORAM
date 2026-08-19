import { apiFetch } from "./client";

// GET /api/exams -- public list, same endpoint the admin picker uses
export function listExams({ signal } = {}) {
  return apiFetch("/api/exams", { signal });
}
