import { useEffect, useRef, useState } from "react";
import { Clock, Loader2, X } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { getMockTestDetail, submitAttempt } from "../../api/mockTests";

const OPTION_LETTERS = ["A", "B", "C", "D"];

export default function TestAttempt() {
  const { testId } = useParams();
  const navigate = useNavigate();
  const [test, setTest] = useState(null);
  const [status, setStatus] = useState("loading");
  const [answers, setAnswers] = useState({});
  const [currentIndex, setCurrentIndex] = useState(0);
  const [remainingSeconds, setRemainingSeconds] = useState(0);
  const [submitting, setSubmitting] = useState(false);

  const startedAtRef = useRef(Date.now());
  const hasAutoSubmittedRef = useRef(false);
  const handleSubmitRef = useRef();

  useEffect(() => {
    const controller = new AbortController();
    getMockTestDetail(testId, { signal: controller.signal })
      .then((data) => {
        setTest(data);
        setRemainingSeconds(data.durationMinutes * 60);
        setStatus("success");
      })
      .catch((err) => {
        if (err.name !== "AbortError") setStatus("error");
      });
    return () => controller.abort();
  }, [testId]);

  // Countdown timer -- auto-submits once when it reaches zero.
  useEffect(() => {
    if (status !== "success") return undefined;
    const interval = setInterval(() => {
      setRemainingSeconds((s) => {
        if (s <= 1) {
          clearInterval(interval);
          if (!hasAutoSubmittedRef.current) {
            hasAutoSubmittedRef.current = true;
            handleSubmitRef.current?.(true);
          }
          return 0;
        }
        return s - 1;
      });
    }, 1000);
    return () => clearInterval(interval);
  }, [status]);

  async function handleSubmit(isAutoSubmit = false) {
    if (submitting || !test) return;

    if (!isAutoSubmit) {
      const unanswered = test.questions.length - Object.keys(answers).length;
      if (unanswered > 0) {
        const proceed = window.confirm(
          `You have ${unanswered} unanswered question${unanswered === 1 ? "" : "s"}. Submit anyway?`
        );
        if (!proceed) return;
      }
    }

    setSubmitting(true);
    const timeTakenSeconds = Math.round((Date.now() - startedAtRef.current) / 1000);
    const payload = {
      answers: test.questions.map((q) => ({
        questionId: q.questionId,
        selectedOption: answers[q.questionId] ?? null,
      })),
      timeTakenSeconds,
    };

    try {
      const result = await submitAttempt(testId, payload);
      navigate(`/tests/results/${result.attemptId}`, { replace: true });
    } catch (err) {
      setSubmitting(false);
      window.alert(err.message || "Couldn't submit your attempt. Please try again.");
    }
  }

  handleSubmitRef.current = handleSubmit;

  if (status === "loading") {
    return (
      <div className="flex min-h-[60vh] items-center justify-center text-ink-400">
        <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
      </div>
    );
  }

  if (status === "error" || !test) {
    return (
      <div className="flex min-h-[60vh] flex-col items-center justify-center gap-3 px-6 text-center">
        <p className="text-sm text-red-600">Couldn't load this test. Please try again.</p>
        <button type="button" onClick={() => navigate("/tests")} className="text-sm font-semibold text-secondary-500">
          Back to Tests
        </button>
      </div>
    );
  }

  const question = test.questions[currentIndex];
  const answeredCount = Object.keys(answers).length;
  const minutes = Math.floor(remainingSeconds / 60);
  const seconds = remainingSeconds % 60;
  const isLastQuestion = currentIndex === test.questions.length - 1;
  const isLowTime = remainingSeconds <= 60;

  return (
    <div className="flex min-h-screen flex-col">
      <div className="sticky top-0 z-20 flex items-center justify-between gap-3 border-b border-primary-100 bg-white px-4 py-3 sm:px-6 lg:px-8">
        <div className="min-w-0">
          <p className="truncate text-sm font-bold text-ink-900">{test.title}</p>
          <p className="text-xs text-ink-400">
            Question {currentIndex + 1} of {test.questions.length}
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-3">
          <span
            className={`flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-sm font-bold ${
              isLowTime ? "bg-red-50 text-red-600" : "bg-primary-50 text-primary-600"
            }`}
          >
            <Clock className="h-4 w-4" strokeWidth={2.25} />
            {String(minutes).padStart(2, "0")}:{String(seconds).padStart(2, "0")}
          </span>
          <button
            type="button"
            onClick={() => {
              if (window.confirm("Exit this test? Your progress won't be saved.")) navigate("/tests");
            }}
            aria-label="Exit test"
            className="flex h-8 w-8 items-center justify-center rounded-full text-ink-400 transition-colors hover:bg-primary-50"
          >
            <X className="h-5 w-5" strokeWidth={2.25} />
          </button>
        </div>
      </div>

      <div className="flex flex-1 flex-col gap-4 px-4 py-4 sm:px-6 lg:flex-row lg:px-8">
        <div className="flex-1">
          <div className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card sm:p-5">
            <p className="text-[15px] font-semibold leading-snug text-ink-900 sm:text-base">
              {question.questionText}
            </p>

            <div className="mt-4 flex flex-col gap-2">
              {OPTION_LETTERS.map((letter) => {
                const text = question[`option${letter}`];
                const selected = answers[question.questionId] === letter;
                return (
                  <button
                    key={letter}
                    type="button"
                    onClick={() => setAnswers((prev) => ({ ...prev, [question.questionId]: letter }))}
                    className={`flex items-start gap-3 rounded-xl border p-3 text-left text-sm transition-colors ${
                      selected
                        ? "border-secondary-500 bg-secondary-50 text-secondary-700"
                        : "border-primary-100 bg-white text-ink-900 hover:bg-primary-50"
                    }`}
                  >
                    <span
                      className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs font-bold ${
                        selected ? "bg-secondary-500 text-white" : "bg-primary-50 text-ink-600"
                      }`}
                    >
                      {letter}
                    </span>
                    {text}
                  </button>
                );
              })}
            </div>

            {answers[question.questionId] && (
              <button
                type="button"
                onClick={() =>
                  setAnswers((prev) => {
                    const next = { ...prev };
                    delete next[question.questionId];
                    return next;
                  })
                }
                className="mt-3 text-xs font-semibold text-ink-400 transition-colors hover:text-red-600"
              >
                Clear response
              </button>
            )}
          </div>

          <div className="mt-4 flex items-center justify-between gap-3">
            <button
              type="button"
              disabled={currentIndex === 0}
              onClick={() => setCurrentIndex((i) => Math.max(0, i - 1))}
              className="rounded-xl border border-primary-100 bg-white px-4 py-2.5 text-sm font-semibold text-ink-600 transition-colors hover:bg-primary-50 disabled:opacity-40"
            >
              Previous
            </button>

            {isLastQuestion ? (
              <button
                type="button"
                onClick={() => handleSubmit(false)}
                disabled={submitting}
                className="flex items-center gap-1.5 rounded-xl bg-primary-600 px-5 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
              >
                {submitting && <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />}
                Submit Test
              </button>
            ) : (
              <button
                type="button"
                onClick={() => setCurrentIndex((i) => Math.min(test.questions.length - 1, i + 1))}
                className="rounded-xl bg-primary-600 px-5 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-primary-700"
              >
                Next
              </button>
            )}
          </div>
        </div>

        <div className="lg:w-64 lg:shrink-0">
          <div className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
            <p className="mb-3 text-xs font-bold text-ink-600">Question Palette</p>
            <div className="grid grid-cols-6 gap-2 lg:grid-cols-5">
              {test.questions.map((q, i) => {
                const isAnswered = Boolean(answers[q.questionId]);
                const isCurrent = i === currentIndex;
                return (
                  <button
                    key={q.questionId}
                    type="button"
                    onClick={() => setCurrentIndex(i)}
                    aria-label={`Go to question ${i + 1}${isAnswered ? " (answered)" : ""}`}
                    className={`flex h-9 w-9 items-center justify-center rounded-lg text-xs font-bold transition-colors ${
                      isCurrent
                        ? "bg-primary-600 text-white"
                        : isAnswered
                        ? "bg-mint-50 text-mint-500"
                        : "bg-primary-50 text-ink-400"
                    }`}
                  >
                    {i + 1}
                  </button>
                );
              })}
            </div>

            <div className="mt-4 flex items-center justify-between border-t border-primary-50 pt-3 text-xs text-ink-400">
              <span>{answeredCount} answered</span>
              <span>{test.questions.length - answeredCount} left</span>
            </div>

            <button
              type="button"
              onClick={() => handleSubmit(false)}
              disabled={submitting}
              className="mt-3 hidden w-full items-center justify-center gap-1.5 rounded-xl bg-primary-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60 lg:flex"
            >
              {submitting && <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />}
              Submit Test
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
