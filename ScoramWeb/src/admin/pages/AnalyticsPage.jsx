import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import {
  Area, AreaChart, Bar, BarChart, CartesianGrid, Cell, Legend, Line, LineChart, Pie, PieChart, ResponsiveContainer,
  Scatter, ScatterChart, Tooltip, XAxis, YAxis, ZAxis,
} from "recharts";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  getUsersAnalytics, getContentAnalytics, getLearningAnalytics, getCommunityAnalytics, getOperationsAnalytics,
} from "../api/analytics";
import { PageHeader, Select } from "../components/AdminUI";
import {
  AXIS_PROPS, CHART_COLORS, ChartCard, DateRangeBar, GRID_PROPS, SERIES_PALETTE, TOOLTIP_STYLE, UnavailableCard,
  timeLabel, useLoader,
} from "../components/analytics";
import { resolvePreset } from "../lib/dateRange";

const TABS = [
  { key: "users", label: "Users", permission: "ViewAnalytics" },
  { key: "content", label: "Content", permission: "ViewAnalytics" },
  { key: "learning", label: "Learning", permission: "ViewAnalytics" },
  { key: "revenue", label: "Revenue", permission: "ViewAnalytics" },
  { key: "community", label: "Community", permission: "ViewAnalytics" },
  { key: "operations", label: "Operations", permission: "Audit" },
];

function Notes({ notes }) {
  if (!notes?.length) return null;
  return (
    <ul className="list-disc space-y-1 rounded-xl2 bg-primary-50 px-5 py-3 text-xs text-primary-600">
      {notes.map((n) => <li key={n}>{n}</li>)}
    </ul>
  );
}

const pct = (v) => (v === null || v === undefined ? "—" : `${Number(v).toFixed(1)}%`);

function UsersTab({ token, range, granularity }) {
  const q = useLoader((signal, refresh) => getUsersAnalytics(token, range, { granularity, refresh, signal }), [token, range.from, range.to, granularity]);
  const s = q.data?.series || [];
  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <ChartCard title="New signups" subtitle="Student accounts created" isLoading={q.isLoading && !q.data} error={q.error} isEmpty={!s.some((p) => p.newUsers)}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={s} margin={{ top: 4, right: 8, left: -18, bottom: 0 }}>
              <CartesianGrid {...GRID_PROPS} /><XAxis dataKey="label" {...AXIS_PROPS} minTickGap={24} /><YAxis allowDecimals={false} {...AXIS_PROPS} />
              <Tooltip {...TOOLTIP_STYLE} cursor={{ fill: "#EAF1FD" }} />
              <Bar dataKey="newUsers" name="New students" fill={CHART_COLORS.blue} radius={[6, 6, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </ChartCard>
        <ChartCard title="Total registered students" subtitle="Cumulative" isLoading={q.isLoading && !q.data} error={q.error} isEmpty={!s.length}>
          <ResponsiveContainer width="100%" height="100%">
            <AreaChart data={s} margin={{ top: 4, right: 8, left: -10, bottom: 0 }}>
              <CartesianGrid {...GRID_PROPS} /><XAxis dataKey="label" {...AXIS_PROPS} minTickGap={24} /><YAxis allowDecimals={false} domain={["dataMin", "auto"]} {...AXIS_PROPS} />
              <Tooltip {...TOOLTIP_STYLE} />
              <Area type="monotone" dataKey="cumulativeUsers" name="Total students" stroke={CHART_COLORS.primary} fill={CHART_COLORS.blue} fillOpacity={0.15} strokeWidth={2.5} />
            </AreaChart>
          </ResponsiveContainer>
        </ChartCard>
        <ChartCard title="Active learners vs attempts" subtitle="People who started a test, and how many attempts they made" isLoading={q.isLoading && !q.data} error={q.error} isEmpty={!s.some((p) => p.activeLearners || p.testAttempts)}>
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={s} margin={{ top: 4, right: 8, left: -18, bottom: 0 }}>
              <CartesianGrid {...GRID_PROPS} /><XAxis dataKey="label" {...AXIS_PROPS} minTickGap={24} /><YAxis allowDecimals={false} {...AXIS_PROPS} />
              <Tooltip {...TOOLTIP_STYLE} /><Legend iconType="circle" wrapperStyle={{ fontSize: 12 }} />
              <Line type="monotone" dataKey="activeLearners" name="Active learners" stroke={CHART_COLORS.orange} strokeWidth={2.5} dot={false} />
              <Line type="monotone" dataKey="testAttempts" name="Test attempts" stroke={CHART_COLORS.teal} strokeWidth={2.5} dot={false} />
            </LineChart>
          </ResponsiveContainer>
        </ChartCard>
      </div>
      <Notes notes={q.data?.notes} />
    </div>
  );
}

function ContentTab({ token, range, granularity }) {
  const q = useLoader((signal, refresh) => getContentAnalytics(token, range, { granularity, refresh, signal }), [token, range.from, range.to, granularity]);
  const d = q.data;
  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <ChartCard title="Publishing activity" subtitle="Uploaded, published and rejected papers" isLoading={q.isLoading && !d} error={q.error} isEmpty={!d?.publishing?.some((p) => p.uploaded || p.published || p.rejected)}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={d?.publishing || []} margin={{ top: 4, right: 8, left: -18, bottom: 0 }}>
              <CartesianGrid {...GRID_PROPS} /><XAxis dataKey="label" {...AXIS_PROPS} minTickGap={24} /><YAxis allowDecimals={false} {...AXIS_PROPS} />
              <Tooltip {...TOOLTIP_STYLE} cursor={{ fill: "#EAF1FD" }} /><Legend iconType="circle" wrapperStyle={{ fontSize: 12 }} />
              <Bar dataKey="uploaded" name="Uploaded" fill={CHART_COLORS.blue} radius={[4, 4, 0, 0]} />
              <Bar dataKey="published" name="Published" fill={CHART_COLORS.green} radius={[4, 4, 0, 0]} />
              <Bar dataKey="rejected" name="Rejected" fill={CHART_COLORS.red} radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </ChartCard>
        <ChartCard title="Questions by subject" subtitle="Share of all active questions (top subjects)" isLoading={q.isLoading && !d} error={q.error} isEmpty={!d?.subjectDistribution?.length}>
          <DonutChart data={d?.subjectDistribution || []} />
        </ChartCard>
        <ChartCard title="Papers by exam" subtitle="Top exams by number of papers" isLoading={q.isLoading && !d} error={q.error} isEmpty={!d?.examDistribution?.length} height={300}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart layout="vertical" data={d?.examDistribution || []} margin={{ top: 4, right: 16, left: 8, bottom: 0 }}>
              <CartesianGrid {...GRID_PROPS} horizontal={false} vertical />
              <XAxis type="number" allowDecimals={false} {...AXIS_PROPS} />
              <YAxis type="category" dataKey="name" width={130} {...AXIS_PROPS} />
              <Tooltip {...TOOLTIP_STYLE} cursor={{ fill: "#EAF1FD" }} />
              <Bar dataKey="count" name="Papers" fill={CHART_COLORS.violet} radius={[0, 6, 6, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </ChartCard>
        <ChartCard title="Organization / partner performance" subtitle="Exams, published papers and learners per organization" isLoading={q.isLoading && !d} error={q.error} isEmpty={!d?.organizations?.length} height={300}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart layout="vertical" data={d?.organizations || []} margin={{ top: 4, right: 16, left: 8, bottom: 0 }}>
              <CartesianGrid {...GRID_PROPS} horizontal={false} vertical />
              <XAxis type="number" allowDecimals={false} {...AXIS_PROPS} />
              <YAxis type="category" dataKey="name" width={130} {...AXIS_PROPS} />
              <Tooltip {...TOOLTIP_STYLE} cursor={{ fill: "#EAF1FD" }} /><Legend iconType="circle" wrapperStyle={{ fontSize: 12 }} />
              <Bar dataKey="exams" name="Exams" fill={CHART_COLORS.blue} />
              <Bar dataKey="publishedPapers" name="Published papers" fill={CHART_COLORS.green} />
              <Bar dataKey="learners" name="Learners" fill={CHART_COLORS.orange} />
            </BarChart>
          </ResponsiveContainer>
        </ChartCard>
      </div>
      <Notes notes={d?.notes} />
    </div>
  );
}

function DonutChart({ data }) {
  return (
    <div className="flex h-full flex-col items-center gap-2 sm:flex-row">
      <div className="h-full min-h-[180px] w-full sm:w-1/2">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie data={data} dataKey="count" nameKey="name" innerRadius="55%" outerRadius="85%" paddingAngle={2} isAnimationActive={false}>
              {data.map((_, i) => <Cell key={i} fill={SERIES_PALETTE[i % SERIES_PALETTE.length]} />)}
            </Pie>
            <Tooltip {...TOOLTIP_STYLE} formatter={(v, n) => [Number(v).toLocaleString("en-IN"), n]} />
          </PieChart>
        </ResponsiveContainer>
      </div>
      <ul className="w-full space-y-1 overflow-y-auto text-xs sm:w-1/2" style={{ maxHeight: "100%" }}>
        {data.map((d, i) => (
          <li key={d.name} className="flex items-center gap-2">
            <span className="h-2.5 w-2.5 shrink-0 rounded-full" style={{ background: SERIES_PALETTE[i % SERIES_PALETTE.length] }} />
            <span className="min-w-0 flex-1 truncate text-ink-600">{d.name}</span>
            <span className="font-semibold text-ink-900">{d.count.toLocaleString("en-IN")} · {d.percent.toFixed(1)}%</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function LearningTab({ token, range, granularity }) {
  const q = useLoader((signal, refresh) => getLearningAnalytics(token, range, { granularity, refresh, signal }), [token, range.from, range.to, granularity]);
  const d = q.data;
  const kinds = [...new Set((d?.scatter || []).map((p) => p.Kind || p.kind))];
  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <ChartCard title="Test activity" subtitle="Attempts started, by outcome" isLoading={q.isLoading && !d} error={q.error} isEmpty={!d?.activity?.some((p) => p.started)}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={d?.activity || []} margin={{ top: 4, right: 8, left: -18, bottom: 0 }}>
              <CartesianGrid {...GRID_PROPS} /><XAxis dataKey="label" {...AXIS_PROPS} minTickGap={24} /><YAxis allowDecimals={false} {...AXIS_PROPS} />
              <Tooltip {...TOOLTIP_STYLE} cursor={{ fill: "#EAF1FD" }} /><Legend iconType="circle" wrapperStyle={{ fontSize: 12 }} />
              <Bar dataKey="completed" name="Completed" stackId="a" fill={CHART_COLORS.green} />
              <Bar dataKey="inProgress" name="In progress" stackId="a" fill={CHART_COLORS.blue} />
              <Bar dataKey="abandoned" name="Abandoned" stackId="a" fill={CHART_COLORS.grey} radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </ChartCard>
        <ChartCard
          title="Time spent vs accuracy"
          subtitle="One dot per completed attempt"
          isLoading={q.isLoading && !d}
          error={q.error}
          isEmpty={!d?.scatter?.length}
          emptyText="No completed attempts with a recorded duration in this period."
          note={d && d.scatterTotalEligible > d.scatter.length ? `Showing a sample of ${d.scatter.length} of ${d.scatterTotalEligible.toLocaleString("en-IN")} attempts.` : undefined}
        >
          <ResponsiveContainer width="100%" height="100%">
            <ScatterChart margin={{ top: 8, right: 12, left: -8, bottom: 12 }}>
              <CartesianGrid {...GRID_PROPS} vertical />
              <XAxis type="number" dataKey="minutes" name="Minutes" unit=" min" {...AXIS_PROPS} />
              <YAxis type="number" dataKey="accuracy" name="Accuracy" unit="%" domain={[0, 100]} {...AXIS_PROPS} />
              <ZAxis range={[40, 40]} />
              <Tooltip {...TOOLTIP_STYLE} cursor={{ strokeDasharray: "3 3" }} />
              <Legend iconType="circle" wrapperStyle={{ fontSize: 12 }} />
              {kinds.map((kind, i) => (
                <Scatter key={kind} name={kind} data={(d.scatter).filter((p) => (p.kind || p.Kind) === kind)} fill={SERIES_PALETTE[i % SERIES_PALETTE.length]} fillOpacity={0.65} isAnimationActive={false} />
              ))}
            </ScatterChart>
          </ResponsiveContainer>
        </ChartCard>
      </div>

      <section className="overflow-x-auto rounded-xl2 bg-white p-4 shadow-card">
        <h3 className="mb-3 text-sm font-extrabold text-ink-900">By test type</h3>
        {!d?.byKind?.length ? (
          <p className="py-4 text-center text-xs text-ink-400">{q.isLoading ? "Loading…" : "No attempts in this period."}</p>
        ) : (
          <table className="w-full min-w-[420px] text-left text-sm">
            <thead className="text-xs text-ink-400"><tr><th className="pb-2 font-semibold">Type</th><th className="pb-2 font-semibold">Attempts</th><th className="pb-2 font-semibold">Completed</th><th className="pb-2 font-semibold">Avg accuracy</th></tr></thead>
            <tbody className="divide-y divide-primary-50">
              {d.byKind.map((k) => (
                <tr key={k.kind}><td className="py-2 font-semibold text-ink-900">{k.kind}</td><td>{k.attempts.toLocaleString("en-IN")}</td><td>{k.completed.toLocaleString("en-IN")}</td><td>{pct(k.avgAccuracy)}</td></tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
      <Notes notes={d?.notes} />
    </div>
  );
}

function RevenueTab() {
  return (
    <div className="flex flex-col gap-4">
      <div className="rounded-xl2 border border-dashed border-primary-100 bg-white/70 p-6 text-center">
        <h3 className="text-sm font-extrabold text-ink-900">Revenue analytics are not available</h3>
        <p className="mx-auto mt-1 max-w-xl text-xs text-ink-600">
          SCORAM does not currently sell anything: there is no payment provider, order, subscription, coupon or refund data
          in the system, so no revenue, MRR, ARPU or conversion figure can be calculated honestly. This tab will light up once
          a payment integration records real transactions.
        </p>
      </div>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {[
          ["Revenue & MRR", "Needs paid plans and a payment provider."],
          ["Subscriptions & churn", "Needs a subscription model."],
          ["Refunds & failed payments", "Needs payment-provider webhooks stored server-side."],
        ].map(([label, reason]) => <UnavailableCard key={label} item={{ label, reason }} />)}
      </div>
    </div>
  );
}

function CommunityTab({ token, range, granularity }) {
  const q = useLoader((signal, refresh) => getCommunityAnalytics(token, range, { granularity, refresh, signal }), [token, range.from, range.to, granularity]);
  const d = q.data;
  const statusCharts = [
    ["Question reports by status", d?.questionReportsByStatus],
    ["Chat reports by status", d?.chatReportsByStatus],
    ["Comment reports by status", d?.commentReportsByStatus],
    ["Feedback by status", d?.feedbackByStatus],
  ];
  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
        <div className="rounded-xl2 bg-white p-4 shadow-card"><p className="text-xs font-semibold text-ink-600">Solutions awaiting approval</p><p className="mt-1 text-2xl font-extrabold text-ink-900">{d ? d.pendingSolutions.toLocaleString("en-IN") : "—"}</p></div>
        <div className="rounded-xl2 bg-white p-4 shadow-card"><p className="text-xs font-semibold text-ink-600">Question-report rejection rate</p><p className="mt-1 text-2xl font-extrabold text-ink-900">{pct(d?.questionReportRejectionRate)}</p><p className="text-[11px] text-ink-400">rejected ÷ (resolved + rejected), reports filed in range</p></div>
      </div>
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <ChartCard title="Question reports over time" subtitle="Filed vs resolved" isLoading={q.isLoading && !d} error={q.error} isEmpty={!d?.questionReportsTrend?.some((p) => p.reported || p.resolved)}>
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={d?.questionReportsTrend || []} margin={{ top: 4, right: 8, left: -18, bottom: 0 }}>
              <CartesianGrid {...GRID_PROPS} /><XAxis dataKey="label" {...AXIS_PROPS} minTickGap={24} /><YAxis allowDecimals={false} {...AXIS_PROPS} />
              <Tooltip {...TOOLTIP_STYLE} /><Legend iconType="circle" wrapperStyle={{ fontSize: 12 }} />
              <Line type="monotone" dataKey="reported" name="Reported" stroke={CHART_COLORS.orange} strokeWidth={2.5} dot={false} />
              <Line type="monotone" dataKey="resolved" name="Resolved" stroke={CHART_COLORS.green} strokeWidth={2.5} dot={false} />
            </LineChart>
          </ResponsiveContainer>
        </ChartCard>
        {statusCharts.map(([title, data]) => (
          <ChartCard key={title} title={title} isLoading={q.isLoading && !d} error={q.error} isEmpty={!data?.some((s) => s.count)} height={200} emptyText="Nothing filed in this period.">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart layout="vertical" data={data || []} margin={{ top: 4, right: 16, left: 8, bottom: 0 }}>
                <CartesianGrid {...GRID_PROPS} horizontal={false} vertical />
                <XAxis type="number" allowDecimals={false} {...AXIS_PROPS} />
                <YAxis type="category" dataKey="status" width={90} {...AXIS_PROPS} />
                <Tooltip {...TOOLTIP_STYLE} cursor={{ fill: "#EAF1FD" }} />
                <Bar dataKey="count" name="Items" fill={CHART_COLORS.teal} radius={[0, 6, 6, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </ChartCard>
        ))}
      </div>
      <Notes notes={d?.notes} />
    </div>
  );
}

function OperationsTab({ token, range, granularity }) {
  const q = useLoader((signal, refresh) => getOperationsAnalytics(token, range, { granularity, refresh, signal }), [token, range.from, range.to, granularity]);
  const d = q.data;
  return (
    <div className="flex flex-col gap-4">
      <ChartCard title="Admin activity by module" subtitle="Audited admin actions, grouped by area" isLoading={q.isLoading && !d} error={q.error} isEmpty={!d?.adminActivity?.some((p) => p.total)} height={300}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={(d?.adminActivity || []).map((p) => ({ label: p.label, ...p.byModule }))} margin={{ top: 4, right: 8, left: -18, bottom: 0 }}>
            <CartesianGrid {...GRID_PROPS} /><XAxis dataKey="label" {...AXIS_PROPS} minTickGap={24} /><YAxis allowDecimals={false} {...AXIS_PROPS} />
            <Tooltip {...TOOLTIP_STYLE} cursor={{ fill: "#EAF1FD" }} /><Legend iconType="circle" wrapperStyle={{ fontSize: 12 }} />
            {(d?.modules || []).map((m, i) => <Bar key={m} dataKey={m} stackId="a" fill={SERIES_PALETTE[i % SERIES_PALETTE.length]} />)}
          </BarChart>
        </ResponsiveContainer>
      </ChartCard>

      <section className="overflow-x-auto rounded-xl2 bg-white p-4 shadow-card">
        <h3 className="mb-3 text-sm font-extrabold text-ink-900">Recent notification campaigns</h3>
        {!d?.recentCampaigns?.length ? (
          <p className="py-4 text-center text-xs text-ink-400">{q.isLoading ? "Loading…" : "No campaigns sent in this period."}</p>
        ) : (
          <table className="w-full min-w-[480px] text-left text-sm">
            <thead className="text-xs text-ink-400"><tr><th className="pb-2 font-semibold">Title</th><th className="pb-2 font-semibold">Sent</th><th className="pb-2 font-semibold">Recipients</th><th className="pb-2 font-semibold">Read</th></tr></thead>
            <tbody className="divide-y divide-primary-50">
              {d.recentCampaigns.map((c) => (
                <tr key={c.campaignId}><td className="py-2 font-semibold text-ink-900">{c.title}</td><td>{new Date(c.sentAt).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" })}</td><td>{c.recipients.toLocaleString("en-IN")}</td><td>{c.read.toLocaleString("en-IN")}</td></tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      {d?.unavailable?.length > 0 && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">{d.unavailable.map((u) => <UnavailableCard key={u.key} item={u} />)}</div>
      )}
      <Notes notes={d?.notes} />
    </div>
  );
}

export default function AnalyticsPage() {
  const { token, hasPermission } = useAdminAuth();
  const [params, setParams] = useSearchParams();
  const allowed = TABS.filter((t) => hasPermission(t.permission));
  const active = allowed.find((t) => t.key === params.get("tab")) || allowed[0];
  const [range, setRange] = useState(() => resolvePreset("30d"));
  const [granularity, setGranularity] = useState("day");
  // A refresh bumps this key so every tab re-fetches with the cache bypassed.
  const [refreshKey, setRefreshKey] = useState(0);
  const [refreshing, setRefreshing] = useState(false);

  if (!active) return null;

  const common = { token, range, granularity, key: `${active.key}-${refreshKey}` };

  return (
    <div>
      <PageHeader title="Analytics" subtitle="Detailed trends. Every figure comes from live data; anything not tracked yet says so." />
      <div className="flex flex-col gap-4 p-4 sm:p-6">
        <DateRangeBar
          range={range}
          onChange={setRange}
          onRefresh={() => { setRefreshing(true); setRefreshKey((k) => k + 1); setTimeout(() => setRefreshing(false), 600); }}
          refreshing={refreshing}
          lastUpdated={timeLabel(new Date())}
        >
          <label className="flex items-center gap-2 text-xs font-semibold text-ink-600">
            Group by
            <Select value={granularity} onChange={(e) => setGranularity(e.target.value)} className="!w-auto !py-2" aria-label="Group by">
              <option value="day">Day</option>
              <option value="week">Week</option>
              <option value="month">Month</option>
            </Select>
          </label>
        </DateRangeBar>

        <div role="tablist" aria-label="Analytics sections" className="flex gap-1 overflow-x-auto border-b border-primary-100">
          {allowed.map((t) => (
            <button
              key={t.key}
              role="tab"
              type="button"
              aria-selected={t.key === active.key}
              onClick={() => setParams({ tab: t.key })}
              className={`shrink-0 border-b-2 px-4 py-2.5 text-sm font-semibold transition-colors ${
                t.key === active.key ? "border-primary-600 text-primary-600" : "border-transparent text-ink-400 hover:text-ink-600"
              }`}
            >
              {t.label}
            </button>
          ))}
        </div>

        <div role="tabpanel">
          {active.key === "users" && <UsersTab {...common} />}
          {active.key === "content" && <ContentTab {...common} />}
          {active.key === "learning" && <LearningTab {...common} />}
          {active.key === "revenue" && <RevenueTab />}
          {active.key === "community" && <CommunityTab {...common} />}
          {active.key === "operations" && <OperationsTab {...common} />}
        </div>
      </div>
    </div>
  );
}
