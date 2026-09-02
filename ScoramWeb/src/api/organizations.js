import { apiFetch } from "./client";

// ORGANIZATION HIERARCHY -- an Organization (SSC, RRB, UPSC, ...) sits above Exam; each Exam
// belongs to at most one Organization (see ScoramAPI/Models/Organization.cs). Used to power the
// "pick an Organization, then pick from its exams" two-step picker everywhere an exam picker
// exists, instead of one flat list of every exam at once.
export function listOrganizations({ signal } = {}) {
  return apiFetch("/api/organizations", { signal });
}
