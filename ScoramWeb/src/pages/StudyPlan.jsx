import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ArrowLeft, CheckCircle2, Circle, EyeOff, RotateCcw } from "lucide-react";
import { getStudyPlanHistory, getTodayStudyPlan, updateStudyTask } from "../api/smartProgress";
import { useApiQuery } from "../hooks/useApiQuery";
import {
  Card, EmptyState, ErrorState, PrimaryLinkClass, SecondaryLinkClass, SectionTitle, SkeletonBlock,
} from "../components/progress/ui";
import { fmtDay, practiceLink } from "../components/progress/format";

function taskAction(task) {
  if (task.type === "ReviseMistakes") return { to: "/mistake-book/revise?due=1", label: "Start revising" };
  if (task.type === "TimedSession") return { to: "/tests/practice", label: "Start a timed test" };
  const link = practiceLink(task.practiceTarget);
  return link ? { to: link, label: "Practise this topic" } : { to: "/tests/practice", label: "Open Practice Tests" };
}

function TaskCard({ task, editable, onChanged }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const done = task.status === "Completed";
  const dismissed = task.status === "Dismissed";
  const action = taskAction(task);

  async function setDismissed(value) {
    setBusy(true);
    setError(null);
    try {
      await updateStudyTask(task.id, value);
      onChanged();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }

  return (
    <li className={`rounded-xl2 border border-primary-100 bg-white p-4 shadow-card ${dismissed ? "opacity-70" : ""}`}>
      <div className="flex items-start gap-3">
        <span className="mt-0.5 shrink-0" aria-hidden="true">
          {done ? <CheckCircle2 className="h-5 w-5 text-mint-500" strokeWidth={2.25} /> : <Circle className="h-5 w-5 text-ink-300" strokeWidth={2.25} />}
        </span>
        <div className="min-w-0 flex-1">
          <p className={`text-sm font-bold text-ink-900 ${dismissed ? "line-through" : ""}`}>{task.title}</p>
          {task.reason && <p className="mt-0.5 text-xs text-ink-600">{task.reason}</p>}
          <p className="mt-1.5 text-xs font-semibold text-ink-600">
            {done ? "Completed" : dismissed ? "Dismissed" : "In progress"} · {task.progressCount} of {task.targetCount}
          </p>
          <progress className="mt-1 h-1.5 w-full overflow-hidden rounded-full [&::-webkit-progress-bar]:bg-primary-50 [&::-webkit-progress-value]:bg-secondary-500" max={task.targetCount || 1} value={task.progressCount} aria-label={`${task.title}: ${task.progressCount} of ${task.targetCount}`} />
          {editable && (
            <div className="mt-3 flex flex-wrap gap-2">
              {!done && !dismissed && <Link to={action.to} className={PrimaryLinkClass()}>{action.label}</Link>}
              {!done && !dismissed && (
                <button type="button" disabled={busy} onClick={() => setDismissed(true)} className={SecondaryLinkClass()}>
                  <EyeOff className="h-3.5 w-3.5" aria-hidden="true" /> Dismiss
                </button>
              )}
              {dismissed && (
                <button type="button" disabled={busy} onClick={() => setDismissed(false)} className={SecondaryLinkClass()}>
                  <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" /> Restore
                </button>
              )}
            </div>
          )}
          {error && <p role="alert" className="mt-2 text-xs text-red-700">{error.message || "Couldn't update that task."}</p>}
        </div>
      </div>
    </li>
  );
}

// Tasks complete only when the server sees real activity (revision answers, answered questions, submitted
// tests); there is no "mark done" button and opening this page never completes anything.
export default function StudyPlan() {
  const navigate = useNavigate();
  const today = useApiQuery((signal) => getTodayStudyPlan({ signal }), []);
  const history = useApiQuery((signal) => getStudyPlanHistory({ days: 14, signal }), []);
  const plan = today.data;
  const pastDays = (history.data?.days || []).filter((d) => d.date !== plan?.date);

  return (
    <div className="mx-auto max-w-2xl px-4 pb-10 pt-4 sm:px-6 lg:pt-6">
      <button type="button" onClick={() => navigate("/progress")} className="flex items-center gap-1.5 text-sm font-semibold text-secondary-500">
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} aria-hidden="true" /> My Progress
      </button>
      <h1 className="mt-3 text-xl font-extrabold text-ink-900 sm:text-2xl">Today's study plan</h1>
      <p className="text-sm text-ink-400">Built from your own results. Tasks tick off automatically as you practise.</p>

      <div className="mt-4" aria-live="polite">
        {today.initialLoading ? <SkeletonBlock rows={5} label="Loading your plan" />
          : today.error && !plan ? <ErrorState error={today.error} onRetry={today.reload} />
          : plan && plan.tasks.length === 0 ? (
            <EmptyState title="No plan yet" action={<Link to="/tests" className={PrimaryLinkClass()}>Take a test</Link>}>{plan.message || "Nothing to plan for today."}</EmptyState>
          ) : plan ? (
            <>
              <p className="mb-2 text-xs font-semibold text-ink-600">
                {plan.summary.completed} of {plan.summary.total} done{plan.summary.dismissed > 0 ? ` · ${plan.summary.dismissed} dismissed` : ""} · {fmtDay(plan.date)}
              </p>
              <ul className="space-y-2">
                {plan.tasks.map((t) => <TaskCard key={t.id} task={t} editable onChanged={() => { today.reload(); history.reload(); }} />)}
              </ul>
            </>
          ) : null}
      </div>

      <SectionTitle hint="Stored progress for earlier days.">Previous days</SectionTitle>
      {history.initialLoading ? <SkeletonBlock rows={3} label="Loading history" />
        : history.error && !history.data ? <ErrorState error={history.error} onRetry={history.reload} />
        : pastDays.length === 0 ? <p className="text-xs text-ink-400">No earlier plans yet.</p>
        : (
          <div className="space-y-4">
            {pastDays.map((d) => (
              <Card key={d.date}>
                <p className="text-sm font-bold text-ink-900">{fmtDay(d.date)}</p>
                <p className="text-xs text-ink-400">{d.summary.completed} of {d.summary.total} completed{d.summary.dismissed > 0 ? ` · ${d.summary.dismissed} dismissed` : ""}</p>
                <ul className="mt-2 space-y-1.5">
                  {d.tasks.map((t) => (
                    <li key={t.id} className="text-xs text-ink-600">
                      <span className="font-semibold">{t.status === "Completed" ? "Done" : t.status === "Dismissed" ? "Dismissed" : "Not finished"}:</span> {t.title} ({t.progressCount}/{t.targetCount})
                    </li>
                  ))}
                </ul>
              </Card>
            ))}
          </div>
        )}
    </div>
  );
}
