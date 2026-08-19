import { useEffect, useState } from "react";
import { Clock, ClipboardCheck, ChevronRight, Loader2, LogIn } from "lucide-react";
import { Link, useNavigate } from "react-router-dom";
import { listMockTests, getMyAttempts } from "../../api/mockTests";
import { useAuth } from "../../context/AuthContext";
import { formatDuration } from "../../utils/format";

export default function TestsList() {
  const { isAuthenticated } = useAuth();
  const navigate = useNavigate();

  const [tests, setTests] = useState([]);
  const [testsStatus, setTestsStatus] = useState("loading");

  const [attempts, setAttempts] = useState([]);
  const [attemptsStatus, setAttemptsStatus] = useState(isAuthenticated ? "loading" : "guest");

  useEffect(() => {
    const controller = new AbortController();
    listMockTests({ page: 1, pageSize: 20 }, { signal: controller.signal })
      .then((res) => {
        setTests(res.items);
        setTestsStatus("success");
      })
      .catch((err) => {
        if (err.name !== "AbortError") setTestsStatus("error");
      });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (!isAuthenticated) {
      setAttemptsStatus("guest");
      return;
    }
    const controller = new AbortController();
    setAttemptsStatus("loading");
    getMyAttempts({ page: 1, pageSize: 10 }, { signal: controller.signal })
      .then((res) => {
        setAttempts(res.items);
        setAttemptsStatus("success");
      })
      .catch((err) => {
        if (err.name !== "AbortError") setAttemptsStatus("error");
      });
    return () => controller.abort();
  }, [isAuthenticated]);

  return (
    <div className="px-4 pb-8 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <h1 className="text-xl font-extrabold text-ink-900 sm:text-2xl">Test</h1>
      <p className="mt-1 text-sm text-ink-400">Timed practice tests, live from the Scoram question bank.</p>

      <h2 className="mt-6 text-[15px] font-bold text-ink-900">Available Tests</h2>
      {testsStatus === "loading" && <LoadingRow />}
      {testsStatus === "error" && <ErrorRow message="Couldn't load tests right now." />}
      {testsStatus === "success" && tests.length === 0 && (
        <EmptyRow message="No tests available yet — check back soon." />
      )}
      {testsStatus === "success" && tests.length > 0 && (
        <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {tests.map((t) => (
            // Guests can click straight in -- ProtectedRoute on /tests/:testId/attempt
            // bounces them to /login?redirect=... and drops them right back here after.
            <button
              key={t.id}
              type="button"
              onClick={() => navigate(`/tests/${t.id}/attempt`)}
              className="flex flex-col items-start gap-2 rounded-xl2 border border-primary-100 bg-white p-4 text-left shadow-card transition-all hover:-translate-y-0.5 hover:shadow-cardHover"
            >
              <span className="flex h-11 w-11 items-center justify-center rounded-2xl bg-accent-50 text-accent-500">
                <ClipboardCheck className="h-5 w-5" strokeWidth={2} />
              </span>
              <span className="text-sm font-bold text-ink-900">{t.title}</span>
              <span className="text-xs text-ink-400">
                {t.examName} · {t.questionCount} Questions
              </span>
              <span className="flex items-center gap-1 text-xs text-ink-400">
                <Clock className="h-3.5 w-3.5" strokeWidth={2} />
                {t.durationMinutes} min · -{t.negativeMarkingRatio} negative marking
              </span>
            </button>
          ))}
        </div>
      )}

      <h2 className="mt-8 text-[15px] font-bold text-ink-900">My Recent Attempts</h2>
      {attemptsStatus === "guest" && (
        <div className="mt-3 flex flex-col items-center gap-2 rounded-xl2 border border-primary-100 bg-white p-6 text-center">
          <p className="text-sm text-ink-400">Log in to see your test history.</p>
          <Link
            to="/login?redirect=/tests"
            className="flex items-center gap-1.5 rounded-lg bg-primary-600 px-3 py-2 text-xs font-semibold text-white transition-colors hover:bg-primary-700"
          >
            <LogIn className="h-3.5 w-3.5" strokeWidth={2.25} />
            Log In
          </Link>
        </div>
      )}
      {attemptsStatus === "loading" && <LoadingRow />}
      {attemptsStatus === "error" && <ErrorRow message="Couldn't load your attempts right now." />}
      {attemptsStatus === "success" && attempts.length === 0 && (
        <EmptyRow message="No attempts yet — take a test above to see it here." />
      )}
      {attemptsStatus === "success" && attempts.length > 0 && (
        <ul className="mt-3 flex flex-col gap-2.5">
          {attempts.map((a) => (
            <li key={a.attemptId}>
              <Link
                to={`/tests/results/${a.attemptId}`}
                className="flex w-full items-center justify-between gap-3 rounded-xl2 border border-primary-100 bg-white p-3.5 text-left shadow-card transition-colors hover:bg-primary-50/40"
              >
                <span className="min-w-0">
                  <span className="block truncate text-sm font-bold text-ink-900">{a.mockTestTitle}</span>
                  <span className="block text-xs text-ink-400">
                    {a.examName} · {formatDuration(a.timeTakenSeconds)}
                  </span>
                </span>
                <span className="flex shrink-0 items-center gap-2">
                  <span className="text-sm font-extrabold text-primary-600">
                    {a.score}/{a.maxPossibleScore}
                  </span>
                  <ChevronRight className="h-4 w-4 text-ink-400" strokeWidth={2} />
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function LoadingRow() {
  return (
    <div className="mt-3 flex justify-center py-6 text-ink-400">
      <Loader2 className="h-5 w-5 animate-spin" strokeWidth={2.25} />
    </div>
  );
}
function ErrorRow({ message }) {
  return <p className="mt-3 rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-600">{message}</p>;
}
function EmptyRow({ message }) {
  return <p className="mt-3 rounded-xl2 border border-primary-100 bg-white p-4 text-sm text-ink-400">{message}</p>;
}
