import { useEffect, useState } from "react";
import {
  UploadCloud, ListChecks, Users, BookOpen, FileStack, ArrowRight, RefreshCw,
  FileQuestion, CheckCircle2, FileEdit, ClipboardCheck, GraduationCap, Flag,
  MessageCircle, Database, Search, HardDrive, ShieldAlert, TrendingUp,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { Bar, BarChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { useAdminAuth } from "../context/AdminAuthContext";
import { getDashboardStats } from "../api/dashboard";
import { reindexSearch } from "../api/papers";
import { PageHeader, Card, Button, Alert, StatusBadge, friendlyError } from "../components/AdminUI";

export default function AdminDashboard() {
  const navigate = useNavigate();
  const { admin, token, isSuperAdmin, hasPermission } = useAdminAuth();
  const [stats, setStats] = useState(null);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState(null);

  useEffect(() => {
    let cancelled = false;
    setIsLoading(true);
    setLoadError(null);
    getDashboardStats(token)
      .then((res) => {
        if (!cancelled) setStats(res);
      })
      .catch((err) => {
        if (!cancelled) setLoadError(err.message);
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [token]);

  return (
    <div>
      <PageHeader title={`Welcome, ${admin?.fullName?.split(" ")[0] || "Admin"}`} subtitle={`Logged in as ${admin?.role}`} />

      {isLoading && <p className="p-6 text-sm text-ink-400">Loading dashboard…</p>}
      {loadError && <div className="p-6"><Alert>{loadError}</Alert></div>}

      {!isLoading && !loadError && stats && (
        <div className="flex flex-col gap-4 p-6">
          {/* Content overview -- clickable where a real destination exists */}
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
            <StatCard icon={FileQuestion} label="Total questions" value={stats.content.totalQuestions} />
            <StatCard
              icon={FileStack}
              label="Total papers"
              value={stats.content.totalPapers}
              onClick={() => navigate("/admin/papers")}
            />
            <StatCard icon={BookOpen} label="Total exams" value={stats.content.totalExams} onClick={() => navigate("/admin/upload")} />
            <StatCard
              icon={CheckCircle2}
              label="Published papers"
              value={stats.content.publishedPapers}
              onClick={() => navigate("/admin/papers?status=Published")}
            />
            <StatCard
              icon={FileEdit}
              label="Draft papers"
              value={stats.content.draftPapers}
              onClick={() => navigate("/admin/papers?status=Draft")}
            />
            <StatCard
              icon={ClipboardCheck}
              label="Pending review"
              value={stats.content.pendingReviewPapers}
              onClick={hasPermission("PublishPaper") ? () => navigate("/admin/papers?status=PendingReview") : undefined}
            />
            <StatCard icon={GraduationCap} label="Mock tests" value={stats.content.totalMockTests} />
            <StatCard
              icon={UploadCloud}
              label="Today's uploads"
              value={stats.activity.todayUploads}
              onClick={() => navigate("/admin/papers")}
            />
          </div>

          {/* Activity signals */}
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
            <StatCard icon={Users} label="Active today" value={stats.activity.todayActiveUsers} tone="secondary" />
            <StatCard icon={Flag} label="Pending question reports" value={stats.activity.pendingQuestionReports} tone="accent" />
            <StatCard icon={MessageCircle} label="Pending chat reports" value={stats.activity.pendingChatReports} tone="accent" />
            <StatCard icon={ListChecks} label="Total admins" value={stats.adminPerformance.length} tone="secondary" />
          </div>

          {/* Graphs */}
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
            <GraphCard title="Uploads — last 7 days" data={stats.dailyUploads} />
            <GraphCard title="Uploads — last 6 months" data={stats.monthlyUploads} />
          </div>

          <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
            <div className="flex flex-col gap-4 lg:col-span-2">
              <LatestUploadsCard uploads={stats.latestUploads} onOpen={(id) => navigate(`/admin/papers/${id}`)} />
              {hasPermission("Audit") && <RecentActivityCard entries={stats.recentActivity} />}
            </div>

            <div className="flex flex-col gap-4">
              <SystemStatusCard system={stats.system} token={token} isSuperAdmin={isSuperAdmin} />
              {stats.adminPerformance.length > 0 && <AdminPerformanceCard admins={stats.adminPerformance} />}
            </div>
          </div>

          {/* Quick actions -- only real, working destinations. Bulk Import, Mock Test creation, and
              Quiz creation aren't built yet (later phases), so they're deliberately left out rather
              than linking to something that doesn't exist. */}
          <div>
            <h2 className="mb-3 text-sm font-bold text-ink-900">Quick actions</h2>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
              <ShortcutCard
                icon={UploadCloud}
                title="Upload PYQ"
                description="Choose or create an exam, pick a language, and add a question."
                onClick={() => navigate("/admin/upload")}
              />
              <ShortcutCard
                icon={FileStack}
                title="Uploaded Papers"
                description="See every paper's status and pick up where you left off."
                onClick={() => navigate("/admin/papers")}
              />
              {hasPermission("PublishPaper") && (
                <ShortcutCard
                  icon={ClipboardCheck}
                  title="Review Queue"
                  description="Approve or reject papers waiting on your review."
                  onClick={() => navigate("/admin/review")}
                />
              )}
              <ShortcutCard
                icon={ListChecks}
                title="Tasks"
                description="View your assigned tasks and update their status."
                onClick={() => navigate("/admin/tasks")}
              />
              <ShortcutCard
                icon={MessageCircle}
                title="Chat Moderation"
                description="Handle reported messages, banned words, and muted users."
                onClick={() => navigate("/admin/chat")}
              />
              {hasPermission("Audit") && (
                <ShortcutCard
                  icon={ShieldAlert}
                  title="Audit Log"
                  description="Review who published, rejected, or deleted what, and when."
                  onClick={() => navigate("/admin/audit-log")}
                />
              )}
              {isSuperAdmin && (
                <ShortcutCard
                  icon={Users}
                  title="Manage Admins"
                  description="Create admin accounts and control access."
                  onClick={() => navigate("/admin/admins")}
                />
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

const TONE_STYLES = {
  primary: "bg-primary-50 text-primary-600",
  secondary: "bg-secondary-50 text-secondary-500",
  accent: "bg-accent-50 text-accent-600",
};

function StatCard({ icon: Icon, label, value, onClick, tone = "primary" }) {
  const content = (
    <Card className={`flex items-center gap-3 ${onClick ? "transition-shadow hover:shadow-cardHover" : ""}`}>
      <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl2 ${TONE_STYLES[tone]}`}>
        <Icon className="h-5 w-5" strokeWidth={2.25} />
      </span>
      <span className="min-w-0">
        <span className="block text-xl font-extrabold text-ink-900">{value ?? "—"}</span>
        <span className="block truncate text-xs text-ink-400">{label}</span>
      </span>
    </Card>
  );

  if (!onClick) return content;
  return (
    <button type="button" onClick={onClick} className="text-left">
      {content}
    </button>
  );
}

function GraphCard({ title, data }) {
  const hasAnyUploads = data.some((d) => d.count > 0);
  return (
    <Card>
      <div className="mb-3 flex items-center gap-2">
        <TrendingUp className="h-4 w-4 text-secondary-500" strokeWidth={2.25} />
        <h3 className="text-sm font-bold text-ink-900">{title}</h3>
      </div>
      {!hasAnyUploads ? (
        <p className="py-8 text-center text-xs text-ink-400">No papers uploaded in this window yet.</p>
      ) : (
        <div className="h-52">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={data} margin={{ top: 4, right: 4, left: -20, bottom: 0 }}>
              <XAxis dataKey="label" tick={{ fontSize: 11, fill: "#9CA3AF" }} axisLine={false} tickLine={false} />
              <YAxis allowDecimals={false} tick={{ fontSize: 11, fill: "#9CA3AF" }} axisLine={false} tickLine={false} />
              <Tooltip
                cursor={{ fill: "#EAF1FD" }}
                contentStyle={{ borderRadius: 12, border: "1px solid #CFE1FA", fontSize: 12 }}
              />
              <Bar dataKey="count" name="Papers uploaded" fill="#1E63D5" radius={[6, 6, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}
    </Card>
  );
}

function SystemStatusCard({ system, token, isSuperAdmin }) {
  const [isReindexing, setIsReindexing] = useState(false);
  const [result, setResult] = useState(null);
  const [error, setError] = useState(null);

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

  return (
    <Card>
      <h3 className="mb-3 text-sm font-bold text-ink-900">System status</h3>
      <div className="flex flex-col gap-2.5">
        <StatusRow icon={Database} label="Database" healthy={system.databaseHealthy} />
        <StatusRow icon={Search} label="Search index" healthy={system.searchIndexHealthy} />
        <div className="flex items-center gap-2.5 text-sm">
          <HardDrive className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2.25} />
          <span className="text-ink-600">Uploads storage</span>
          <span className="ml-auto font-semibold text-ink-900">{system.storageUsedMb} MB</span>
        </div>
      </div>

      {isSuperAdmin && (
        <>
          <Button variant="secondary" className="mt-4 w-full" onClick={handleReindex} isLoading={isReindexing}>
            <RefreshCw className="h-4 w-4" strokeWidth={2.25} />
            Reindex search
          </Button>
          {result && <div className="mt-2"><Alert type="success">{result}</Alert></div>}
          {error && <div className="mt-2"><Alert>{error}</Alert></div>}
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
