import { apiFetch, API_BASE_URL } from "../../api/client";

function qs(params = {}) {
  const q = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => {
    if (v !== undefined && v !== null && v !== "") q.set(k, v);
  });
  const s = q.toString();
  return s ? `?${s}` : "";
}

// All analytics calls take the resolved range { from, to, tzOffsetMinutes } from lib/dateRange.js.
// refresh=true bypasses the server's 60-second cache (the dashboard's Refresh button).
function get(token, kind, range, extra = {}, signal) {
  return apiFetch(
    `/api/admin/analytics/${kind}${qs({ from: range.from, to: range.to, tzOffsetMinutes: range.tzOffsetMinutes, ...extra })}`,
    { token, signal }
  );
}

export const getOverview = (token, range, opts = {}) => get(token, "overview", range, opts.refresh ? { refresh: true } : {}, opts.signal);
export const getUsersAnalytics = (token, range, opts = {}) => get(token, "users", range, { granularity: opts.granularity, ...(opts.refresh ? { refresh: true } : {}) }, opts.signal);
export const getContentAnalytics = (token, range, opts = {}) => get(token, "content", range, { granularity: opts.granularity, ...(opts.refresh ? { refresh: true } : {}) }, opts.signal);
export const getLearningAnalytics = (token, range, opts = {}) => get(token, "learning", range, { granularity: opts.granularity, ...(opts.refresh ? { refresh: true } : {}) }, opts.signal);
export const getCommunityAnalytics = (token, range, opts = {}) => get(token, "community", range, { granularity: opts.granularity, ...(opts.refresh ? { refresh: true } : {}) }, opts.signal);
export const getOperationsAnalytics = (token, range, opts = {}) => get(token, "operations", range, { granularity: opts.granularity, ...(opts.refresh ? { refresh: true } : {}) }, opts.signal);

// Sidebar badge counters -- only keys the admin may see come back.
export const getBadges = (token, signal) => apiFetch("/api/admin/analytics/badges", { token, signal });

export const getReportCatalog = (token) => apiFetch("/api/admin/reports/catalog", { token });

// Report downloads need the Authorization header, so they can't be a plain <a href>. Fetches the file as a
// blob and triggers a browser download. Returns { truncated } so the UI can warn about the 50,000-row cap.
export async function downloadReport(token, type, range, format) {
  const res = await fetch(
    `${API_BASE_URL}/api/admin/reports/${encodeURIComponent(type)}${qs({ from: range.from, to: range.to, tzOffsetMinutes: range.tzOffsetMinutes, format })}`,
    { headers: { Authorization: `Bearer ${token}` } }
  );
  if (!res.ok) {
    let message = "The export failed.";
    try {
      const data = await res.json();
      if (data?.message) message = data.message;
    } catch { /* not JSON */ }
    if (res.status === 403) message = "You don't have permission to export this report.";
    const err = new Error(message);
    err.status = res.status;
    throw err;
  }
  const blob = await res.blob();
  const disposition = res.headers.get("Content-Disposition") || "";
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const filename = match ? decodeURIComponent(match[1]) : `scoram-${type}.${format}`;
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
  return { truncated: res.headers.get("X-Report-Truncated") === "true", filename };
}
