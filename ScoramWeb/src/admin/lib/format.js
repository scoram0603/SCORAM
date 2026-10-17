// KPI value formatting shared by the dashboard cards.
export function formatKpiValue(value, unit) {
  if (value === null || value === undefined) return "—";
  if (unit === "percent") return `${Number(value).toFixed(1)}%`;
  if (unit === "minutes") {
    const m = Number(value);
    if (m >= 120) return `${(m / 60).toFixed(1)} h`;
    if (m >= 1) return `${m.toFixed(m >= 10 ? 0 : 1)} min`;
    return `${Math.round(m * 60)} s`;
  }
  return Number(value).toLocaleString("en-IN", { maximumFractionDigits: 0 });
}
