// Pure helpers for student reviews. The server (ReviewRules.cs) is the authority -- these mirror its limits so the
// form can give instant feedback, and `filterPublicReviews` is a presentation safeguard on top of the server rule
// (Approved + rating >= 3), never a replacement for it.
export const MIN_RATING = 1;
export const MAX_RATING = 5;
export const MIN_TEXT = 10;
export const MAX_TEXT = 1000;
export const MIN_PUBLIC_RATING = 3;

export const RATING_LABELS = { 1: "Poor", 2: "Fair", 3: "Good", 4: "Very good", 5: "Excellent" };

/** Student-facing wording for the review's moderation state. */
export const REVIEW_STATUS_INFO = {
  Pending: { label: "Awaiting review", tone: "amber", help: "Thanks! Your review will appear on the website once our team has reviewed it." },
  Approved: { label: "Approved", tone: "green", help: "Your review has been approved. Ratings of 3 stars and above may be shown on the SCORAM home page." },
  Rejected: { label: "Not published", tone: "gray", help: "Your review wasn't published. You can edit it and submit it again." },
  Hidden: { label: "Removed from public view", tone: "gray", help: "This review was removed from public view and can't be edited. You can withdraw it and write a new one." },
};

export function normalizeText(text) {
  return String(text ?? "").replace(/\r\n?/g, "\n").trim();
}

/** -> { valid, errors: { rating?, text? }, text } */
export function validateReview({ rating, text }) {
  const errors = {};
  const cleaned = normalizeText(text);
  if (!Number.isInteger(rating) || rating < MIN_RATING || rating > MAX_RATING) {
    errors.rating = "Please choose a rating from 1 to 5 stars.";
  }
  if (cleaned.length < MIN_TEXT) errors.text = `Please write at least ${MIN_TEXT} characters about your experience.`;
  else if (cleaned.length > MAX_TEXT) errors.text = `A review can be at most ${MAX_TEXT} characters.`;
  return { valid: Object.keys(errors).length === 0, errors, text: cleaned };
}

/** Defensive filter for the public list: drops malformed rows, anything under 3 stars, and duplicate ids. */
export function filterPublicReviews(items) {
  if (!Array.isArray(items)) return [];
  const seen = new Set();
  return items.filter((r) => {
    if (!r || typeof r !== "object") return false;
    if (typeof r.id !== "string" || seen.has(r.id)) return false;
    if (!Number.isInteger(r.rating) || r.rating < MIN_PUBLIC_RATING || r.rating > MAX_RATING) return false;
    if (typeof r.text !== "string" || r.text.trim().length === 0) return false;
    seen.add(r.id);
    return true;
  });
}

/** Initials for the avatar bubble; the anonymous label gets a neutral glyph instead of "SS". */
export function reviewInitials(displayName) {
  const name = typeof displayName === "string" ? displayName.trim() : "";
  if (!name || name === "SCORAM Student") return "S";
  const parts = name.replace(/\./g, "").split(/\s+/).filter(Boolean);
  return ((parts[0]?.[0] || "") + (parts[1]?.[0] || "")).toUpperCase() || "S";
}

export function formatReviewDate(iso) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return d.toLocaleDateString("en-IN", { month: "short", year: "numeric" });
}
