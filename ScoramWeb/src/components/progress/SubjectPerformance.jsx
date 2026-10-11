import { useState } from "react";
import { Link } from "react-router-dom";
import { ChevronDown, ChevronRight } from "lucide-react";
import { AccuracyBar, ChangeSummary, ConfidenceNote, TopicStatusBadge } from "./ui";
import { fmtSeconds, plural, topicLink } from "./format";

// Subject rows (expandable -> topics) straight from GET /api/progress/subjects?includeTopics=true.
// Every number/status is the server's; "Unclassified" is an explicit bucket, never merged or inferred.
export default function SubjectPerformance({ subjects, filters }) {
  const [openSubject, setOpenSubject] = useState(null);

  return (
    <ul className="space-y-2">
      {subjects.map((s) => {
        const isOpen = openSubject === s.subject;
        const panelId = `subject-panel-${s.subject.replace(/\W+/g, "-")}`;
        return (
          <li key={s.subject} className="rounded-xl2 border border-primary-100 bg-white shadow-card">
            <button
              type="button"
              aria-expanded={isOpen}
              aria-controls={panelId}
              onClick={() => setOpenSubject(isOpen ? null : s.subject)}
              className="flex w-full items-start gap-3 p-4 text-left"
            >
              <span className="mt-0.5 text-ink-400" aria-hidden="true">
                {isOpen ? <ChevronDown className="h-4 w-4" strokeWidth={2.5} /> : <ChevronRight className="h-4 w-4" strokeWidth={2.5} />}
              </span>
              <span className="min-w-0 flex-1">
                <span className="flex flex-wrap items-baseline justify-between gap-x-3">
                  <span className="text-sm font-bold text-ink-900">
                    {s.isUnclassified ? "Unclassified" : s.subject}
                  </span>
                  <span className="text-[11px] text-ink-400">
                    {plural(s.attempted, "question")} answered · {s.correct} correct · {s.incorrect} wrong · {s.skipped} skipped
                  </span>
                </span>
                <span className="mt-2 block">
                  <AccuracyBar value={s.accuracyPercent} label={`${s.subject} accuracy`} />
                </span>
                <span className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1">
                  <ConfidenceNote confidence={s.confidence} attempted={s.attempted} />
                  {s.weakTopicCount > 0 && (
                    <span className="text-[11px] font-semibold text-accent-600">
                      {plural(s.weakTopicCount, "topic")} to work on (of {s.topicCount})
                    </span>
                  )}
                  {s.avgSecondsPerAnswered != null && (
                    <span className="text-[11px] text-ink-400">~{fmtSeconds(s.avgSecondsPerAnswered)} per question</span>
                  )}
                </span>
              </span>
            </button>

            {isOpen && (
              <div id={panelId} className="border-t border-primary-100 px-4 pb-4 pt-3">
                <ChangeSummary change={s.trend} title="Recent change in this subject" />
                {s.topics.length === 0 ? (
                  <p className="mt-3 text-xs text-ink-400">No topic breakdown for this subject.</p>
                ) : (
                  <ul className="mt-3 divide-y divide-primary-50">
                    {s.topics.map((t) => (
                      <li key={`${t.subject}/${t.topic}`} className="py-2.5">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <span className="text-sm font-semibold text-ink-900">
                            {t.isUnclassified ? "Unclassified (no topic recorded)" : t.topic}
                          </span>
                          <TopicStatusBadge status={t.status} attempted={t.attempted} />
                        </div>
                        <div className="mt-1.5">
                          <AccuracyBar value={t.accuracyPercent} label={`${t.topic} accuracy`} />
                        </div>
                        <p className="mt-1 flex flex-wrap items-center justify-between gap-2 text-[11px] text-ink-400">
                          <span>
                            {plural(t.attempted, "question")} answered · {t.correct} correct · {t.incorrect} wrong · {t.skipped} skipped
                            {t.openMistakes > 0 ? ` · ${plural(t.openMistakes, "open mistake")}` : ""}
                          </span>
                          <Link to={topicLink(t.subject, t.topic, filters)} className="font-semibold text-secondary-500 hover:underline">
                            Topic details
                          </Link>
                        </p>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            )}
          </li>
        );
      })}
    </ul>
  );
}
