import { AlertTriangle, ArrowDown, ArrowUp, CheckCircle2, Info, Minus, RefreshCw, Target, TrendingUp, Hourglass, RotateCcw } from "lucide-react";
import { fmtPct, fmtPoints, isNum, MIN_ATTEMPTS_FOR_ANALYSIS, plural } from "./format";

export function Card({ as: Tag = "section", className = "", children, ...rest }) {
  return (
    <Tag className={`rounded-xl2 border border-primary-100 bg-white p-4 shadow-card ${className}`} {...rest}>
      {children}
    </Tag>
  );
}

export function SectionTitle({ children, hint, id }) {
  return (
    <div className="mb-2 mt-6">
      <h2 id={id} className="text-sm font-bold text-ink-900">{children}</h2>
      {hint && <p className="mt-0.5 text-xs text-ink-400">{hint}</p>}
    </div>
  );
}

export function Skeleton({ className = "" }) {
  return <div className={`animate-pulse rounded-lg bg-primary-50 ${className}`} aria-hidden="true" />;
}

export function SkeletonBlock({ rows = 3, label = "Loading" }) {
  return (
    <div role="status" aria-live="polite" aria-busy="true" className="space-y-2">
      <span className="sr-only">{label}…</span>
      {Array.from({ length: rows }, (_, i) => (
        <Skeleton key={i} className={i === 0 ? "h-5 w-1/3" : "h-4 w-full"} />
      ))}
    </div>
  );
}

export function SkeletonCards({ count = 4, label = "Loading" }) {
  return (
    <div role="status" aria-live="polite" aria-busy="true" className="grid grid-cols-2 gap-3 sm:grid-cols-4">
      <span className="sr-only">{label}…</span>
      {Array.from({ length: count }, (_, i) => (
        <Skeleton key={i} className="h-20" />
      ))}
    </div>
  );
}

export function ErrorState({ error, onRetry, title = "Couldn't load this right now." }) {
  const detail = error?.message && error.status !== 0 ? error.message : null;
  return (
    <div role="alert" className="rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-700">
      <p className="flex items-center gap-2 font-semibold">
        <AlertTriangle className="h-4 w-4 shrink-0" strokeWidth={2.25} aria-hidden="true" />
        {title}
      </p>
      {detail && <p className="mt-1 text-xs text-red-600">{detail}</p>}
      {onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="mt-3 inline-flex items-center gap-1.5 rounded-xl2 border border-red-200 bg-white px-3 py-1.5 text-xs font-semibold text-red-700 hover:bg-red-100"
        >
          <RefreshCw className="h-3.5 w-3.5" strokeWidth={2.25} aria-hidden="true" />
          Retry
        </button>
      )}
    </div>
  );
}

export function EmptyState({ icon: Icon = Info, title, children, action }) {
  return (
    <div className="rounded-xl2 border border-dashed border-primary-100 bg-white p-6 text-center">
      <span className="mx-auto flex h-11 w-11 items-center justify-center rounded-2xl bg-primary-50 text-primary-500">
        <Icon className="h-5 w-5" strokeWidth={2.25} aria-hidden="true" />
      </span>
      <p className="mt-3 text-sm font-bold text-ink-900">{title}</p>
      {children && <div className="mx-auto mt-1 max-w-md text-xs leading-relaxed text-ink-400">{children}</div>}
      {action && <div className="mt-4 flex flex-wrap justify-center gap-2">{action}</div>}
    </div>
  );
}

export function InlineNote({ children, tone = "info" }) {
  const cls = tone === "warn" ? "border-accent-100 bg-accent-50 text-accent-600" : "border-primary-100 bg-primary-50/60 text-ink-600";
  return (
    <p className={`flex items-start gap-2 rounded-lg border px-3 py-2 text-xs leading-snug ${cls}`}>
      <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" strokeWidth={2.25} aria-hidden="true" />
      <span>{children}</span>
    </p>
  );
}

// Neutral bar: it shows the server's accuracy number, it does NOT classify it (status comes from the
// server's Status field). A null value renders "—" and no fill, never an empty-looking 0%.
export function AccuracyBar({ value, label }) {
  const has = isNum(value);
  const width = has ? Math.max(0, Math.min(100, Number(value))) : 0;
  return (
    <div className="flex items-center gap-2">
      <div
        className={`h-2 min-w-0 flex-1 overflow-hidden rounded-full ${has ? "bg-primary-50" : "border border-dashed border-primary-100"}`}
        role="img"
        aria-label={`${label ? `${label}: ` : ""}${has ? fmtPct(value) : "no accuracy yet"}`}
      >
        {has && <div className="h-full rounded-full bg-secondary-500" style={{ width: `${width}%` }} />}
      </div>
      <span className="w-12 shrink-0 text-right text-xs font-bold tabular-nums text-ink-900">{fmtPct(value)}</span>
    </div>
  );
}

const STATUS_META = {
  NeedsAttention: { label: "Needs attention", icon: AlertTriangle, cls: "bg-red-50 text-red-700 border-red-100" },
  PracticeRecommended: { label: "Practice recommended", icon: Target, cls: "bg-accent-50 text-accent-600 border-accent-100" },
  Improving: { label: "Improving", icon: TrendingUp, cls: "bg-teal-50 text-primary-600 border-teal-500/30" },
  PerformingWell: { label: "Performing well", icon: CheckCircle2, cls: "bg-mint-50 text-mint-500 border-mint-100" },
  NeedsRetesting: { label: "Needs retesting", icon: RotateCcw, cls: "bg-violet-50 text-violet-500 border-violet-500/20" },
};

// Server topic Status -> text + icon (colour is never the only signal). InsufficientData is stated
// as such with the sample size, never as "weak" or "strong".
export function TopicStatusBadge({ status, attempted }) {
  if (status === "InsufficientData" || !STATUS_META[status]) {
    return (
      <span className="inline-flex items-center gap-1 rounded-full border border-primary-100 bg-primary-50/60 px-2 py-0.5 text-[11px] font-semibold text-ink-600">
        <Hourglass className="h-3 w-3" strokeWidth={2.25} aria-hidden="true" />
        {insufficientText(attempted)}
      </span>
    );
  }
  const m = STATUS_META[status];
  const Icon = m.icon;
  return (
    <span className={`inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-[11px] font-semibold ${m.cls}`}>
      <Icon className="h-3 w-3" strokeWidth={2.25} aria-hidden="true" />
      {m.label}
    </span>
  );
}

export function insufficientText(attempted) {
  return `Not enough data yet (${attempted ?? 0} of ${MIN_ATTEMPTS_FOR_ANALYSIS} answered)`;
}

// Subject-level Confidence (Insufficient | Preliminary | Higher).
export function ConfidenceNote({ confidence, attempted }) {
  if (confidence === "Insufficient") return <span className="text-[11px] text-ink-400">{insufficientText(attempted)}</span>;
  if (confidence === "Preliminary") return <span className="text-[11px] text-ink-400">Preliminary — small sample ({plural(attempted ?? 0, "answer")})</span>;
  if (confidence === "Higher") return <span className="text-[11px] text-ink-400">Higher confidence ({plural(attempted ?? 0, "answer")})</span>;
  return null;
}

const CHANGE_META = {
  Improved: { icon: ArrowUp, label: "Improved", cls: "text-mint-500" },
  Declined: { icon: ArrowDown, label: "Declined", cls: "text-red-600" },
  Stable: { icon: Minus, label: "Stable", cls: "text-ink-600" },
  NotEnoughData: { icon: Info, label: "Not enough data", cls: "text-ink-400" },
};

function windowLine(w) {
  if (!w) return null;
  const label = w.label || "Window";
  return (
    <li>
      <span className="font-semibold text-ink-600">{label}:</span> {fmtPct(w.accuracyPercent)} accuracy
      <span className="text-ink-400"> ({w.correct} of {plural(w.attempted, "question")} answered correctly)</span>
    </li>
  );
}

// ChangeDto -> arrow + signed percentage POINTS + both sample sizes. Never relative %, never colour alone.
export function ChangeSummary({ change, title, compact = false }) {
  if (!change) return null;
  const meta = CHANGE_META[change.status] || CHANGE_META.NotEnoughData;
  const Icon = meta.icon;
  const points = fmtPoints(change.accuracyChangePoints);
  return (
    <div>
      {title && <p className="text-xs font-bold text-ink-900">{title}</p>}
      <p className={`mt-1 flex flex-wrap items-center gap-1.5 text-sm font-extrabold ${meta.cls}`}>
        <Icon className="h-4 w-4" strokeWidth={2.75} aria-hidden="true" />
        <span>{meta.label}</span>
        {points && <span>· {points}</span>}
      </p>
      {points && <p className="text-[11px] text-ink-400">Change in accuracy, in percentage points (current minus previous).</p>}
      {change.note && <p className="mt-1 text-xs text-ink-600">{change.note}</p>}
      {!compact && (
        <ul className="mt-1.5 space-y-0.5 text-xs text-ink-400">
          {windowLine(change.current)}
          {windowLine(change.previous)}
        </ul>
      )}
    </div>
  );
}

export function PrimaryLinkClass(extra = "") {
  return `inline-flex items-center justify-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-2 text-xs font-semibold text-white transition-colors hover:bg-primary-700 ${extra}`;
}

export function SecondaryLinkClass(extra = "") {
  return `inline-flex items-center justify-center gap-1.5 rounded-xl2 border border-primary-100 bg-white px-4 py-2 text-xs font-semibold text-ink-600 transition-colors hover:bg-primary-50 ${extra}`;
}

export function StatTile({ label, value, sub }) {
  return (
    <div className="rounded-xl2 border border-primary-100 bg-white p-3 shadow-card">
      <p className="text-xl font-extrabold tabular-nums text-ink-900">{value}</p>
      <p className="text-[11px] font-medium text-ink-400">{label}</p>
      {sub && <p className="mt-0.5 text-[11px] text-ink-400">{sub}</p>}
    </div>
  );
}

export function SelectField({ label, value, onChange, children, disabled, className = "" }) {
  return (
    <label className={`block ${className}`}>
      <span className="mb-1 block text-[11px] font-semibold text-ink-600">{label}</span>
      <select
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
        className="h-10 w-full rounded-xl2 border border-primary-100 bg-white px-2.5 text-sm text-ink-900 focus:border-secondary-500 disabled:bg-primary-50 disabled:text-ink-400"
      >
        {children}
      </select>
    </label>
  );
}

