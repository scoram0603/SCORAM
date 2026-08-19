import { apiFetch } from "./client";

// GET /api/referrals/me -- code is generated on first call if the student doesn't have one yet.
export function getMyReferrals(opts = {}) {
  return apiFetch("/api/referrals/me", { ...opts, auth: true });
}
