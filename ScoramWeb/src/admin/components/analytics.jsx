import { useEffect, useRef, useState } from "react";
import {
  AlertCircle, ArrowDownRight, ArrowUpRight, CalendarDays, Info, Minus, RefreshCw, ArrowRight,
} from "lucide-react";
import { Link } from "react-router-dom";
import { Line, LineChart, ResponsiveContainer } from "recharts";
import { Button } from "./AdminUI";
import { formatKpiValue } from "../lib/format";
import { PRESETS, describeRange, resolvePreset, toIsoDate, validateCustom } from "../lib/dateRange";

export const CHART_COLORS = {
  primary: "#123B7A",
  blue: "#1E63D5",
  orange: "#FF6B00",
  green: "#1E9E5A",
  teal: "#13A6B2",
  violet: "#7C3AED",
  red: "#DC2626",
  grey: "#9CA3AF",
};
export const SERIES_PALETTE = [
  CHART_COLORS.blue, CHART_COLORS.orange, CHART_COLORS.green, CHART_COLORS.violet, CHART_COLORS.teal, CHART_COLORS.primary,
];

export const AXIS_PROPS = { stroke: "#9CA3AF", fontSize: 11, tickLine: false, axisLine: false };
export const GRID_PROPS = { stroke: "#EAEEF6", strokeDasharray: "3 3", vertical: false };
export const TOOLTIP_STYLE = {
  contentStyle: { borderRadius: 12, border: "1px solid #CBD5E9", fontSize: 12, boxShadow: "0 4px 16px rgba(5,15,39,.08)" },
  labelStyle: { fontWeight: 700, color: "#111827" },
};

// ---- Date range bar ----------------------------------------------------------------------------------
// Controlled: parent keeps `range` ({key, from, to, tzOffsetMinutes}); onChange fires only with a valid range.
export function DateRangeBar({ range, onChange, onRefresh, refreshing, children, lastUpdated }) {
  const [customFrom, setCustomFrom] = useState(range.from || "");
  const [customTo, setCustomTo] = useState(range.to || "");
  const [customError, setCustomError] = useState(null);
  const today = toIsoDate(new Date());

  function pick(key) {
    if (key === "custom") {
      onChange({ ...resolvePreset("30d"), key: "custom" });
      return;
    }
    onChange(resolvePreset(key));
  }

  function applyCustom() {
    const err = validateCustom(customFrom, customTo);
    setCustomError(err);
    if (!err) onChange(resolvePreset("custom", { from: customFrom, to: customTo }));
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <label className="flex items-center gap-2 rounded-xl2 border border-primary-100 bg-white px-3 py-2 text-sm font-semibold text-ink-900">
          <CalendarDays className="h-4 w-4 text-primary-500" strokeWidth={2.25} />
          <span className="sr-only">Date range</span>
          <select
            value={range.key}
            onChange={(e) => pick(e.target.value)}
            className="bg-transparent text-sm font-semibold focus:outline-none"
            aria-label="Date range"
          >
            {PRESETS.map((p) => (
              <option key={p.key} value={p.key}>{p.label}</option>
            ))}
          </select>
        </label>
        <span className="text-xs text-ink-400">{describeRange(range)}</span>
        <div className="ml-auto flex flex-wrap items-center gap-2">
          {lastUpdated && <span className="text-xs text-ink-400">Updated {lastUpdated}</span>}
          {onRefresh && (
            <Button variant="secondary" onClick={onRefresh} isLoading={refreshing} className="!px-3 !py-2">
              {!refreshing && <RefreshCw className="h-4 w-4" strokeWidth={2.25} />}
              Refresh
            </Button>
          )}
          {children}
        </div>
      </div>

      {range.key === "custom" && (
        <div className="flex flex-wrap items-end gap-2 rounded-xl2 bg-white p-3 shadow-card">
          <label className="text-xs font-semibold text-ink-600">
            From
            <input type="date" max={today} value={customFrom} onChange={(e) => setCustomFrom(e.target.value)}
              className="mt-1 block rounded-xl2 border border-primary-100 px-3 py-2 text-sm text-ink-900 focus:border-secondary-500 focus:outline-none" />
          </label>
          <label className="text-xs font-semibold text-ink-600">
            To
            <input type="date" max={today} value={customTo} onChange={(e) => setCustomTo(e.target.value)}
              className="mt-1 block rounded-xl2 border border-primary-100 px-3 py-2 text-sm text-ink-900 focus:border-secondary-500 focus:outline-none" />
          </label>
          <Button onClick={applyCustom} className="!py-2">Apply</Button>
          {customError && <p className="basis-full text-xs font-medium text-red-600">{customError}</p>}
        </div>
      )}
    </div>
  );
}

// ---- KPI card ----------------------------------------------------------------------------------------
function DeltaChip({ kpi }) {
  if (kpi.previous === null || kpi.previous === undefined || kpi.delta === null || kpi.delta === undefined) {
    return <span className="text-[11px] text-ink-400">Point-in-time total</span>;
  }
  const up = kpi.delta > 0;
  const flat = kpi.delta === 0;
  const good = kpi.polarity === "neutral" ? null : (kpi.polarity === "up_good") === up;
  const tone = flat || good === null ? "bg-ink-400/10 text-ink-600" : good ? "bg-mint-50 text-mint-500" : "bg-red-50 text-red-600";
  const Icon = flat ? Minus : up ? ArrowUpRight : ArrowDownRight;
  const pct = kpi.deltaPct === null || kpi.deltaPct === undefined ? null : `${Math.abs(kpi.deltaPct).toFixed(1)}%`;
  const absText = kpi.unit === "percent"
    ? `${Math.abs(kpi.delta).toFixed(1)} pts`
    : Math.abs(kpi.delta).toLocaleString("en-IN", { maximumFractionDigits: 1 });
  return (
    <span className="flex items-center gap-1.5 text-[11px] text-ink-400">
      <span className={`inline-flex items-center gap-0.5 rounded-full px-1.5 py-0.5 font-bold ${tone}`}>
        <Icon className="h-3 w-3" strokeWidth={2.75} />
        {kpi.unit === "percent" ? absText : (pct ?? absText)}
      </span>
      vs previous period ({formatKpiValue(kpi.previous, kpi.unit)})
    </span>
  );
}

export function Sparkline({ data, color = CHART_COLORS.blue }) {
  if (!data || data.length < 2) return null;
  const points = data.map((v, i) => ({ i, v }));
  return (
    <div className="h-9 w-full" aria-hidden="true">
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={points} margin={{ top: 4, right: 2, bottom: 2, left: 2 }}>
          <Line type="monotone" dataKey="v" stroke={color} strokeWidth={2} dot={false} isAnimationActive={false} />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

export function KpiCard({ kpi }) {
  const body = (
    <>
      <div className="flex items-start justify-between gap-2">
        <p className="text-xs font-semibold text-ink-600">{kpi.label}</p>
        <span className="group relative">
          <Info className="h-3.5 w-3.5 text-ink-400" strokeWidth={2.25} aria-hidden="true" />
          <span role="tooltip" className="pointer-events-none absolute right-0 top-5 z-20 hidden w-60 rounded-xl bg-primary-900 p-2.5 text-[11px] font-normal leading-snug text-white shadow-lg group-hover:block group-focus-within:block">
            {kpi.definition}
          </span>
        </span>
      </div>
      <p className="mt-1 text-2xl font-extrabold text-ink-900">{formatKpiValue(kpi.value, kpi.unit)}</p>
      <div className="mt-1"><DeltaChip kpi={kpi} /></div>
      <div className="mt-2"><Sparkline data={kpi.spark} /></div>
      {kpi.drillTo && (
        <p className="mt-1 flex items-center gap-1 text-[11px] font-semibold text-secondary-600">
          View details <ArrowRight className="h-3 w-3" strokeWidth={2.5} />
        </p>
      )}
    </>
  );
  const cls = "block rounded-xl2 bg-white p-4 shadow-card transition-shadow";
  return kpi.drillTo ? (
    <Link to={kpi.drillTo} className={`${cls} hover:shadow-md`} aria-label={`${kpi.label}: ${formatKpiValue(kpi.value, kpi.unit)}. Open details`}>
      {body}
    </Link>
  ) : (
    <div className={cls}>{body}</div>
  );
}

export function UnavailableCard({ item }) {
  return (
    <div className="rounded-xl2 border border-dashed border-primary-100 bg-white/60 p-4">
      <p className="text-xs font-semibold text-ink-600">{item.label}</p>
      <p className="mt-1 text-lg font-extrabold text-ink-400">Not available</p>
      <p className="mt-1 text-[11px] leading-snug text-ink-400">{item.reason}</p>
    </div>
  );
}

// ---- Chart card with loading / empty / error states ---------------------------------------------------
export function ChartCard({ title, subtitle, isLoading, error, isEmpty, emptyText = "No data for this period.", height = 260, children, note, action }) {
  return (
    <section className="rounded-xl2 bg-white p-4 shadow-card">
      <div className="mb-3 flex items-start justify-between gap-2">
        <div>
          <h3 className="text-sm font-extrabold text-ink-900">{title}</h3>
          {subtitle && <p className="text-xs text-ink-400">{subtitle}</p>}
        </div>
        {action}
      </div>
      <div style={{ height }} className="w-full">
        {isLoading ? (
          <div className="flex h-full animate-pulse flex-col justify-end gap-2" aria-busy="true" aria-label="Loading chart">
            <div className="h-full rounded-xl bg-primary-50" />
          </div>
        ) : error ? (
          <div className="flex h-full flex-col items-center justify-center gap-1 text-center text-xs text-red-600">
            <AlertCircle className="h-5 w-5" strokeWidth={2.25} />
            {error}
          </div>
        ) : isEmpty ? (
          <div className="flex h-full items-center justify-center text-center text-xs text-ink-400">{emptyText}</div>
        ) : (
          children
        )}
      </div>
      {note && <p className="mt-2 text-[11px] leading-snug text-ink-400">{note}</p>}
    </section>
  );
}

// ---- Data loader hook ---------------------------------------------------------------------------------
// Runs `fetcher(signal, refresh)` whenever deps change; exposes { data, error, isLoading, reload }.
// Out-of-date responses are ignored (race-safe when the range changes quickly).
export function useLoader(fetcher, deps) {
  const [state, setState] = useState({ data: null, error: null, isLoading: true });
  const [tick, setTick] = useState(0);
  const refreshRef = useRef(false);
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;

  useEffect(() => {
    const controller = new AbortController();
    const refresh = refreshRef.current;
    refreshRef.current = false;
    setState((s) => ({ ...s, isLoading: true, error: null }));
    fetcherRef.current(controller.signal, refresh)
      .then((data) => {
        if (!controller.signal.aborted) setState({ data, error: null, isLoading: false, updatedAt: new Date() });
      })
      .catch((err) => {
        if (controller.signal.aborted) return;
        setState((s) => ({ data: s.data, error: err?.status === 403 ? "You don't have access to this data." : err?.message || "Couldn't load this.", isLoading: false }));
      });
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick]);

  return {
    ...state,
    reload: (forceFresh = false) => {
      refreshRef.current = forceFresh;
      setTick((t) => t + 1);
    },
  };
}

export const timeLabel = (d) => (d ? d.toLocaleTimeString("en-IN", { hour: "2-digit", minute: "2-digit" }) : null);
