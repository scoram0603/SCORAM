import { apiFetch } from "../../api/client";

// Business IDs (EXMSSC001, SUB001, TST0001, MCK0001, ADM0001) -- SuperAdmin-only endpoints.
// The server enforces the role; the UI only hides the controls for everyone else.
//
// Failed calls throw ApiError; err.data.code is one of INVALID_FORMAT, DUPLICATE_BUSINESS_ID,
// CONFIRMATION_REQUIRED, NO_CHANGE, NOT_FOUND, BACKFILL_PENDING, SUPERADMIN_REQUIRED.

// payload: { entityType: "exam"|"subject"|"test"|"mocktest"|"admin", entityId (the GUID),
//            newBusinessId, confirm: true }
// Changes ONLY the Business ID -- the GUID and every relationship stay as they were.
export function changeBusinessId(token, payload) {
  return apiFetch("/api/admin/business-ids/change", { method: "POST", token, body: payload });
}

// dryRun defaults to true on the server: returns the plan without saving anything.
export function runBusinessIdBackfill(token, { dryRun = true } = {}) {
  return apiFetch(`/api/admin/business-ids/backfill?dryRun=${dryRun}`, { method: "POST", token });
}
