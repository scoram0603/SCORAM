import { Flag, Gavel, Lightbulb, ClipboardCheck, MessageSquare, ArrowRight } from "lucide-react";
import { Link } from "react-router-dom";
import { useAdminAuth } from "../context/AdminAuthContext";
import { getBadges } from "../api/analytics";
import { Alert, PageHeader } from "../components/AdminUI";
import { useLoader } from "../components/analytics";

// One place to see every moderation queue the signed-in admin may open, with its waiting count. Queues the
// admin has no permission for are not listed and their counts are never returned by the server.
const QUEUES = [
  { key: "review", label: "Papers awaiting review", to: "/admin/review", permission: "PublishPaper", icon: ClipboardCheck, hint: "Approve, reject or publish submitted papers." },
  { key: "questionReports", label: "Question reports", to: "/admin/question-bank/reports", permission: "ModerateQuestionReports", icon: Flag, hint: "Students flagged a wrong or unclear question." },
  { key: "solutions", label: "Solutions to approve", to: "/admin/solutions", permission: "ModerateSolutions", icon: Lightbulb, hint: "Student-submitted explanations waiting for approval." },
  { key: "commentReports", label: "Reported comments", to: "/admin/comment-reports", permission: "ModerateDiscussions", icon: Flag, hint: "Discussion comments students reported." },
  { key: "chatReports", label: "Reported chat messages", to: "/admin/chat", permission: "HandleChatReports", icon: MessageSquare, hint: "Open Group Chat → Reports to act on them." },
];

export default function ModerationOverview() {
  const { token, hasPermission } = useAdminAuth();
  const q = useLoader(() => getBadges(token), [token]);
  const visible = QUEUES.filter((x) => hasPermission(x.permission));
  const total = visible.reduce((n, x) => n + (q.data?.[x.key] || 0), 0);

  return (
    <div>
      <PageHeader title="Moderation Overview" subtitle="Everything waiting on a human decision, in one place." />
      <div className="flex flex-col gap-4 p-4 sm:p-6">
        {q.error && <Alert>{q.error}</Alert>}
        <div className="flex items-center gap-3 rounded-xl2 bg-white p-4 shadow-card">
          <span className="flex h-11 w-11 items-center justify-center rounded-xl2 bg-primary-50 text-primary-600"><Gavel className="h-5 w-5" /></span>
          <div><p className="text-2xl font-extrabold text-ink-900">{q.data ? total.toLocaleString("en-IN") : "—"}</p><p className="text-xs text-ink-600">items waiting across your queues</p></div>
        </div>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {visible.map((x) => {
            const n = q.data?.[x.key];
            return (
              <Link key={x.key} to={x.to} className="group rounded-xl2 bg-white p-4 shadow-card transition-shadow hover:shadow-md">
                <div className="flex items-start justify-between"><x.icon className="h-5 w-5 text-primary-500" />
                  <span className={`rounded-full px-2 py-0.5 text-xs font-bold ${n ? "bg-accent-50 text-accent-600" : "bg-mint-50 text-mint-500"}`}>{n === undefined ? "…" : n ? `${n} waiting` : "All clear"}</span></div>
                <h3 className="mt-3 text-sm font-extrabold text-ink-900">{x.label}</h3>
                <p className="mt-0.5 text-xs text-ink-600">{x.hint}</p>
                <p className="mt-3 flex items-center gap-1 text-xs font-semibold text-secondary-600">Open queue <ArrowRight className="h-3 w-3 transition-transform group-hover:translate-x-0.5" /></p>
              </Link>
            );
          })}
        </div>
        <p className="text-[11px] text-ink-400">Counts are the live number of unresolved items. Average time-to-resolve is not shown because it is not recorded for every queue.</p>
      </div>
    </div>
  );
}
