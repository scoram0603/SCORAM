import { useCallback, useState } from "react";
import {
  UploadCloud, ListChecks, Users, FileStack, ArrowRight, RefreshCw, ClipboardCheck, Flag, FileDown,
  MessageCircle, Database, Search, ShieldAlert, Copy,
} from "lucide-react";
import { Link, useNavigate } from "react-router-dom";
import {
  Area, AreaChart, Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from "recharts";
import { useAdminAuth } from "../context/AdminAuthContext";
import { getDashboardStats } from "../api/dashboard";
import { getOverview, getUsersAnalytics, getLearningAnalytics } from "../api/analytics";
import { reindexSearch } from "../api/papers";
import { backfillQuestionBankMirrors } from "../api/adminQuestions";
import { PageHeader, Card, Button, Alert, StatusBadge, friendlyError } from "../components/AdminUI";
import {
  AXIS_PROPS, CHART_COLORS, ChartCard, DateRangeBar, GRID_PROPS, KpiCard, TOOLTIP_STYLE, UnavailableCard,
  timeLabel, useLoader,
} from "../components/analytics";
import { resolvePreset } from "../lib/dateRange";

const KPI_GROUP_ORDER = ["Users", "Learning", "Content", "Moderation", "Operations"];

export default function AdminDashboard() {
  const navigate = useNavigate();
  const { admin, token, isSuperAdmin, hasPermission } = useAdminAuth();
  const canAnalytics = hasPermission("ViewAnalytics");
  const [range, setRange] = useState(() => resolvePreset("30d"));

  // Every block loads independently: one failing aggregate shows an error in ITS card, never blanks the page.
  const overview = useLoader((signal, refresh) => getOverview(token, range, { refresh, signal }), [token, range.from, range.to, range.tzOffsetMinutes]);
  const users = useLoader(
    (signal, refresh) => (canAnalytics ? getUsersAnalytics(token, range, { refresh, signal }) : Promise.resolve(null)),
    [token, canAnalytics, range.from, range.to, range.tzOffsetMinutes]
  );
  const learning = useLoader(
    (signal, refresh) => (canAnalytics ? getLearningAnalytics(token, range, { refresh, signal }) : Promise.resolve(null)),
    [token, canAnalytics, range.from, range.to, range.tzOffsetMinutes]
  );
  const stats = useLoader(() => getDashboardStats(token), [token]);

  const refreshAll = useCallback(() => {
    overview.reload(true);
    users.reload(true);
    learning.reload(true);
    stats.reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [overview.reload, users.reload, learning.reload, stats.reload]);

  const kpiGroups = KPI_GROUP_ORDER
    .map((g) => ({ name: g, items: (overview.data?.kpis || []).filter((k) => k.group === g) }))
    .filter((g) => g.items.length > 0);
  const refreshing = overview.isLoading || users.isLoading || learning.isLoading;

  return (
    <div>
      <PageHeader title={`Welcome, ${admin?.fullName?.split(" ")[0] || "Admin"}`} subtitle={`Logged in as ${admin?.role}`} />

      <div className="flex flex-col gap-5 p-4 sm:p-6">
        <DateRangeBar
          range={range}
          onChange={setRange}
          onRefresh={refreshAll}
          refreshing={refreshing}
          lastUpdated={timeLabel(overview.updatedAt)}
        >
          {hasPermission("ExportReports") && (
            <Link to="/admin/reports" className="flex items-center gap-1.5 rounded-xl2 bg-primary-50 px-3 py-2 text-sm font-semibold text-primary-600 hover:bg-primary-100">
              <FileDown className="h-4 w-4" strokeWidth={2.25} /> Export
            </Link>
          )}
        </DateRangeBar>

        {overview.error && <Alert>{overview.error}</Alert>}

        {overview.isLoading && !overview.data && (
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4" aria-busy="true" aria-label="Loading dashboard">
            {Array.from({ length: 8 }).map((_, i) => <div key={i} className="h-32 animate-pulse rounded-xl2 bg-white shadow-card" />)}
          </div>
        )}

        {overview.data?.restricted && (
          <p className="rounded-xl2 bg-primary-50 px-3 py-2 text-xs text-primary-600">
            User and learning metrics are hidden because your account doesn't have the View Analytics permission.
          </p>
        )}

        {kpiGroups.map((group) => (
          <section key={group.name} aria-label={`${group.name} metrics`}>
            <h2 className="mb-2 text-xs font-bold uppercase tracking-wider text-ink-400">{group.name}</h2>
            <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
              {group.items.map((k) => <KpiCard key={k.key} kpi={k} />)}
            </div>
          </section>
        ))}

        {overview.data?.unavailable?.length > 0 && (
          <section aria-label="Metrics not available yet">
            <h2 className="mb-2 text-xs font-bold uppercase tracking-wider text-ink-400">Not tracked yet</h2>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {overview.data.unavailable.map((u) => <UnavailableCard key={u.key} item={u} />)}
            </div>
          </section>
        )}

        {canAnalytics && (
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
            <ChartCard
              title="New students & active learners"
              subtitle="Active learner = a student who started a test that day"
              isLoading={users.isLoading && !users.data}
              error={users.error}
              isEmpty={!users.data?.series?.some((p) => p.newUsers || p.activeLearners)}
            >
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={users.data?.series || []} margin={{ top: 4, right: 8, left: -18, bottom: 0 }}>
                  <CartesianGrid {...GRID_PROPS} />
                  <XAxis dataKey="label" {...AXIS_PROPS} minTickGap={24} />
                  <YAxis allowDecimals={false} {...AXIS_PROPS} />
                  <Tooltip {...TOOLTIP_STYLE} />
                  <Legend iconType="circle" wrapperStyle={{ fontSize: 12 }} />
                  <Line type="monotone" dataKey="newUsers" name="New students" stroke={CHART_COLORS.blue} strokeWidth={2.5} dot={false} />
                  <Line type="monotone" dataKey="activeLearners" name="Active learners" stroke={CHART_COLORS.orange} strokeWidth={2.5} dot={false} />
                </LineChart>
              </ResponsiveContainer>
            </ChartCard>

            <ChartCard
              title="Total registered students"
              subtitle="Running total across the selected period"
              isLoading={users.isLoading && !users.data}
              error={users.error}
              isEmpty={!users.data?.series?.length}
            >
              <ResponsiveContainer width="100%" height="100%">
                <AreaChart data={users.data?.series || []} margin={{ top: 4, right: 8, left: -10, bottom: 0 }}>
                  <CartesianGrid {...GRID_PROPS} />
                  <XAxis dataKey="label" {...AXIS_PROPS} minTickGap={24} />
                  <YAxis allowDecimals={false} {...AXIS_PROPS} domain={["dataMin", "auto"]} />
                  <Tooltip {...TOOLTIP_STYLE} />
                  <Area type="monotone" dataKey="cumulativeUsers" name="Total students" stroke={CHART_COLORS.primary} fill={CHART_COLORS.blue} fillOpacity={0.15} strokeWidth={2.5} />
                </AreaChart>
              </ResponsiveContainer>
            </ChartCard>

            <ChartCard
              title="Test attempts"
              subtitle="Started attempts by outcome"
              isLoading={learning.isLoading && !learning.data}
              error={learning.error}
              isEmpty={!learning.data?.activity?.some((p) => p.started)}
            >
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={learning.data?.activity || []} margin={{ top: 4, right: 8, left: -18, bottom: 0 }}>
                  <CartesianGrid {...GRID_PROPS} />
                  <XAxis dataKey="label" {...AXIS_PROPS} minTickGap={24} />
                  <YAxis allowDecimals={false} {...AXIS_PROPS} />
                  <Tooltip {...TOOLTIP_STYLE} cursor={{ fill: "#EAF1FD" }} />
                  <Legend iconType="circle" wrapperStyle={{ fontSize: 12 }} />
                  <Bar dataKey="completed" name="Completed" stackId="a" fill={CHART_COLORS.green} />
                  <Bar dataKey="inProgress" name="In progress" stackId="a" fill={CHART_COLORS.blue} />
                  <Bar dataKey="abandoned" name="Abandoned" stackId="a" fill={CHART_COLORS.grey} radius={[4, 4, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </ChartCard>

            <ChartCard
              title="Papers uploaded — last 7 days"
              subtitle="Always the last 7 days, regardless of the range above"
              isLoading={overview.isLoading && !overview.data}
              error={overview.error}
              isEmpty={!overview.data?.uploadsLast7Days?.some((p) => p.count)}
              emptyText="No papers uploaded in the last 7 days."
            >
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={overview.data?.uploadsLast7Days || []} margin={{ top: 4, right: 8, left: -18, bottom: 0 }}>
                  <CartesianGrid {...GRID_PROPS} />
                  <XAxis dataKey="label" {...AXIS_PROPS} />
                  <YAxis allowDecimals={false} {...AXIS_PROPS} />
                  <Tooltip {...TOOLTIP_STYLE} cursor={{ fill: "#EAF1FD" }} />
                  <Bar dataKey="count" name="Papers uploaded" fill={CHART_COLORS.blue} radius={[6, 6, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </ChartCard>
          </div>
        )}

        {!canAnalytics && (
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
            <ChartCard
              title="Papers uploaded — last 7 days"
              isLoading={overview.isLoading && !overview.data}
              error={overview.error}
              isEmpty={!overview.data?.uploadsLast7Days?.some((p) => p.count)}
              emptyText="No papers uploaded in the last 7 days."
            >
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={overview.data?.uploadsLast7Days || []} margin={{ top: 4, right: 8, left: -18, bottom: 0 }}>
                  <CartesianGrid {...GRID_PROPS} />
                  <XAxis dataKey="label" {...AXIS_PROPS} />
                  <YAxis allowDecimals={false} {...AXIS_PROPS} />
                  <Tooltip {...TOOLTIP_STYLE} cursor={{ fill: "#EAF1FD" }} />
                  <Bar dataKey="count" name="Papers uploaded" fill={CHART_COLORS.blue} radius={[6, 6, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </ChartCard>
            <ChartCard
              title="Papers uploaded — last 6 months"
              isLoading={overview.isLoading && !overview.data}
              error={overview.error}
              isEmpty={!overview.data?.uploadsLast6Months?.some((p) => p.count)}
              emptyText="No papers uploaded in the last 6 months."
            >
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={overview.data?.uploadsLast6Months || []} margin={{ top: 4, right: 8, left: -18, bottom: 0 }}>
                  <CartesianGrid {...GRID_PROPS} />
                  <XAxis dataKey="label" {...AXIS_PROPS} />
                  <YAxis allowDecimals={false} {...AXIS_PROPS} />
                  <Tooltip {...TOOLTIP_STYLE} cursor={{ fill: "#EAF1FD" }} />
                  <Bar dataKey="count" name="Papers uploaded" fill={CHART_COLORS.teal} radius={[6, 6, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </ChartCard>
          </div>
        )}

        {stats.error && <Alert>{stats.error}</Alert>}
        {stats.data && (
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
            <div className="flex flex-col gap-4 lg:col-span-2">
              <LatestUploadsCard uploads={stats.data.latestUploads} onOpen={(id) => navigate(`/admin/papers/${id}`)} />
              {hasPermission("Audit") && <RecentActivityCard entries={stats.data.recentActivity} />}
            </div>
            <div className="flex flex-col gap-4">
              <SystemStatusCard system={stats.data.system} token={token} isSuperAdmin={isSuperAdmin} />
              {stats.data.adminPerformance.length > 0 && <AdminPerformanceCard admins={stats.data.adminPerformance} />}
            </div>
          </div>
        )}

        {/* Quick actions -- only real, working destinations. */}
        <div>
          <h2 className="mb-3 text-sm font-bold text-ink-900">Quick actions</h2>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <ShortcutCard icon={UploadCloud} title="Upload PYQ" description="Choose or create an exam, pick a language, and add a question." onClick={() => navigate("/admin/upload")} />
            <ShortcutCard icon={FileStack} title="Uploaded Papers" description="See every paper's status and pick up where you left off." onClick={() => navigate("/admin/papers")} />
            {hasPermission("PublishPaper") && (
              <ShortcutCard icon={ClipboardCheck} title="Review Queue" description="Approve or reject papers waiting on your review." onClick={() => navigate("/admin/review")} />
            )}
            <ShortcutCard icon={ListChecks} title="Tasks" description="View your assigned tasks and update their status." onClick={() => navigate("/admin/tasks")} />
            <ShortcutCard icon={MessageCircle} title="Chat Moderation" description="Handle reported messages, banned words, and muted users." onClick={() => navigate("/admin/chat")} />
            {hasPermission("Audit") && (
              <ShortcutCard icon={ShieldAlert} title="Audit Log" description="Review who published, rejected, or deleted what, and when." onClick={() => navigate("/admin/audit-log")} />
            )}
            {isSuperAdmin && (
              <ShortcutCard icon={Users} title="Manage Admins" description="Create admin accounts and control access." onClick={() => navigate("/admin/admins")} />
            )}
            {hasPermission("ModerateQuestionReports") && (
              <ShortcutCard icon={Flag} title="Question Reports" description="Review reports students filed against questions." onClick={() => navigate("/admin/question-bank/reports")} />
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function SystemStatusCard({ system, token, isSuperAdmin }) {
  const [isReindexing, setIsReindexing] = useState(false);
  const [result, setResult] = useState(null);
  const [error, setError] = useState(null);

  const [isMirroring, setIsMirroring] = useState(false);
  const [mirrorResult, setMirrorResult] = useState(null);
  const [mirrorError, setMirrorError] = useState(null);

  async function handleReindex() {
    setIsReindexing(true);
    setResult(null);
    setError(null);
    try {
      const res = await reindexSearch(token);
      setResult(res.message);
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setIsReindexing(false);
    }
  }

  // Catches up PYQ questions that predate the Question Bank mirror feature (or that failed to
  // mirror at upload time) so they finally show up in "PYQs" (Question Bank search/browse) too.
  // Safe to click repeatedly -- only ever touches questions still missing a mirror.
  async function handleBackfillMirrors() {
    setIsMirroring(true);
    setMirrorResult(null);
    setMirrorError(null);
    try {
      const res = await backfillQuestionBankMirrors(token);
      setMirrorResult(res.message);
    } catch (err) {
      setMirrorError(friendlyError(err));
    } finally {
      setIsMirroring(false);
    }
  }

  return (
    <Card>
      <h3 className="mb-3 text-sm font-bold text-ink-900">System status</h3>
      <div className="flex flex-col gap-2.5">
        <StatusRow icon={Database} label="Database" healthy={system.databaseHealthy} />
        <StatusRow icon={Search} label="Search index" healthy={system.searchIndexHealthy} />
      </div>

      {isSuperAdmin && (
        <>
          <Button variant="secondary" className="mt-4 w-full" onClick={handleReindex} isLoading={isReindexing}>
            <RefreshCw className="h-4 w-4" strokeWidth={2.25} />
            Reindex search
          </Button>
          {result && <div className="mt-2"><Alert type="success">{result}</Alert></div>}
          {error && <div className="mt-2"><Alert>{error}</Alert></div>}

          <Button variant="secondary" className="mt-2 w-full" onClick={handleBackfillMirrors} isLoading={isMirroring}>
            <Copy className="h-4 w-4" strokeWidth={2.25} />
            Backfill Question Bank mirrors
          </Button>
          {mirrorResult && <div className="mt-2"><Alert type="success">{mirrorResult}</Alert></div>}
          {mirrorError && <div className="mt-2"><Alert>{mirrorError}</Alert></div>}
        </>
      )}
    </Card>
  );
}

function StatusRow({ icon: Icon, label, healthy }) {
  return (
    <div className="flex items-center gap-2.5 text-sm">
      <Icon className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2.25} />
      <span className="text-ink-600">{label}</span>
      <span
        className={`ml-auto flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-semibold ${
          healthy ? "bg-mint-50 text-mint-500" : "bg-red-50 text-red-600"
        }`}
      >
        <span className={`h-1.5 w-1.5 rounded-full ${healthy ? "bg-mint-500" : "bg-red-600"}`} />
        {healthy ? "Healthy" : "Unreachable"}
      </span>
    </div>
  );
}

function LatestUploadsCard({ uploads, onOpen }) {
  return (
    <Card>
      <h3 className="mb-3 text-sm font-bold text-ink-900">Latest uploads</h3>
      {uploads.length === 0 ? (
        <p className="text-xs text-ink-400">Nothing uploaded yet.</p>
      ) : (
        <div className="flex flex-col divide-y divide-primary-50">
          {uploads.map((paper) => (
            <button
              key={paper.id}
              type="button"
              onClick={() => onOpen(paper.id)}
              className="flex items-center justify-between gap-3 py-2.5 text-left first:pt-0 last:pb-0"
            >
              <span className="min-w-0">
                <span className="block truncate text-sm font-semibold text-ink-900">
                  {paper.examName} · {paper.year}
                </span>
                <span className="block text-xs text-ink-400">
                  {paper.language} · {paper.questionCount} question{paper.questionCount === 1 ? "" : "s"}
                </span>
              </span>
              <StatusBadge status={paper.status} />
            </button>
          ))}
        </div>
      )}
    </Card>
  );
}

function RecentActivityCard({ entries }) {
  return (
    <Card>
      <div className="mb-3 flex items-center justify-between">
        <h3 className="text-sm font-bold text-ink-900">Recent activity</h3>
        <a href="/admin/audit-log" className="flex items-center gap-1 text-xs font-semibold text-secondary-500 hover:underline">
          View all <ArrowRight className="h-3 w-3" strokeWidth={2.5} />
        </a>
      </div>
      {entries.length === 0 ? (
        <p className="text-xs text-ink-400">Nothing logged yet.</p>
      ) : (
        <div className="flex flex-col divide-y divide-primary-50">
          {entries.map((entry) => (
            <div key={entry.id} className="py-2.5 first:pt-0 last:pb-0">
              <p className="text-sm text-ink-900">
                <span className="font-semibold">{entry.adminName}</span> · {entry.action.replace(".", " ")}
              </p>
              <p className="text-xs text-ink-400">{new Date(entry.createdAt).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" })}</p>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}

function AdminPerformanceCard({ admins }) {
  return (
    <Card>
      <h3 className="mb-3 text-sm font-bold text-ink-900">Admin performance</h3>
      <div className="flex flex-col gap-3">
        {admins.map((a) => (
          <div key={a.adminId} className="flex items-center justify-between gap-3 text-sm">
            <span className="truncate font-medium text-ink-900">{a.fullName}</span>
            <span className="shrink-0 text-xs text-ink-400">
              {a.papersUploaded} uploaded · {a.papersPublished} published
            </span>
          </div>
        ))}
      </div>
    </Card>
  );
}

function ShortcutCard({ icon: Icon, title, description, onClick }) {
  return (
    <button type="button" onClick={onClick} className="text-left">
      <Card className="flex h-full flex-col gap-3 transition-shadow hover:shadow-cardHover">
        <span className="flex h-10 w-10 items-center justify-center rounded-xl2 bg-secondary-50 text-secondary-500">
          <Icon className="h-5 w-5" strokeWidth={2.25} />
        </span>
        <span className="flex items-center gap-1.5 text-sm font-bold text-ink-900">
          {title}
          <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
        </span>
        <span className="text-xs text-ink-400">{description}</span>
      </Card>
    </button>
  );
}
