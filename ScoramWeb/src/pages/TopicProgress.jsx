import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { ArrowLeft, NotebookPen } from "lucide-react";
import { getTopicDetail } from "../api/smartProgress";
import { useApiQuery } from "../hooks/useApiQuery";
import {
  AccuracyBar, Card, ChangeSummary, EmptyState, ErrorState, InlineNote, PrimaryLinkClass, SecondaryLinkClass,
  SectionTitle, SkeletonBlock, StatTile, TopicStatusBadge, ConfidenceNote,
} from "../components/progress/ui";
import { fmtDate, fmtPct, fmtSeconds, kindLabel, plural, practiceLink } from "../components/progress/format";

// Topic detail: /progress/topic?subject=&topic=[&examId&kind&days&myExamsOnly]. All numbers, the status
// and every recommendation come from GET /api/progress/topics/detail.
export default function TopicProgress() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const subject = params.get("subject") || "";
  const topic = params.get("topic") || "";
  const filters = {
    examId: params.get("examId") || undefined,
    kind: params.get("kind") || undefined,
    days: params.get("days") ? Number(params.get("days")) : undefined,
    myExamsOnly: params.get("myExamsOnly") === "true" || undefined,
  };
  const key = params.toString();
  const q = useApiQuery((signal) => getTopicDetail({ subject, topic, ...filters }, { signal }), [key], { enabled: Boolean(subject && topic) });

  const d = q.data;
  const t = d?.topic;
  const link = t ? practiceLink(t.practiceTarget) : null;
  const unclassified = t?.isUnclassified;

  return (
    <div className="px-4 pb-10 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <button type="button" onClick={() => navigate(-1)} className="flex items-center gap-1.5 text-sm font-semibold text-secondary-500">
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} aria-hidden="true" />
        Back
      </button>

      <h1 className="mt-3 text-xl font-extrabold text-ink-900 sm:text-2xl">{topic || "Topic"}</h1>
      <p className="text-sm text-ink-400">{subject}</p>

      {!subject || !topic ? (
        <div className="mt-6"><EmptyState title="No topic selected">Open a topic from your Progress page.</EmptyState></div>
      ) : q.initialLoading ? (
        <div className="mt-6"><SkeletonBlock rows={5} label="Loading topic" /></div>
      ) : q.error && !d ? (
        <div className="mt-6">
          {q.error.status === 404
            ? <EmptyState title="No answers for this topic yet">Answer some questions on this topic in a test and it will appear here.</EmptyState>
            : <ErrorState error={q.error} onRetry={q.reload} />}
        </div>
      ) : d ? (
        <>
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <TopicStatusBadge status={t.status} attempted={t.attempted} />
            <ConfidenceNote confidence={t.confidence} attempted={t.attempted} />
          </div>

          <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
            <StatTile label="Accuracy" value={fmtPct(t.accuracyPercent)} sub={`${plural(t.attempted, "question")} answered`} />
            <StatTile label="Correct / wrong" value={`${t.correct} / ${t.incorrect}`} sub={`${t.skipped} skipped`} />
            <StatTile label="Avg time per answer" value={fmtSeconds(t.avgSecondsPerAnswered)} sub={t.timedQuestions ? `${plural(t.timedQuestions, "timed question")}` : "timing not recorded"} />
            <StatTile label="Last practised" value={fmtDate(t.lastAttemptedAt)} />
          </div>

          {unclassified && (
            <div className="mt-3"><InlineNote tone="warn">Some questions here have no topic assigned, so SCORAM can't give topic-specific practice for them.</InlineNote></div>
          )}

          <SectionTitle hint="Accuracy of each assessment that included this topic (oldest first).">Trend</SectionTitle>
          <Card>
            <ChangeSummary change={t.trend} title="Recent vs previous period" />
            {d.trend?.length > 0 && (
              <ul className="mt-4 space-y-2" aria-label="Accuracy per assessment">
                {d.trend.map((p) => (
                  <li key={p.attemptId}>
                    <Link to={`/tests/result/${p.attemptId}`} className="block rounded-lg p-1 hover:bg-primary-50/50">
                      <p className="mb-1 flex justify-between text-[11px] text-ink-400">
                        <span>{fmtDate(p.date)} · {kindLabel(p.testKind)}</span>
                        <span>{p.correct} of {plural(p.attempted, "question")} correct</span>
                      </p>
                      <AccuracyBar value={p.accuracyPercent} label={`Accuracy on ${fmtDate(p.date)}`} />
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </Card>

          <SectionTitle>Mistakes in this topic</SectionTitle>
          <Card>
            <p className="text-sm text-ink-900">
              <strong>{d.openMistakes}</strong> open · <strong>{d.repeatedMistakes}</strong> wrong more than once · <strong>{d.correctedMistakes}</strong> corrected
            </p>
            {t.revision && (
              <p className="mt-1 text-xs text-ink-400">
                Revision answers (counted separately from your test scores): {t.revision.correct} of {t.revision.attempted} correct ({fmtPct(t.revision.accuracyPercent)}).
              </p>
            )}
            <div className="mt-3 flex flex-wrap gap-2">
              <Link to={`/mistake-book?${new URLSearchParams({ subject, topic }).toString()}`} className={SecondaryLinkClass()}>
                <NotebookPen className="h-3.5 w-3.5" aria-hidden="true" /> Open in Mistake Book
              </Link>
              {d.openMistakes > 0 && (
                <Link to={`/mistake-book/revise?${new URLSearchParams({ subject, topic }).toString()}`} className={PrimaryLinkClass()}>Revise these mistakes</Link>
              )}
            </div>
          </Card>

          <SectionTitle hint="Each suggestion shows the recorded numbers behind it.">What to do next</SectionTitle>
          <div className="space-y-2">
            {t.recommendations.map((r) => (
              <Card key={r.code}>
                <p className="text-sm font-bold text-ink-900">{r.title}</p>
                <p className="mt-0.5 text-xs text-ink-600">{r.reason}</p>
              </Card>
            ))}
          </div>
          <div className="mt-3">
            {link ? (
              <Link to={link} className={PrimaryLinkClass()}>Practice this topic</Link>
            ) : (
              <InlineNote>Practice shortcut unavailable: this topic's name doesn't map to a single Question Bank topic. You can still pick it manually from Practice Tests.</InlineNote>
            )}
          </div>
        </>
      ) : null}
    </div>
  );
}
