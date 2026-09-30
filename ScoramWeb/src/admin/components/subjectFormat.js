// The API sends UTC timestamps without a "Z"; treat them as UTC so dates don't shift by the browser's offset.
export function formatDate(value) {
  if (!value) return "—";
  const d = new Date(/[zZ]|[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`);
  return Number.isNaN(d.getTime()) ? "—" : d.toLocaleDateString(undefined, { day: "2-digit", month: "short", year: "numeric" });
}
