import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { Loader2, Clock, Flag, ChevronLeft, ChevronRight, Menu, X, AlertTriangle } from "lucide-react";
import { getAttempt, saveAnswer, submitTestAttempt } from "../../api/testAttempts";
import { API_BASE_URL } from "../../api/client";
import { MathText, RichQuestionBody } from "../questions/MathText";

function imgSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

const OPTION_LETTERS = ["A", "B", "C", "D"];
const SAVE_DEBOUNCE_MS = 500;

// A response from GET /api/tests/attempts/{id} is either "still in progress" (has expiresAt) or
// "already graded" (has score/questions with a correctOption) -- this is how the shared endpoint
// tells the two apart, see Controllers/TestAttemptsController.GetAttempt.
function isInProgressShape(data) {
  return data && typeof data.expiresAt === "string";
}

// Route: /tests/attempt/:attemptId -- reached after starting/generating/resuming either kind of
// test. Practice and Mock Tests are indistinguishable from here on (SCORAM_TESTS' shared attempt
// backbone) -- this component doesn't need to know or care which one it's running.
export default function TestRunner() {
  const { attemptId } = useParams();
  const navigate = useNavigate();

  const [status, setStatus] = useState("loading"); // loading | running | error
  const [meta, setMeta] = useState(null);
  const [questions, setQuestions] = useState([]);
  const [currentIndex, setCurrentIndex] = useState(0);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [showInstructions, setShowInstructions] = useState(true);
  const [remainingSeconds, setRemainingSeconds] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [confirmSubmit, setConfirmSubmit] = useState(false);

  const startedAtRef = useRef(null);
  const saveTimersRef = useRef({});
  const submittedRef = useRef(false);

  useEffect(() => {
    let cancelled = false;
    getAttempt(attemptId)
      .then((data) => {
        if (cancelled) return;
        if (!isInProgressShape(data)) {
          navigate(`/tests/result/${attemptId}`, { replace: true });
          return;
        }
        setMeta({
          title: data.title,
          testKind: data.testKind,
          durationMinutes: data.durationMinutes,
          negativeMarkingRatio: data.negativeMarkingRatio,
          expiresAt: data.expiresAt,
          instructions: data.instructions,
        });
        setQuestions(data.questions);
        startedAtRef.current = new Date(data.startedAt).getTime();
        setStatus("running");
      })
      .catch(() => {
        if (!cancelled) setStatus("error");
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [attemptId]);

  useEffect(() => {
    if (status !== "running" || !meta) return;
    const expiresAt = new Date(meta.expiresAt).getTime();

    function tick() {
      const secondsLeft = Math.max(0, Math.round((expiresAt - Date.now()) / 1000));
      setRemainingSeconds(secondsLeft);
      if (secondsLeft <= 0) handleSubmit(true);
    }
    tick();
    const interval = setInterval(tick, 1000);
    return () => clearInterval(interval);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status, meta]);

  useEffect(() => {
    if (status !== "running") return;
    function handleBeforeUnload(e) {
      e.preventDefault();
      e.returnValue = "";
    }
    window.addEventListener("beforeunload", handleBeforeUnload);
    return () => window.removeEventListener("beforeunload", handleBeforeUnload);
  }, [status]);

  const current = questions[currentIndex];

  const counts = useMemo(() => {
    let answered = 0, markedForReview = 0, answeredAndMarked = 0, notVisited = 0;
    questions.forEach((q) => {
      if (q.selectedOption && q.isMarkedForReview) answeredAndMarked++;
      else if (q.selectedOption) answered++;
      else if (q.isMarkedForReview) markedForReview++;
      else notVisited++;
    });
    return { answered, markedForReview, answeredAndMarked, notVisited, notAnswered: questions.length - answered - answeredAndMarked };
  }, [questions]);

  const updateCurrentQuestion = useCallback((patch) => {
    setQuestions((prev) => prev.map((q, i) => (i === currentIndex ? { ...q, ...patch } : q)));
  }, [currentIndex]);

  const queueSave = useCallback((studentAnswerId, payload) => {
    if (saveTimersRef.current[studentAnswerId]) clearTimeout(saveTimersRef.current[studentAnswerId]);
    saveTimersRef.current[studentAnswerId] = setTimeout(() => {
      saveAnswer(studentAnswerId, payload).catch(() => {});
    }, SAVE_DEBOUNCE_MS);
  }, []);

  function handleSelectOption(letter) {
    updateCurrentQuestion({ selectedOption: letter });
    queueSave(current.id, { selectedOption: letter, isMarkedForReview: current.isMarkedForReview });
  }

  function handleClearResponse() {
    updateCurrentQuestion({ selectedOption: null });
    queueSave(current.id, { selectedOption: null, isMarkedForReview: current.isMarkedForReview });
  }

  function handleToggleMarkForReview() {
    const next = !current.isMarkedForReview;
    updateCurrentQuestion({ isMarkedForReview: next });
    queueSave(current.id, { selectedOption: current.selectedOption, isMarkedForReview: next });
  }

  function goTo(index) {
    setCurrentIndex(Math.max(0, Math.min(questions.length - 1, index)));
    setPaletteOpen(false);
  }

  async function handleSubmit(isAutoSubmit = false) {
    if (submittedRef.current) return;
    if (!isAutoSubmit && !confirmSubmit) {
      setConfirmSubmit(true);
      return;
    }
    submittedRef.current = true;
    setSubmitting(true);

    Object.values(saveTimersRef.current).forEach(clearTimeout);

    const timeTakenSeconds = Math.round((Date.now() - startedAtRef.current) / 1000);
    try {
      await submitTestAttempt(attemptId, timeTakenSeconds);
      navigate(`/tests/result/${attemptId}`, { replace: true });
    } catch (err) {
      submittedRef.current = false;
      setSubmitting(false);
      window.alert(err.message || "Couldn't submit right now. Please check your connection and try again.");
    }
  }

  if (status === "loading") {
    return (
      <div className="flex min-h-[60vh] items-center justify-center text-ink-400">
        <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
      </div>
    );
  }

  if (status === "error") {
    return (
      <div className="flex min-h-[60vh] flex-col items-center justify-center gap-2 px-6 text-center">
        <AlertTriangle className="h-7 w-7 text-red-500" strokeWidth={2} />
        <p className="text-sm text-ink-600">Couldn't load this test. It may have expired or already been submitted.</p>
        <button type="button" onClick={() => navigate("/tests")} className="mt-2 text-sm font-semibold text-secondary-500">
          Back to Tests
        </button>
      </div>
    );
  }

  if (showInstructions) {
    return <InstructionsScreen meta={meta} questionCount={questions.length} onStart={() => setShowInstructions(false)} />;
  }

  return (
    <div className="flex min-h-screen flex-col bg-surface">
      <header className="sticky top-0 z-20 flex items-center justify-between border-b border-primary-100 bg-white px-4 py-3">
        <div className="min-w-0">
          <p className="truncate text-sm font-bold text-ink-900">{meta.title}</p>
          <p className="text-xs text-ink-400">Question {currentIndex + 1} of {questions.length}</p>
        </div>
        <div className="flex items-center gap-3">
          <span className={`flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-sm font-bold tabular-nums ${remainingSeconds < 60 ? "bg-red-50 text-red-600" : "bg-primary-50 text-primary-600"}`}>
            <Clock className="h-4 w-4" strokeWidth={2.25} />
            {formatTime(remainingSeconds)}
          </span>
          <button type="button" onClick={() => setPaletteOpen(true)} className="flex h-9 w-9 items-center justify-center rounded-lg text-ink-600 lg:hidden">
            <Menu className="h-5 w-5" strokeWidth={2.25} />
          </button>
        </div>
      </header>

      <div className="flex flex-1">
        <main className="flex-1 px-4 py-5 sm:px-6">
          {current && (
            <>
              <div className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card sm:p-5">
                <p className="text-[15px] font-semibold leading-snug text-ink-900">
                  <RichQuestionBody contentBlocks={current.contentBlocks} fallbackText={current.questionText} />
                </p>
                {imgSrc(current.questionImageUrl) && (
                  <img src={imgSrc(current.questionImageUrl)} alt="" className="mt-3 max-h-64 rounded-lg border border-primary-100" />
                )}

                <div className="mt-4 flex flex-col gap-2">
                  {OPTION_LETTERS.map((letter) => (
                    <button
                      key={letter}
                      type="button"
                      onClick={() => handleSelectOption(letter)}
                      className={`flex items-start gap-2 rounded-lg border px-3.5 py-2.5 text-left text-sm transition-colors ${
                        current.selectedOption === letter
                          ? "border-primary-600 bg-primary-50 text-primary-700"
                          : "border-primary-100 text-ink-600 hover:bg-primary-50/60"
                      }`}
                    >
                      <span className="font-bold">{letter}.</span>
                      <span className="min-w-0 flex-1">
                        <MathText text={current[`option${letter}`]} />
                        {imgSrc(current[`option${letter}ImageUrl`]) && (
                          <img src={imgSrc(current[`option${letter}ImageUrl`])} alt="" className="mt-1.5 max-h-32 rounded border border-primary-100" />
                        )}
                      </span>
                    </button>
                  ))}
                </div>
              </div>

              <div className="mt-4 flex flex-wrap items-center gap-2">
                <button
                  type="button"
                  onClick={handleToggleMarkForReview}
                  className={`flex items-center gap-1.5 rounded-xl2 border px-3.5 py-2 text-xs font-semibold transition-colors ${
                    current.isMarkedForReview ? "border-accent-300 bg-accent-50 text-accent-600" : "border-primary-100 text-ink-600"
                  }`}
                >
                  <Flag className="h-3.5 w-3.5" strokeWidth={2.25} />
                  {current.isMarkedForReview ? "Marked for Review" : "Mark for Review"}
                </button>
                <button
                  type="button"
                  onClick={handleClearResponse}
                  disabled={!current.selectedOption}
                  className="rounded-xl2 border border-primary-100 px-3.5 py-2 text-xs font-semibold text-ink-600 disabled:opacity-40"
                >
                  Clear Response
                </button>
              </div>

              <div className="mt-6 flex items-center justify-between gap-3">
                <button
                  type="button"
                  onClick={() => goTo(currentIndex - 1)}
                  disabled={currentIndex === 0}
                  className="flex items-center gap-1 rounded-xl2 border border-primary-100 px-4 py-2.5 text-sm font-semibold text-ink-600 disabled:opacity-40"
                >
                  <ChevronLeft className="h-4 w-4" strokeWidth={2.5} />
                  Previous
                </button>

                {currentIndex < questions.length - 1 ? (
                  <button
                    type="button"
                    onClick={() => goTo(currentIndex + 1)}
                    className="flex items-center gap-1 rounded-xl2 bg-primary-600 px-4 py-2.5 text-sm font-semibold text-white"
                  >
                    Next
                    <ChevronRight className="h-4 w-4" strokeWidth={2.5} />
                  </button>
                ) : (
                  <button type="button" onClick={() => handleSubmit(false)} className="rounded-xl2 bg-accent-500 px-5 py-2.5 text-sm font-semibold text-white">
                    Submit Test
                  </button>
                )}
              </div>
            </>
          )}
        </main>

        <aside className="hidden w-72 shrink-0 border-l border-primary-100 bg-white p-4 lg:block">
          <QuestionPalette questions={questions} currentIndex={currentIndex} onSelect={goTo} counts={counts} onSubmit={() => handleSubmit(false)} />
        </aside>

        {paletteOpen && (
          <div className="fixed inset-0 z-30 flex justify-end bg-ink-900/40 lg:hidden" onClick={() => setPaletteOpen(false)}>
            <div className="h-full w-72 bg-white p-4" onClick={(e) => e.stopPropagation()}>
              <div className="mb-2 flex items-center justify-between">
                <span className="text-sm font-bold text-ink-900">Questions</span>
                <button type="button" onClick={() => setPaletteOpen(false)}>
                  <X className="h-5 w-5 text-ink-400" strokeWidth={2.25} />
                </button>
              </div>
              <QuestionPalette questions={questions} currentIndex={currentIndex} onSelect={goTo} counts={counts} onSubmit={() => handleSubmit(false)} />
            </div>
          </div>
        )}
      </div>

      {confirmSubmit && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-ink-900/40 px-6">
          <div className="w-full max-w-sm rounded-xl2 bg-white p-5 shadow-xl">
            <h3 className="text-sm font-bold text-ink-900">Submit the test?</h3>
            <p className="mt-1.5 text-sm text-ink-600">
              {counts.notVisited + counts.markedForReview > 0
                ? `You still have ${counts.notVisited + counts.markedForReview} question(s) unanswered or marked for review.`
                : "You've answered every question."}{" "}
              You won't be able to change your answers after this.
            </p>
            <div className="mt-4 flex gap-2">
              <button type="button" onClick={() => setConfirmSubmit(false)} className="flex-1 rounded-xl2 border border-primary-100 py-2.5 text-sm font-semibold text-ink-600">
                Keep working
              </button>
              <button
                type="button"
                onClick={() => handleSubmit(false)}
                disabled={submitting}
                className="flex flex-1 items-center justify-center gap-1.5 rounded-xl2 bg-accent-500 py-2.5 text-sm font-semibold text-white disabled:opacity-60"
              >
                {submitting && <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />}
                Submit
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function InstructionsScreen({ meta, questionCount, onStart }) {
  const label = meta.testKind === "Mock" ? "Mock Test" : meta.testKind === "PreviousYearPaper" ? "Previous Year Paper" : "Practice Test";
  return (
    <div className="mx-auto flex min-h-[80vh] max-w-lg flex-col justify-center px-6 py-10">
      <p className="text-xs font-bold uppercase tracking-wide text-secondary-500">{label}</p>
      <h1 className="mt-1 text-xl font-extrabold text-ink-900">{meta.title}</h1>

      <div className="mt-5 grid grid-cols-2 gap-3 text-sm">
        <Stat label="Questions" value={questionCount} />
        <Stat label="Duration" value={`${meta.durationMinutes} min`} />
        <Stat label="Marks / Question" value="1" />
        <Stat label="Negative Marking" value={meta.negativeMarkingRatio > 0 ? `-${meta.negativeMarkingRatio}` : "None"} />
      </div>

      {meta.instructions && (
        <div className="mt-5 rounded-xl2 border border-primary-100 bg-white p-4 text-sm leading-relaxed text-ink-600">
          {meta.instructions}
        </div>
      )}

      <button type="button" onClick={onStart} className="mt-6 rounded-xl2 bg-primary-600 py-3 text-sm font-bold text-white transition-colors hover:bg-primary-700">
        Start {label}
      </button>
    </div>
  );
}

function Stat({ label, value }) {
  return (
    <div className="rounded-xl2 border border-primary-100 bg-white p-3">
      <p className="text-base font-extrabold text-ink-900">{value}</p>
      <p className="text-[11px] font-medium text-ink-400">{label}</p>
    </div>
  );
}

function QuestionPalette({ questions, currentIndex, onSelect, counts, onSubmit }) {
  return (
    <div className="flex h-full flex-col">
      <div className="grid grid-cols-2 gap-1.5 text-[11px]">
        <Legend swatch="bg-mint-500" label={`Answered (${counts.answered + counts.answeredAndMarked})`} />
        <Legend swatch="bg-primary-100" label={`Not answered (${counts.notAnswered - counts.markedForReview})`} />
        <Legend swatch="bg-accent-500" label={`Marked (${counts.markedForReview})`} />
        <Legend swatch="bg-secondary-500" label={`Answered + Marked (${counts.answeredAndMarked})`} />
      </div>

      <div className="mt-4 grid grid-cols-5 gap-2 overflow-y-auto">
        {questions.map((q, i) => {
          const isCurrent = i === currentIndex;
          const swatch = q.selectedOption && q.isMarkedForReview
            ? "bg-secondary-500 text-white"
            : q.isMarkedForReview
            ? "bg-accent-500 text-white"
            : q.selectedOption
            ? "bg-mint-500 text-white"
            : "bg-primary-50 text-ink-600";
          return (
            <button
              key={q.id}
              type="button"
              onClick={() => onSelect(i)}
              className={`flex h-9 w-9 items-center justify-center rounded-lg text-xs font-bold transition-transform ${swatch} ${isCurrent ? "ring-2 ring-primary-600 ring-offset-1" : ""}`}
            >
              {i + 1}
            </button>
          );
        })}
      </div>

      <button type="button" onClick={onSubmit} className="mt-4 rounded-xl2 bg-accent-500 py-2.5 text-sm font-semibold text-white">
        Submit Test
      </button>
    </div>
  );
}

function Legend({ swatch, label }) {
  return (
    <span className="flex items-center gap-1.5 text-ink-600">
      <span className={`h-2.5 w-2.5 shrink-0 rounded-sm ${swatch}`} />
      {label}
    </span>
  );
}

function formatTime(totalSeconds) {
  const h = Math.floor(totalSeconds / 3600);
  const m = Math.floor((totalSeconds % 3600) / 60);
  const s = totalSeconds % 60;
  const pad = (n) => String(n).padStart(2, "0");
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`;
}
