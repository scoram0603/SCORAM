import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Zap, Target, Loader2, Play, Clock, TrendingUp, CalendarClock, Eye, Swords, Check, X as XIcon,
} from "lucide-react";
import { previewWeakTopics, listDailyQuizzes } from "../api/quizzes";
import { getMyQuizChallenges, declineQuizChallenge } from "../api/quizChallenges";

// Quizzes (Phase 1: Weak Topics Quiz) -- deliberately NOT another filter form like Practice Tests.
// PYP/Practice/Mock are all "sit down for a real session" modes; this is the opposite -- a quick,
// zero-config, low-pressure daily touchpoint auto-generated from the student's OWN wrong-answer
// history (see QuizzesController/TestAttemptService.SelectWeakTopicQuestionsAsync), meant to pair
// with the streak/XP system so there's a reason to open the app for just a few minutes even on a
// day with no time for a full paper.
const COUNT_OPTIONS = [5, 8, 12, 15];

export default function Quizzes() {
  const navigate = useNavigate();
  const [weakSubjects, setWeakSubjects] = useState(null);
  const [previewStatus, setPreviewStatus] = useState("loading");
  const [questionCount, setQuestionCount] = useState(8);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    previewWeakTopics()
      .then((subjects) => {
        setWeakSubjects(subjects);
        setPreviewStatus("success");
      })
      .catch(() => setPreviewStatus("error"));
  }, []);

  function handleStart() {
    navigate("/tests/instructions/quiz-weak/adhoc", { state: { questionCount } });
  }

  const hasWeakSubjects = previewStatus === "success" && weakSubjects.length > 0;

  return (
    <div className="mx-auto max-w-2xl px-4 pb-10 pt-4 sm:px-6 lg:pt-6">
      <div>
        <h1 className="text-xl font-extrabold text-ink-900 sm:text-2xl">Quizzes</h1>
        <p className="mt-1 text-sm text-ink-400">
          Quick, low-pressure practice -- a few minutes a day, built from what you're actually
          getting wrong.
        </p>
      </div>

      <PendingChallengesSection />
      <SentChallengesSection />

      <DailyQuizzesSection />

      <div className="mt-6 rounded-xl2 border border-primary-100 bg-white p-5 shadow-card">
        <div className="flex items-center gap-2">
          <span className="flex h-10 w-10 items-center justify-center rounded-full bg-secondary-50 text-secondary-500">
            <Zap className="h-5 w-5" strokeWidth={2.25} />
          </span>
          <div>
            <p className="text-sm font-bold text-ink-900">Weak Topics Quiz</p>
            <p className="text-xs text-ink-400">Auto-picked from your own answer history</p>
          </div>
        </div>

        {/* ---------- Weak-area preview ---------- */}
        <div className="mt-4">
          {previewStatus === "loading" && (
            <div className="flex items-center gap-2 text-xs text-ink-400">
              <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.25} />
              Checking your weak areas...
            </div>
          )}
          {previewStatus === "error" && (
            <p className="text-xs text-ink-400">Couldn't load your weak areas right now, but you can still start a quiz below.</p>
          )}
          {previewStatus === "success" && !hasWeakSubjects && (
            <p className="rounded-xl bg-primary-50 px-3 py-2.5 text-xs text-ink-600">
              Not enough test history yet to spot a weak topic -- attempt a few Practice Tests or a
              Previous Year Paper first. For now, this'll be a general mixed quiz.
            </p>
          )}
          {hasWeakSubjects && (
            <>
              <p className="flex items-center gap-1 text-xs font-semibold text-ink-600">
                <TrendingUp className="h-3.5 w-3.5" strokeWidth={2.25} />
                Your weak areas right now
              </p>
              <div className="mt-2 flex flex-wrap gap-2">
                {weakSubjects.map((s) => (
                  <span
                    key={s.subject}
                    className="flex items-center gap-1.5 rounded-full border border-secondary-100 bg-secondary-50 px-3 py-1.5 text-xs font-semibold text-secondary-600"
                  >
                    <Target className="h-3 w-3" strokeWidth={2.5} />
                    {s.subject} · {Math.round(s.accuracy)}%
                  </span>
                ))}
              </div>
            </>
          )}
        </div>

        {/* ---------- Question count ---------- */}
        <div className="mt-5">
          <p className="mb-1.5 text-xs font-semibold text-ink-600">Number of questions</p>
          <div className="flex gap-2">
            {COUNT_OPTIONS.map((n) => (
              <button
                key={n}
                type="button"
                onClick={() => setQuestionCount(n)}
                className={`h-9 flex-1 rounded-xl2 border text-sm font-semibold transition-colors ${
                  questionCount === n
                    ? "border-primary-600 bg-primary-600 text-white"
                    : "border-primary-100 bg-white text-ink-600 hover:border-primary-300"
                }`}
              >
                {n}
              </button>
            ))}
          </div>
          <p className="mt-2 flex items-center gap-1 text-xs text-ink-400">
            <Clock className="h-3.5 w-3.5" strokeWidth={2.25} />
            ~{Math.max(5, questionCount)} min · no negative marking
          </p>
        </div>

        {error && (
          <p className="mt-4 rounded-xl2 border border-red-200 bg-red-50 px-3 py-2.5 text-xs font-medium text-red-600">
            {error}
          </p>
        )}

        <button
          type="button"
          onClick={handleStart}
          disabled={starting}
          className="mt-5 flex h-11 w-full items-center justify-center gap-2 rounded-xl2 bg-primary-600 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
        >
          {starting ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.25} /> : <Play className="h-4 w-4" strokeWidth={2.25} />}
          Start Quiz
        </button>
      </div>

      <p className="mt-4 text-center text-xs text-ink-400">
        Looking for a full-length test instead? Try{" "}
        <button type="button" className="font-semibold text-secondary-500 hover:underline" onClick={() => navigate("/tests")}>
          Practice or Mock Tests
        </button>.
      </p>
    </div>
  );
}

// ---------- Daily Quiz (Phase 2, admin-curated -- see QuizzesAdminController) ----------
function DailyQuizzesSection() {
  const navigate = useNavigate();
  const [quizzes, setQuizzes] = useState(null);
  const [status, setStatus] = useState("loading");
  const [startingId, setStartingId] = useState(null);

  useEffect(() => {
    listDailyQuizzes()
      .then((res) => {
        setQuizzes(res);
        setStatus("success");
      })
      .catch(() => setStatus("error"));
  }, []);

  function handleStart(quiz) {
    navigate(`/tests/instructions/quiz-daily/${quiz.id}`);
  }

  // Nothing live right now -- don't show an empty section, the Weak Topics card below is always
  // there regardless.
  if (status !== "loading" && (!quizzes || quizzes.length === 0)) return null;

  return (
    <div className="mt-6">
      <p className="flex items-center gap-1.5 text-sm font-bold text-ink-900">
        <CalendarClock className="h-4 w-4 text-secondary-500" strokeWidth={2.25} />
        Today's Quizzes
      </p>
      <div className="mt-2 flex flex-col gap-2">
        {status === "loading" && (
          <div className="flex items-center gap-2 rounded-xl2 border border-primary-100 bg-white p-4 text-xs text-ink-400">
            <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.25} />
            Checking for live quizzes...
          </div>
        )}
        {quizzes?.map((q) => {
          const attempted = q.myAttemptCount != null && q.myAttemptCount > 0;
          const exhausted = q.maxAttempts != null && q.myAttemptCount != null && q.myAttemptCount >= q.maxAttempts;
          const isUpcoming = q.availabilityStatus === "Upcoming";
          return (
            <div key={q.id} className="flex flex-wrap items-center justify-between gap-3 rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
              <div className="min-w-0">
                <p className="text-sm font-bold text-ink-900">
                  {q.title}
                  {isUpcoming && <span className="ml-2 rounded-full bg-primary-50 px-2 py-0.5 text-[10px] font-bold text-primary-600">Upcoming</span>}
                  {q.language && <span className="ml-2 rounded-full bg-mint-50 px-2 py-0.5 text-[10px] font-bold text-mint-600">{q.language}</span>}
                </p>
                <p className="mt-0.5 text-xs text-ink-400">
                  {q.topic ? `${q.topic} · ` : ""}{q.questionCount} Q · {q.durationMinutes} min
                  {attempted && " · Attempted"}
                </p>
              </div>
              {isUpcoming ? (
                <span className="flex h-9 items-center gap-1.5 rounded-xl bg-primary-50 px-3.5 text-xs font-semibold text-ink-400">
                  <Clock className="h-3.5 w-3.5" strokeWidth={2.25} />
                  Not started yet
                </span>
              ) : exhausted ? (
                <span className="flex h-9 items-center gap-1.5 rounded-xl bg-primary-50 px-3.5 text-xs font-semibold text-ink-400">
                  <Eye className="h-3.5 w-3.5" strokeWidth={2.25} />
                  Already attempted
                </span>
              ) : (
                <button
                  type="button"
                  onClick={() => handleStart(q)}
                  disabled={startingId === q.id}
                  className="flex h-9 items-center gap-1.5 rounded-xl bg-primary-600 px-3.5 text-xs font-semibold text-white hover:bg-primary-700 disabled:opacity-60"
                >
                  {startingId === q.id ? <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.25} /> : <Play className="h-3.5 w-3.5" strokeWidth={2.25} />}
                  {attempted ? "Retry" : "Start"}
                </button>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

// ---------- Challenge a Friend (Phase 3, see QuizChallengesController) ----------
// Only shows challenges someone's SENT you and haven't responded to yet -- a busy inbox of every
// past challenge (completed, declined, sent-by-me) belongs on a fuller history view later, not
// cluttering the main Quizzes page every time a student opens it.
function PendingChallengesSection() {
  const navigate = useNavigate();
  const [challenges, setChallenges] = useState(null);
  const [status, setStatus] = useState("loading");
  const [busyId, setBusyId] = useState(null);

  useEffect(() => {
    getMyQuizChallenges("received", "Pending")
      .then((res) => {
        setChallenges(res);
        setStatus("success");
      })
      .catch(() => setStatus("error"));
  }, []);

  function handleStart(challenge) {
    navigate(`/tests/instructions/quiz-challenge/${challenge.id}`);
  }

  async function handleDecline(challenge) {
    setBusyId(challenge.id);
    try {
      await declineQuizChallenge(challenge.id);
      setChallenges((prev) => prev.filter((c) => c.id !== challenge.id));
    } catch (err) {
      window.alert(err.message || "Couldn't decline this challenge.");
    } finally {
      setBusyId(null);
    }
  }

  if (status !== "success" || !challenges || challenges.length === 0) return null;

  return (
    <div className="mt-6">
      <p className="flex items-center gap-1.5 text-sm font-bold text-ink-900">
        <Swords className="h-4 w-4 text-secondary-500" strokeWidth={2.25} />
        Challenges waiting for you
      </p>
      <div className="mt-2 flex flex-col gap-2">
        {challenges.map((c) => (
          <div key={c.id} className="flex flex-wrap items-center justify-between gap-3 rounded-xl2 border border-secondary-100 bg-secondary-50 p-4">
            <div className="min-w-0">
              <p className="text-sm font-bold text-ink-900">{c.challengerName} challenged you</p>
              <p className="mt-0.5 text-xs text-ink-600">
                {c.quizTitle} · {c.questionCount} Q · beat their score of {c.challengerScore}
              </p>
            </div>
            <div className="flex shrink-0 items-center gap-2">
              <button
                type="button"
                onClick={() => handleDecline(c)}
                disabled={busyId === c.id}
                className="flex h-9 items-center gap-1 rounded-xl border border-primary-100 bg-white px-3 text-xs font-semibold text-ink-500 hover:border-primary-300 disabled:opacity-60"
              >
                <XIcon className="h-3.5 w-3.5" strokeWidth={2.25} />
                Decline
              </button>
              <button
                type="button"
                onClick={() => handleStart(c)}
                disabled={busyId === c.id}
                className="flex h-9 items-center gap-1.5 rounded-xl bg-primary-600 px-3.5 text-xs font-semibold text-white hover:bg-primary-700 disabled:opacity-60"
              >
                {busyId === c.id ? <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.25} /> : <Check className="h-3.5 w-3.5" strokeWidth={2.25} />}
                Accept
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// Shows the challenger's own recent sent challenges with live status/result -- fixes the
// discoverability half of the "who won" gap (the result page itself shows it too, via
// ChallengeComparisonCards on TestAttemptResult.jsx, but a challenger won't necessarily go back to
// that specific old result just to check).
function SentChallengesSection() {
  const [challenges, setChallenges] = useState(null);
  const [status, setStatus] = useState("loading");
  const [showAll, setShowAll] = useState(false);

  useEffect(() => {
    getMyQuizChallenges("sent")
      .then((res) => {
        setChallenges(res);
        setStatus("success");
      })
      .catch(() => setStatus("error"));
  }, []);

  if (status !== "success" || !challenges || challenges.length === 0) return null;

  const visible = showAll ? challenges : challenges.slice(0, 3);

  return (
    <div className="mt-6">
      <p className="flex items-center gap-1.5 text-sm font-bold text-ink-900">
        <Swords className="h-4 w-4 text-secondary-500" strokeWidth={2.25} />
        Challenges you've sent
      </p>
      <div className="mt-2 flex flex-col gap-2">
        {visible.map((c) => {
          const iWon = c.status === "Completed" && c.winner === "Challenger";
          const isTie = c.status === "Completed" && c.winner === "Tie";
          return (
            <div key={c.id} className="flex flex-wrap items-center justify-between gap-3 rounded-xl2 border border-primary-100 bg-white p-3.5 shadow-card">
              <div className="min-w-0">
                <p className="text-sm font-semibold text-ink-900">vs {c.challengedName}</p>
                <p className="mt-0.5 text-xs text-ink-400">{c.quizTitle}</p>
              </div>
              {c.status === "Completed" ? (
                <span className={`shrink-0 rounded-full px-2.5 py-1 text-xs font-bold ${
                  iWon ? "bg-mint-50 text-mint-600" : isTie ? "bg-primary-50 text-ink-500" : "bg-red-50 text-red-500"
                }`}>
                  {c.challengerScore} - {c.challengedScore} {isTie ? "· Tie" : iWon ? "· You won" : "· You lost"}
                </span>
              ) : (
                <span className="shrink-0 rounded-full bg-primary-50 px-2.5 py-1 text-xs font-semibold text-ink-400">{c.status}</span>
              )}
            </div>
          );
        })}
      </div>
      {challenges.length > 3 && !showAll && (
        <button type="button" onClick={() => setShowAll(true)} className="mt-2 text-xs font-semibold text-secondary-500 hover:underline">
          Show all {challenges.length}
        </button>
      )}
    </div>
  );
}
