import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { ArrowLeft, CheckCircle2, XCircle } from "lucide-react";
import { getRevisionSession, postRevisionAnswer } from "../api/smartProgress";
import { useApiQuery } from "../hooks/useApiQuery";
import QuestionView from "../components/progress/QuestionView";
import { MathText } from "../components/questions/MathText";
import { Card, EmptyState, ErrorState, InlineNote, PrimaryLinkClass, SecondaryLinkClass, SkeletonBlock } from "../components/progress/ui";
import { newRequestId, plural } from "../components/progress/format";

const COUNT = 10;

// Revision session. Questions arrive WITHOUT the answer key; the correct option and explanation are only
// revealed by the POST /api/revision/answers response. Answers here never touch the original test score.
export default function Revision() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const subject = params.get("subject") || undefined;
  const topic = params.get("topic") || undefined;
  const dueOnly = params.get("due") === "1" || undefined;
  const [round, setRound] = useState(0);

  const session = useApiQuery(
    (signal) => getRevisionSession({ count: COUNT, subject, topic, dueOnly }, { signal }),
    [subject, topic, dueOnly, round],
  );

  const [index, setIndex] = useState(0);
  const [picked, setPicked] = useState(null);
  const [result, setResult] = useState(null);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState(null);
  const [tally, setTally] = useState({ correct: 0, wrong: 0, corrected: 0 });
  const requestId = useRef(null);
  const shownAt = useRef(0);
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  const questions = session.data?.questions || [];
  const current = questions[index];
  const finished = session.data && index >= questions.length && questions.length > 0;

  useEffect(() => {
    // new session loaded -> start from the top
    setIndex(0); setPicked(null); setResult(null); setSubmitError(null); setTally({ correct: 0, wrong: 0, corrected: 0 });
  }, [session.data]);

  useEffect(() => {
    shownAt.current = Date.now();
    requestId.current = null;
  }, [index, session.data]);

  async function submit() {
    if (!current || !picked || submitting) return;
    setSubmitting(true);
    setSubmitError(null);
    if (!requestId.current) requestId.current = newRequestId(); // a retry re-uses it, so it's recorded once
    try {
      const res = await postRevisionAnswer({
        questionRef: current.questionRef,
        selectedOption: picked,
        timeSpentSeconds: Math.min(3600, Math.round((Date.now() - shownAt.current) / 1000)),
        clientRequestId: requestId.current,
      });
      if (!alive.current) return;
      setResult(res);
      if (!res.wasDuplicate) {
        setTally((t) => ({
          correct: t.correct + (res.isCorrect ? 1 : 0),
          wrong: t.wrong + (res.isCorrect ? 0 : 1),
          corrected: t.corrected + (res.correctedNow ? 1 : 0),
        }));
      }
    } catch (e) {
      if (alive.current) setSubmitError(e);
    } finally {
      if (alive.current) setSubmitting(false);
    }
  }

  function next() {
    setIndex((i) => i + 1);
    setPicked(null);
    setResult(null);
    setSubmitError(null);
  }

  return (
    <div className="mx-auto max-w-2xl px-4 pb-10 pt-4 sm:px-6 lg:pt-6">
      <button type="button" onClick={() => navigate("/mistake-book")} className="flex items-center gap-1.5 text-sm font-semibold text-secondary-500">
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} aria-hidden="true" /> Mistake Book
      </button>
      <h1 className="mt-3 text-xl font-extrabold text-ink-900">Revise your mistakes</h1>
      {(subject || topic) && <p className="text-sm text-ink-400">{subject}{topic ? ` · ${topic}` : ""}</p>}

      <div className="mt-4" aria-live="polite">
        {session.initialLoading ? <SkeletonBlock rows={6} label="Preparing your revision" />
          : session.error && !session.data ? <ErrorState error={session.error} onRetry={session.reload} />
          : questions.length === 0 ? (
            <EmptyState title="Nothing to revise right now" action={<Link to="/mistake-book" className={PrimaryLinkClass()}>Back to Mistake Book</Link>}>
              {dueOnly ? "No mistakes are due for revision at the moment." : "You have no open mistakes. Questions you get wrong in a test will show up here."}
            </EmptyState>
          ) : finished ? (
            <Card>
              <p className="text-base font-extrabold text-ink-900">Session complete</p>
              <p className="mt-1 text-sm text-ink-600">{tally.correct} correct · {tally.wrong} wrong{tally.corrected > 0 ? ` · ${plural(tally.corrected, "mistake")} now corrected` : ""}</p>
              {session.data.matchingOpenCount > questions.length && (
                <p className="mt-1 text-xs text-ink-400">{session.data.matchingOpenCount - questions.length} more open mistakes match — start another round.</p>
              )}
              <div className="mt-4 flex flex-wrap gap-2">
                <button type="button" onClick={() => setRound((r) => r + 1)} className={PrimaryLinkClass()}>Another round</button>
                <Link to="/mistake-book" className={SecondaryLinkClass()}>Back to Mistake Book</Link>
              </div>
            </Card>
          ) : current ? (
            <Card>
              <p className="mb-3 flex justify-between text-[11px] text-ink-400">
                <span>Question {index + 1} of {questions.length}</span>
                <span>{current.subject} · {current.topic}{current.isRepeated ? " · wrong more than once" : ""}</span>
              </p>
              <QuestionView q={current} picked={picked} correctOption={result?.correctOption} onPick={setPicked} disabled={Boolean(result) || submitting} />

              {!result ? (
                <div className="mt-4">
                  <button type="button" disabled={!picked || submitting} onClick={submit} className={`${PrimaryLinkClass()} disabled:opacity-50`}>
                    {submitting ? "Checking…" : "Check answer"}
                  </button>
                  {submitError && (
                    <p role="alert" className="mt-2 text-xs text-red-700">
                      {submitError.message || "Couldn't save that answer."} Tap "Check answer" to try again — it won't be counted twice.
                    </p>
                  )}
                </div>
              ) : (
                <div className="mt-4" role="status">
                  <p className="flex items-center gap-1.5 text-sm font-bold text-ink-900">
                    {result.isCorrect
                      ? <><CheckCircle2 className="h-4 w-4 text-mint-500" aria-hidden="true" /> Correct</>
                      : <><XCircle className="h-4 w-4 text-red-600" aria-hidden="true" /> Not quite — the answer is {result.correctOption}</>}
                  </p>
                  <p className="mt-1 text-xs text-ink-600">
                    {result.correctedNow
                      ? "This mistake is now marked Corrected."
                      : result.status === "Corrected"
                        ? "Already corrected."
                        : `${result.consecutiveCorrect} of ${result.correctionTarget} correct in a row needed to mark it Corrected.`}
                  </p>
                  {(result.explanation) && (
                    <div className="mt-3 rounded-xl2 bg-primary-50/60 p-3">
                      <p className="text-xs font-bold text-ink-900">Explanation</p>
                      <MathText text={result.explanation} className="mt-1 text-sm text-ink-600" />
                    </div>
                  )}
                  <button type="button" onClick={next} className={`${PrimaryLinkClass()} mt-4`}>
                    {index + 1 >= questions.length ? "Finish" : "Next question"}
                  </button>
                </div>
              )}
              <div className="mt-4"><InlineNote>Revision answers are tracked separately and never change your original test score.</InlineNote></div>
            </Card>
          ) : null}
      </div>
    </div>
  );
}
