import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Zap, Target, Loader2, Play, Clock, TrendingUp, CalendarClock, Eye } from "lucide-react";
import { previewWeakTopics, generateWeakTopicsQuiz, listDailyQuizzes, startDailyQuiz } from "../api/quizzes";

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

  async function handleStart() {
    setError("");
    setStarting(true);
    try {
      const attempt = await generateWeakTopicsQuiz(questionCount);
      navigate(`/tests/attempt/${attempt.attemptId}`);
    } catch (err) {
      setError(err.message || "Couldn't start the quiz right now.");
      setStarting(false);
    }
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

  async function handleStart(quiz) {
    setStartingId(quiz.id);
    try {
      const attempt = await startDailyQuiz(quiz.id);
      navigate(`/tests/attempt/${attempt.attemptId}`);
    } catch (err) {
      window.alert(err.message || "Couldn't start this quiz right now.");
      setStartingId(null);
    }
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
