import { Link, useSearchParams } from "react-router-dom";
import { BookMarked, CalendarCheck, ClipboardList, Flame, NotebookPen } from "lucide-react";
import { getProgressOverview, getProgressSubjects } from "../../api/smartProgress";
import { useApiQuery } from "../../hooks/useApiQuery";
import { useMyExams } from "../../context/MyExamsContext";
import SubjectPerformance from "./SubjectPerformance";
import TrendChart from "./TrendChart";
import {
  Card, ChangeSummary, EmptyState, ErrorState, InlineNote, PrimaryLinkClass, SecondaryLinkClass, SectionTitle, SelectField,
  SkeletonBlock, SkeletonCards, StatTile, TopicStatusBadge,
} from "./ui";
import { fmtDate, fmtPct, fmtSeconds, fmtDay, KIND_OPTIONS, kindLabel, PERIOD_OPTIONS, plural, topicLink } from "./format";

// "Smart Progress" block on /progress. Filters live in the URL (?exam=&kind=&days=&mine=) so a view
// can be shared/bookmarked and Back works; each change aborts the previous in-flight requests.
export function useProgressFilters() {
  const [params, setParams] = useSearchParams();
  const filters = {
    examId: params.get("exam") || "",
    kind: params.get("kind") || "",
    days: params.get("days") || "",
    myExamsOnly: params.get("mine") === "1",
  };
  function setFilter(key, value) {
    const next = new URLSearchParams(params);
    if (value === "" || value === false || value == null) next.delete(key);
    else next.set(key, value === true ? "1" : value);
    setParams(next, { replace: true });
  }
  function clear() {
    const next = new URLSearchParams(params);
    ["exam", "kind", "days", "mine"].forEach((k) => next.delete(k));
    setParams(next, { replace: true });
  }
  const active = Boolean(filters.examId || filters.kind || filters.days || filters.myExamsOnly);
  return { filters, setFilter, clear, active };
}

function apiFilters(f) {
  return {
    examId: f.examId || undefined,
    kind: f.kind || undefined,
    days: f.days ? Number(f.days) : undefined,
    myExamsOnly: f.myExamsOnly || undefined,
  };
}

export default function SmartProgressSection() {
  const { filters, setFilter, clear, active } = useProgressFilters();
  const { exams } = useMyExams();
  const key = JSON.stringify(filters);

  const overview = useApiQuery((signal) => getProgressOverview(apiFilters(filters), { signal }), [key]);
  const subjects = useApiQuery((signal) => getProgressSubjects(apiFilters(filters), { includeTopics: true, signal }), [key]);

  const o = overview.data;
  const busy = overview.loading || subjects.loading;
  const hasHistory = o && o.totals.completedAssessments > 0;

  return (
    <div className="mt-4">
      <Card className="!p-3 sm:!p-4" aria-label="Progress filters">
        <form className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4" onSubmit={(e) => e.preventDefault()}>
          <SelectField label="Exam" value={filters.examId} onChange={(v) => setFilter("exam", v)}>
            <option value="">All exams</option>
            {exams.map((e) => (
              <option key={e.examId} value={e.examId}>{e.examName}</option>
            ))}
          </SelectField>
          <SelectField label="Assessment type" value={filters.kind} onChange={(v) => setFilter("kind", v)}>
            {KIND_OPTIONS.map((k) => (
              <option key={k.value} value={k.value}>{k.label}</option>
            ))}
          </SelectField>
          <SelectField label="Time period" value={filters.days} onChange={(v) => setFilter("days", v)}>
            {PERIOD_OPTIONS.map((k) => (
              <option key={k.value} value={k.value}>{k.label}</option>
            ))}
          </SelectField>
          <label className="flex items-end gap-2 pb-2.5 text-xs font-semibold text-ink-600">
            <input
              type="checkbox"
              checked={filters.myExamsOnly}
              onChange={(e) => setFilter("mine", e.target.checked)}
              className="h-4 w-4 rounded border-primary-100"
            />
            Only my selected exams
          </label>
        </form>
        {active && (
          <button type="button" onClick={clear} className="mt-2 text-xs font-semibold text-secondary-500 hover:underline">
            Clear filters
          </button>
        )}
      </Card>

      <div aria-busy={busy} className={busy && o ? "opacity-70 transition-opacity" : ""}>
        {overview.initialLoading && (
          <div className="mt-4 space-y-4">
            <SkeletonCards label="Loading your progress" />
            <Card><SkeletonBlock rows={4} /></Card>
          </div>
        )}

        {overview.error && !o && (
          <div className="mt-4"><ErrorState error={overview.error} onRetry={overview.reload} title="Couldn't load your progress analytics." /></div>
        )}
        {overview.error && o && (
          <div className="mt-4"><ErrorState error={overview.error} onRetry={overview.reload} title="Couldn't refresh these numbers." /></div>
        )}

        {o && !hasHistory && !active && (
          <div className="mt-4">
            <EmptyState
              icon={ClipboardList}
              title="No completed assessments yet"
              action={
                <>
                  <Link to="/tests" className={PrimaryLinkClass()}>Take a test</Link>
                  <Link to="/pyq" className={SecondaryLinkClass()}>Try a Previous Year Paper</Link>
                </>
              }
            >
              Your accuracy, weak topics and improvement will appear here after you submit your first Practice, Mock, PYP or Quiz attempt.
            </EmptyState>
          </div>
        )}

        {o && !hasHistory && active && (
          <div className="mt-4">
            <EmptyState
              icon={ClipboardList}
              title="No completed assessments match these filters"
              action={<button type="button" onClick={clear} className={PrimaryLinkClass()}>Clear filters</button>}
            >
              Try a longer time period, another exam or all assessment types.
            </EmptyState>
          </div>
        )}

        {o && hasHistory && <OverviewBody o={o} filters={filters} />}
      </div>

      {/* Subjects: separate request, so a failure here never hides the rest of the dashboard. */}
      {(hasHistory || subjects.data) && (
        <>
          <SectionTitle hint="Tap a subject to see its topics. Accuracy = correct answers ÷ questions answered.">Subject performance</SectionTitle>
          {subjects.initialLoading && <Card><SkeletonBlock rows={5} label="Loading subjects" /></Card>}
          {subjects.error && <ErrorState error={subjects.error} onRetry={subjects.reload} title="Couldn't load subject performance." />}
          {subjects.data && subjects.data.subjects.length === 0 && (
            <EmptyState title="No subject data for this selection">Answers need a subject on the question to appear here.</EmptyState>
          )}
          {subjects.data && subjects.data.subjects.length > 0 && (
            <SubjectPerformance subjects={subjects.data.subjects} filters={filters} />
          )}
          {subjects.data?.dataQuality?.notes?.length > 0 && (
            <div className="mt-3 space-y-1.5">
              {subjects.data.dataQuality.notes.map((n) => <InlineNote key={n}>{n}</InlineNote>)}
            </div>
          )}
        </>
      )}
    </div>
  );
}

function OverviewBody({ o, filters }) {
  const t = o.totals;
  const trendPoints = o.scoreTrend.map((p) => ({
    label: `${fmtDate(p.date)} · ${kindLabel(p.testKind)}`,
    value: p.accuracyPercent,
    detail: `${plural(p.attempted, "question")} answered`,
  }));

  return (
    <>
      <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
        <StatTile label="Completed assessments" value={t.completedAssessments} />
        <StatTile label="Questions answered" value={t.questionsAnswered} sub={`${t.skipped} skipped`} />
        <StatTile
          label="Accuracy"
          value={fmtPct(t.accuracyPercent)}
          sub={t.questionsAnswered > 0 ? `${t.correct} of ${t.questionsAnswered} correct` : "Nothing answered yet"}
        />
        <StatTile label="Avg time per answered question" value={fmtSeconds(t.avgSecondsPerAnswered)} sub={t.avgSecondsPerAnswered == null ? "Timing not recorded" : undefined} />
      </div>

      {t.byKind.length > 0 && (
        <ul className="mt-3 flex flex-wrap gap-2 text-xs text-ink-600">
          {t.byKind.map((k) => (
            <li key={k.testKind} className="rounded-full border border-primary-100 bg-white px-3 py-1">
              <span className="font-semibold">{kindLabel(k.testKind)}</span>: {plural(k.count, "attempt")}, avg accuracy {fmtPct(k.avgAccuracyPercent)}
            </li>
          ))}
        </ul>
      )}

      <div className="mt-3 flex flex-wrap items-center gap-x-5 gap-y-1 rounded-xl2 border border-primary-100 bg-white px-4 py-3 text-xs text-ink-600 shadow-card">
        <span className="flex items-center gap-1.5 font-bold text-ink-900">
          <Flame className="h-4 w-4 text-accent-500" strokeWidth={2.25} aria-hidden="true" />
          {plural(o.currentStreak, "day")} streak
        </span>
        <span>Longest: {plural(o.longestStreak, "day")}</span>
        <span>Active {o.activeDaysLast7} of the last 7 days</span>
      </div>

      <SectionTitle hint="Recent window vs the window before it, in percentage points of accuracy.">Improvement</SectionTitle>
      <div className="grid gap-3 sm:grid-cols-2">
        <Card><ChangeSummary change={o.last7Days} title="Last 7 days" /></Card>
        <Card><ChangeSummary change={o.last30Days} title="Last 30 days" /></Card>
      </div>

      {trendPoints.length >= 2 && (
        <>
          <SectionTitle>Accuracy per assessment</SectionTitle>
          <Card><TrendChart points={trendPoints} title="Accuracy per assessment" /></Card>
        </>
      )}

      <SectionTitle hint="Topics the server flagged as needing work. Topics with too few answers are not judged.">Weak-topic summary</SectionTitle>
      <WeakTopics o={o} filters={filters} />

      <SectionTitle>Revision (Mistake Book)</SectionTitle>
      <RevisionCard r={o.revision} />

      <SectionTitle>Today's study plan</SectionTitle>
      <PlanCard p={o.studyPlan} />

      <SectionTitle>Recent activity</SectionTitle>
      {o.recentActivity.length === 0 ? (
        <EmptyState title="No recent assessments in this selection" />
      ) : (
        <ul className="space-y-2">
          {o.recentActivity.map((a) => (
            <li key={a.attemptId}>
              <Link to={`/tests/result/${a.attemptId}`} className="flex items-center justify-between gap-3 rounded-xl2 border border-primary-100 bg-white p-3 shadow-card hover:bg-primary-50/40">
                <span className="min-w-0">
                  <span className="block truncate text-sm font-semibold text-ink-900">{a.title}</span>
                  <span className="block text-[11px] text-ink-400">
                    {kindLabel(a.testKind)} · {fmtDate(a.submittedAt)} · {fmtSeconds(a.timeTakenSeconds)}
                  </span>
                </span>
                <span className="shrink-0 text-right">
                  <span className="block text-sm font-extrabold tabular-nums text-ink-900">{a.score} / {a.totalQuestions}</span>
                  <span className="block text-[11px] text-ink-400">
                    Accuracy {fmtPct(a.accuracyPercent)} · Score {fmtPct(a.scorePercent)}
                  </span>
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}

      {o.dataQuality?.notes?.length > 0 && (
        <div className="mt-4 space-y-1.5">
          {o.dataQuality.notes.map((n) => <InlineNote key={n}>{n}</InlineNote>)}
        </div>
      )}
    </>
  );
}

function WeakTopics({ o, filters }) {
  return (
    <>
      {o.weakTopics.length === 0 ? (
        <EmptyState title="No weak topics flagged right now">
          {o.insufficientDataTopicCount > 0
            ? `${plural(o.insufficientDataTopicCount, "topic")} don't have enough answers yet to be judged either way.`
            : "Keep practising and this list will update."}
        </EmptyState>
      ) : (
        <ul className="space-y-2">
          {o.weakTopics.map((t) => (
            <li key={`${t.subject}/${t.topic}`} className="rounded-xl2 border border-primary-100 bg-white p-3 shadow-card">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="text-sm font-bold text-ink-900">
                  {t.isUnclassified ? "Unclassified" : t.topic}
                  <span className="ml-1.5 text-xs font-medium text-ink-400">{t.subject}</span>
                </p>
                <TopicStatusBadge status={t.status} attempted={t.attempted} />
              </div>
              <p className="mt-1 text-xs text-ink-600">
                Accuracy {fmtPct(t.accuracyPercent)} from {plural(t.attempted, "answered question")} ({t.correct} correct)
              </p>
              <Link to={topicLink(t.subject, t.topic, filters)} className="mt-1.5 inline-block text-xs font-semibold text-secondary-500 hover:underline">
                See details and what to do next
              </Link>
            </li>
          ))}
        </ul>
      )}
      {o.weakTopics.length > 0 && o.insufficientDataTopicCount > 0 && (
        <p className="mt-2 text-xs text-ink-400">
          {plural(o.insufficientDataTopicCount, "other topic")} don't have enough answers to be judged yet.
        </p>
      )}
    </>
  );
}

function RevisionCard({ r }) {
  return (
    <Card>
      <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-xs sm:grid-cols-4">
        <Fact label="Open mistakes" value={r.open} />
        <Fact label="Due now" value={r.dueNow} />
        <Fact label="New / Learning" value={`${r.new} / ${r.learning}`} />
        <Fact label="Corrected" value={r.corrected} />
        <Fact label="Repeated mistakes" value={r.repeated} />
        <Fact label="Dismissed" value={r.dismissed} />
        <Fact label="Revision answers, last 7 days" value={r.revisionAnswersLast7Days} />
      </dl>
      <div className="mt-3 flex flex-wrap gap-2">
        <Link to="/mistake-book" className={SecondaryLinkClass()}>
          <NotebookPen className="h-3.5 w-3.5" strokeWidth={2.25} aria-hidden="true" />
          Open Mistake Book
        </Link>
        {r.open > 0 && (
          <Link to="/mistake-book/revise" className={PrimaryLinkClass()}>
            <BookMarked className="h-3.5 w-3.5" strokeWidth={2.25} aria-hidden="true" />
            Start revision
          </Link>
        )}
      </div>
      {r.open === 0 && <p className="mt-2 text-xs text-ink-400">No open mistakes to revise.</p>}
    </Card>
  );
}

function PlanCard({ p }) {
  return (
    <Card>
      {p.total === 0 ? (
        <p className="text-xs text-ink-600">No study tasks for today yet.</p>
      ) : (
        <p className="text-sm text-ink-900">
          <span className="font-extrabold">{p.completed} of {p.total}</span> tasks completed
          <span className="text-xs text-ink-400"> · {p.pending} pending · {p.dismissed} dismissed · {fmtDay(p.date)}</span>
        </p>
      )}
      <Link to="/study-plan" className={`${SecondaryLinkClass()} mt-3`}>
        <CalendarCheck className="h-3.5 w-3.5" strokeWidth={2.25} aria-hidden="true" />
        Open Study Plan
      </Link>
    </Card>
  );
}

function Fact({ label, value }) {
  return (
    <div>
      <dt className="text-ink-400">{label}</dt>
      <dd className="text-base font-extrabold tabular-nums text-ink-900">{value}</dd>
    </div>
  );
}
