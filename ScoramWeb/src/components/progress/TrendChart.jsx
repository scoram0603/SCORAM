import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { fmtPct, isNum } from "./format";

// Accessible line chart over server-provided accuracy values. points: [{ label, value, detail? }].
// The chart itself is aria-hidden presentation; the text summary + data table carry the same data.
export default function TrendChart({ points, title, valueName = "Accuracy", height = 176 }) {
  const withValue = points.filter((p) => isNum(p.value));
  const first = withValue[0];
  const last = withValue[withValue.length - 1];
  const summary =
    withValue.length === 0
      ? `${title}: no accuracy recorded yet.`
      : withValue.length === 1
      ? `${title}: one data point, ${fmtPct(last.value)} (${last.label}).`
      : `${title}: ${points.length} points. First ${fmtPct(first.value)} (${first.label}), latest ${fmtPct(last.value)} (${last.label}).`;
  const data = points.map((p, i) => ({ ...p, idx: i + 1, value: isNum(p.value) ? Number(p.value) : null }));

  return (
    <figure>
      <figcaption className="text-xs text-ink-600">{summary}</figcaption>
      <div className="mt-2" style={{ height }} aria-hidden="true">
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={data} margin={{ top: 4, right: 8, left: -20, bottom: 0 }}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#EAEEF6" />
            <XAxis dataKey="idx" tick={{ fontSize: 11, fill: "#6B7280" }} axisLine={false} tickLine={false} />
            <YAxis domain={[0, 100]} tick={{ fontSize: 11, fill: "#6B7280" }} axisLine={false} tickLine={false} unit="%" />
            <Tooltip
              contentStyle={{ borderRadius: 10, border: "1px solid #EAEEF6", fontSize: 12 }}
              formatter={(v, _n, item) => [isNum(v) ? fmtPct(v) : "—", item.payload.detail || valueName]}
              labelFormatter={(_, payload) => payload?.[0]?.payload?.label || ""}
            />
            <Line type="monotone" dataKey="value" stroke="#1E63D5" strokeWidth={2.5} dot={{ r: 3, fill: "#1E63D5" }} connectNulls={false} isAnimationActive={false} />
          </LineChart>
        </ResponsiveContainer>
      </div>
      <details className="mt-2 text-xs text-ink-600">
        <summary className="cursor-pointer font-semibold text-secondary-500">View as table</summary>
        <div className="mt-1 overflow-x-auto">
          <table className="w-full min-w-[16rem] text-left">
            <thead>
              <tr className="text-ink-400">
                <th scope="col" className="py-1 pr-3 font-semibold">#</th>
                <th scope="col" className="py-1 pr-3 font-semibold">When</th>
                <th scope="col" className="py-1 pr-3 font-semibold">{valueName}</th>
                <th scope="col" className="py-1 font-semibold">Detail</th>
              </tr>
            </thead>
            <tbody>
              {points.map((p, i) => (
                <tr key={i} className="border-t border-primary-50">
                  <td className="py-1 pr-3">{i + 1}</td>
                  <td className="py-1 pr-3">{p.label}</td>
                  <td className="py-1 pr-3 font-semibold">{fmtPct(p.value)}</td>
                  <td className="py-1">{p.detail || ""}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </figure>
  );
}
