import { useState } from "react";
import { Link } from "react-router-dom";
import { ChevronDown, ChevronRight } from "lucide-react";
import {
  AccuracyBar, Card, ChangeSummary, ConfidenceNote, EmptyState, InlineNote, PrimaryLinkClass, SectionTitle, StatTile, TopicStatusBadge,
} from "./ui";
import { fmtNum, fmtPct, fmtPoints, fmtSeconds, kindLabel, plural, practiceLink, topicLink } from "./format";

// Sections of the per-assessment analytics. "review" is the existing Question Review list, rendered by the page.
export const RESULT_TABS = [
  { id: "overview", label: "Overview" },
  { id: "subjects", label: "Subjects" },
  { id: "topics", label: "Topics" },
  { id: "review", label: "Question Review" },
  { id: "time", label: "Time" },
  { id: "improvement", label: "Improvement" },
  { id: "next", label: "Next Steps" },
];

const PACE_TEXT = {
  OverAllottedPace: "Above the time allotted per question for this assessment",
  WithinAllottedPace: "Within the time allotted per question for this assessment",
  UnderAllottedPace: "Well under the time allotted per question for this assessment",
};

function Overview({ a }) {
  const s = a.summary;
  return (
    <>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <StatTile label="Score" value={`${fmtNum(s.score)} / ${fmtNum(s.maxScore)}`} sub={s.negativeMarkingRatio ? `−${fmtNum(s.negativeMarkingRatio)} per wrong answer` : "no negative marking"} />
        <StatTile label="Accuracy" value={fmtPct(s.accuracyPercent)} sub={`${s.correct} of ${plural(s.attempted, "answered question")}`} />
        <StatTile label="Attempted" value={`${s.attempted} / ${s.totalQuestions}`} sub={`${s.unattempted} skipped`} />
        <StatTile label="Time" value={fmtSeconds(s.timeTakenSeconds)} sub={s.allottedSeconds ? `of ${fmtSeconds(s.allottedSeconds)} allotted` : "no time limit recorded"} />
      </div>
      <p className="mt-3 text-xs text-ink-400">Score percentage: {fmtPct(s.scorePercent)} (score ÷ total questions). Accuracy counts only the questions you answered.</p>
      {a.notes.length > 0 && <div className="mt-3 space-y-2">{a.notes.map((n) => <InlineNote key={n}>{n}</InlineNote>)}</div>}
    </>
  );
}

function Subjects({ a, filters }) {
  const [open, setOpen] = useState(null);
  if (a.subjects.length === 0) return <EmptyState title="No subject data">This assessment has no subject information.</EmptyState>;
  return (
    <ul className="space-y-2">
      {a.subjects.map((s) => {
        const isOpen = open === s.subject;
        return (
          <li key={s.subject} className="rounded-xl2 border border-primary-100 bg-white shadow-card">
            <button type="button" aria-expanded={isOpen} onClick={() => setOpen(isOpen ? null : s.subject)} className="flex w-full items-start gap-3 p-4 text-left">
              <span className="mt-0.5 text-ink-400" aria-hidden="true">{isOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}</span>
              <span className="min-w-0 flex-1">
                <span className="flex flex-wrap items-baseline justify-between gap-x-3">
                  <span className="text-sm font-bold text-ink-900">{s.isUnclassified ? "Unclassified" : s.subject}</span>
                  <span className="text-[11px] text-ink-400">{s.correct} correct · {s.incorrect} wrong · {s.unattempted} skipped of {s.totalQuestions}</span>
                </span>
                <span className="mt-2 block"><AccuracyBar value={s.accuracyPercent} label={`${s.subject} accuracy`} /></span>
                <span className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-[11px] text-ink-400">
                  <span>Marks {fmtNum(s.marksObtained)} / {fmtNum(s.maxMarks)}</span>
                  {s.timedQuestions > 0 && <span>Avg {fmtSeconds(s.avgSecondsPerAttempted)} per answered question</span>}
                  <ConfidenceNote confidence={s.confidence} attempted={s.attempted} />
                </span>
                {s.accuracyChangePoints != null && (
                  <span className="mt-1 block text-[11px] font-semibold text-ink-600">
                    vs previous attempt: {fmtPoints(s.accuracyChangePoints)} ({fmtPct(s.previousAccuracyPercent)} on {plural(s.previousAttempted ?? 0, "answered question")})
                  </span>
                )}
              </span>
            </button>
            {isOpen && (
              <div className="border-t border-primary-100 p-4">
                <TopicList topics={s.topics} filters={filters} />
              </div>
            )}
          </li>
        );
      })}
    </ul>
  );
}

function TopicList({ topics, filters }) {
  return (
    <ul className="space-y-3">
      {topics.map((t) => (
        <li key={`${t.subject}|${t.topic}`}>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="text-sm font-semibold text-ink-900">{t.isUnclassified && t.topic === "Unclassified" ? "No topic assigned" : t.topic}</p>
            {t.overall && <TopicStatusBadge status={t.overall.status} attempted={t.overall.attempted} />}
          </div>
          <AccuracyBar value={t.accuracyPercent} label={`${t.topic} accuracy in this assessment`} />
          <p className="mt-0.5 text-[11px] text-ink-400">
            This assessment: {t.correct} correct · {t.incorrect} wrong · {t.unattempted} skipped · marks {fmtNum(t.marksObtained)}
            {t.overall && ` · all-time: ${fmtPct(t.overall.accuracyPercent)} over ${plural(t.overall.attempted, "answer")}`}
          </p>
          {!(t.isUnclassified && t.topic === "Unclassified") && (
            <Link to={topicLink(t.subject, t.topic, filters)} className="text-[11px] font-semibold text-secondary-500 hover:underline">Open topic details</Link>
          )}
        </li>
      ))}
    </ul>
  );
}

function Topics({ a, filters }) {
  const all = a.subjects.flatMap((s) => s.topics);
  if (all.length === 0) return <EmptyState title="No topic data">This assessment's questions have no topics assigned.</EmptyState>;
  const ordered = [...all].sort((x, y) => (x.accuracyPercent ?? 101) - (y.accuracyPercent ?? 101));
  return (
    <>
      <p className="mb-3 text-xs text-ink-400">Weakest first. The status badge reflects your whole history of the topic, not just this assessment; topics with fewer than 5 answers aren't labelled strong or weak.</p>
      <Card><TopicList topics={ordered} filters={filters} /></Card>
    </>
  );
}

function Time({ a }) {
  const t = a.time;
  if (!t.available) {
    return <EmptyState title="Time analysis not available">{t.note || "Per-question timings weren't recorded for this attempt."}</EmptyState>;
  }
  return (
    <>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <StatTile label="Median per answered question" value={fmtSeconds(t.medianSecondsPerAttempted)} />
        <StatTile label="Average per answered question" value={fmtSeconds(t.avgSecondsPerAttempted)} />
        <StatTile label="Avg on correct answers" value={fmtSeconds(t.avgSecondsCorrect)} />
        <StatTile label="Avg on wrong answers" value={fmtSeconds(t.avgSecondsIncorrect)} />
      </div>
      {t.paceLabel && (
        <p className="mt-3 text-sm text-ink-900">
          <strong>Pace:</strong> {PACE_TEXT[t.paceLabel] || t.paceLabel}
          {t.allottedSecondsPerQuestion != null && ` (about ${fmtSeconds(t.allottedSecondsPerQuestion)} allotted per question; yours ${fmtNum(t.paceRatio)}× that).`}
        </p>
      )}
      {t.fastIncorrectCount > 0 && t.fastIncorrectThresholdSeconds != null && (
        <p className="mt-1 text-sm text-ink-900">
          <strong>{plural(t.fastIncorrectCount, "question")}</strong> answered wrongly in {fmtSeconds(t.fastIncorrectThresholdSeconds)} or less — these may have been read too quickly.
        </p>
      )}
      {t.note && <div className="mt-3"><InlineNote>{t.note}</InlineNote></div>}

      {t.slowestQuestions.length > 0 && (
        <>
          <SectionTitle>Slowest questions</SectionTitle>
          <ul className="space-y-1.5">
            {t.slowestQuestions.map((q) => (
              <li key={q.questionOrder} className="rounded-lg border border-primary-100 bg-white px-3 py-2 text-xs text-ink-600">
                <strong className="text-ink-900">Q{q.questionOrder}</strong> · {fmtSeconds(q.seconds)} · {q.wasSkipped ? "skipped" : q.isCorrect ? "correct" : "wrong"}
                {q.isUnusuallySlow && " · unusually slow for the time allotted"} · {q.subject}{q.topic !== "Unclassified" ? ` · ${q.topic}` : ""}
              </li>
            ))}
          </ul>
        </>
      )}
      {t.bySubject.length > 0 && (
        <>
          <SectionTitle>Time by subject</SectionTitle>
          <ul className="space-y-1 text-xs text-ink-600">
            {t.bySubject.map((s) => (
              <li key={s.subject}>{s.subject}: {fmtSeconds(s.timeSpentSeconds)} over {plural(s.timedQuestions, "timed question")} (avg {fmtSeconds(s.avgSecondsPerAttempted)})</li>
            ))}
          </ul>
        </>
      )}
    </>
  );
}

function Improvement({ a }) {
  const i = a.improvement;
  return (
    <>
      {i.comparable ? (
        <Card>
          <p className="text-sm font-bold text-ink-900">{i.previousTitle ? `Compared with ${i.previousTitle}` : "Compared with your previous attempt"}</p>
          <p className="text-xs text-ink-400">{i.basis}</p>
          <p className="mt-2 text-sm text-ink-900">{i.note}</p>
          {i.scorePercentChangePoints != null && <p className="mt-1 text-xs text-ink-600">Score percentage change: {fmtPoints(i.scorePercentChangePoints)}.</p>}
          {i.previousAttemptId && <Link to={`/tests/result/${i.previousAttemptId}`} className="mt-2 inline-block text-xs font-semibold text-secondary-500 hover:underline">Open previous result</Link>}
          {i.subjectChanges.length > 0 && (
            <ul className="mt-3 space-y-1 text-xs text-ink-600">
              {i.subjectChanges.map((c) => (
                <li key={c.subject}>
                  <strong className="text-ink-900">{c.subject}</strong>: {fmtPct(c.previousAccuracyPercent)} → {fmtPct(c.currentAccuracyPercent)}
                  {c.changePoints != null && ` (${fmtPoints(c.changePoints)})`} · {c.previousAttempted} → {c.currentAttempted} answered
                </li>
              ))}
            </ul>
          )}
        </Card>
      ) : (
        <EmptyState title="Not enough comparable data yet">{i.basis}</EmptyState>
      )}
      <div className="mt-4">
        <Card><ChangeSummary change={i.last30Days} title={`${kindLabel(a.testKind)}s — last 30 days vs the 30 before`} /></Card>
      </div>
    </>
  );
}

function Next({ a }) {
  if (a.recommendations.length === 0) {
    return <EmptyState title="No specific advice yet" action={<Link to="/progress" className={PrimaryLinkClass()}>See my progress</Link>}>Recommendations appear once a topic has enough answers to say something reliable.</EmptyState>;
  }
  const topics = Object.fromEntries(a.subjects.flatMap((s) => s.topics).map((t) => [`${t.subject}|${t.topic}`, t]));
  return (
    <ul className="space-y-2">
      {a.recommendations.map((r, idx) => {
        const t = r.subject && r.topic ? topics[`${r.subject}|${r.topic}`] : null;
        const practice = t?.overall ? practiceLink(t.overall.practiceTarget) : null;
        return (
          <li key={`${r.code}-${idx}`}>
            <Card>
              <p className="text-sm font-bold text-ink-900">{r.title}{r.topic ? ` — ${r.topic}` : ""}</p>
              <p className="mt-0.5 text-xs text-ink-600">{r.reason}</p>
              <div className="mt-2 flex flex-wrap gap-2">
                {r.actionType === "Revise" && <Link to="/mistake-book" className={PrimaryLinkClass()}>Open Mistake Book</Link>}
                {practice && r.actionType !== "Revise" && <Link to={practice} className={PrimaryLinkClass()}>Practise this topic</Link>}
              </div>
            </Card>
          </li>
        );
      })}
    </ul>
  );
}

export default function AttemptAnalyticsPanel({ analytics, section }) {
  const filters = {};
  switch (section) {
    case "overview": return <Overview a={analytics} />;
    case "subjects": return <Subjects a={analytics} filters={filters} />;
    case "topics": return <Topics a={analytics} filters={filters} />;
    case "time": return <Time a={analytics} />;
    case "improvement": return <Improvement a={analytics} />;
    case "next": return <Next a={analytics} />;
    default: return null;
  }
}
