// PYQ BROWSING PROGRESS -- pure helpers (no React / DOM / network) for useQuestionBrowseProgress and the
// PYQs page, so the rules can be unit-tested.

export const MAX_ANSWERS_PER_REQUEST = 100; // the API rejects bigger batches
export const MAX_RESTORE_PAGES = 30;        // never fan out more than this many pages to get back to a position
export const LOCAL_MIRROR_MAX_ANSWERS = 1000;

const LIST_KEYS = ["subjectIds", "topicIds", "examIds", "years", "languages"];
const MAX_LIST_ITEMS = 50;
const MAX_ITEM_LENGTH = 64;
const MAX_SEARCH_LENGTH = 200;

/** GUIDs are compared case-insensitively everywhere (API returns lower-case, other sources may not). */
export function qKey(id) {
  return String(id ?? "").toLowerCase();
}

/** Valid MCQ option letter A-D (any case, padded) -> "A".."D"; anything else -> null. */
export function normalizeOption(raw) {
  if (typeof raw !== "string") return null;
  const s = raw.trim().toUpperCase();
  return s.length === 1 && s >= "A" && s <= "D" ? s : null;
}

/** The filters that define a browsing session, taken from the page's URLSearchParams. */
export function filtersFromParams(params) {
  const filters = {};
  const q = params.get("q");
  if (q) filters.q = q.slice(0, MAX_SEARCH_LENGTH);
  LIST_KEYS.forEach((key) => {
    const raw = params.get(key);
    const list = raw ? raw.split(",").filter(Boolean) : [];
    if (list.length) filters[key] = list;
  });
  return filters;
}

/** Anything coming back from the server is untrusted: keep only known keys, strings, bounded sizes. */
export function sanitizeFilters(raw) {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return {};
  const out = {};
  if (typeof raw.q === "string" && raw.q.trim()) out.q = raw.q.slice(0, MAX_SEARCH_LENGTH);
  LIST_KEYS.forEach((key) => {
    const value = raw[key];
    if (!Array.isArray(value)) return;
    const list = value
      .filter((v) => (typeof v === "string" || typeof v === "number") && String(v).length > 0 && String(v).length <= MAX_ITEM_LENGTH)
      .map(String)
      .filter((v) => !v.includes(","))
      .slice(0, MAX_LIST_ITEMS);
    if (list.length) out[key] = list;
  });
  return out;
}

/** filters (+ view mode) -> the URL query string the PYQs page reads its state from. */
export function paramsFromFilters(filters, viewMode) {
  const params = new URLSearchParams();
  const clean = sanitizeFilters(filters);
  if (clean.q) params.set("q", clean.q);
  LIST_KEYS.forEach((key) => {
    if (clean[key]?.length) params.set(key, clean[key].join(","));
  });
  if (viewMode === "slide") params.set("mode", "slide");
  return params;
}

/** True when the URL already carries a search / filter (a shared link or an explicit choice): don't override it. */
export function hasBrowseParams(params) {
  return Object.keys(filtersFromParams(params)).length > 0;
}

export function hasAnyFilters(filters) {
  return Object.keys(sanitizeFilters(filters)).length > 0;
}

/** How many result pages must be loaded so that item `position` (0-based) is present. */
export function pagesNeeded(position, pageSize, maxPages = MAX_RESTORE_PAGES) {
  const pos = Number.isFinite(position) && position > 0 ? Math.floor(position) : 0;
  return Math.max(1, Math.min(maxPages, Math.floor(pos / pageSize) + 1));
}

/**
 * Where to resume inside the freshly loaded list: the saved question if it is still there (the order can
 * change when questions are added), otherwise the saved index clamped into range.
 */
export function resolveResumeIndex(items, lastQuestionId, lastPosition) {
  if (!items.length) return 0;
  if (lastQuestionId) {
    const idx = items.findIndex((q) => qKey(q.id) === qKey(lastQuestionId));
    if (idx >= 0) return idx;
  }
  const pos = Number.isFinite(lastPosition) ? Math.floor(lastPosition) : 0;
  return Math.max(0, Math.min(items.length - 1, pos));
}

/** Server answers + still-unsynced local taps -> { [questionKey]: "A".."D" }. Unsynced taps win (they are newer). */
export function mergeAnswers(serverAnswers, pending) {
  const out = {};
  (serverAnswers || []).forEach((a) => {
    const option = normalizeOption(a?.selectedOption);
    if (option && a?.questionId) out[qKey(a.questionId)] = option;
  });
  (pending || []).forEach((p) => {
    const option = normalizeOption(p?.option);
    if (option && p?.questionId) out[qKey(p.questionId)] = option;
  });
  return out;
}

/** Add a tap to the unsynced queue: one entry per question, the latest tap wins. */
export function enqueueAnswer(queue, { questionId, option, at }) {
  const key = qKey(questionId);
  return [...queue.filter((p) => qKey(p.questionId) !== key), { questionId: key, option, at }];
}

/** Remove from the queue only what was actually sent -- a newer tap on the same question stays queued. */
export function dequeueSent(queue, sent) {
  return queue.filter((p) => !sent.some((s) => qKey(s.questionId) === qKey(p.questionId) && s.at === p.at));
}

/** Retry delay after the n-th consecutive failure: 5s, 10s, 20s ... capped at 60s. */
export function retryDelayMs(attempt) {
  return Math.min(60000, 5000 * 2 ** Math.max(0, attempt));
}

/** Keep the localStorage mirror small: at most `max` answers (the rest are on the server anyway). */
export function capAnswers(answers, max = LOCAL_MIRROR_MAX_ANSWERS) {
  const entries = Object.entries(answers || {});
  return entries.length <= max ? answers : Object.fromEntries(entries.slice(entries.length - max));
}

/** A position that carries no information (nothing filtered, first question) isn't worth creating a session for. */
export function isDefaultPosition({ filters, lastPosition }) {
  return !hasAnyFilters(filters) && !(lastPosition > 0);
}
