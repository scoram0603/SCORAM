import { useEffect, useState } from "react";
import { ChevronRight, CheckCircle2, Clock, Target, Loader2, LogIn } from "lucide-react";
import { Link } from "react-router-dom";
import { getMyTestAttempts } from "../../api/testAttempts";
import { useAuth } from "../../context/AuthContext";
import { formatDuration } from "../../utils/format";

// Live — wired to GET /api/tests/attempts/mine (TestAttemptsController), the shared
// Practice + Mock Test attempt history (SCORAM_TESTS). Requires login (it's per-student
// history), so guests see a Log In prompt instead of an error. Only "Submitted" attempts
// are requested -- an in-progress one has no score yet and belongs under Resume, not here.
export default function RecentTests() {
  const { isAuthenticated } = useAuth();
  const [items, setItems] = useState([]);
  const [status, setStatus] = useState(isAuthenticated ? "loading" : "guest");

  useEffect(() => {
    if (!isAuthenticated) {
      setStatus("guest");
      return;
    }
    const controller = new AbortController();
    setStatus("loading");
    getMyTestAttempts({ status: "Submitted", page: 1, pageSize: 3 }, { signal: controller.signal })
      .then((res) => {
        setItems(res.items);
        setStatus("success");
      })
      .catch((err) => {
        if (err.name !== "AbortError") setStatus("error");
      });
    return () => controller.abort();
  }, [isAuthenticated]);

  return (
    <section className="px-4 pb-6 sm:px-6 lg:px-0">
      <div className="mb-3 flex items-center justify-between sm:mb-4">
        <h3 className="text-[17px] font-bold text-ink-900 sm:text-lg">Recent Tests</h3>
        <Link to="/tests" className="flex items-center gap-0.5 text-sm font-semibold text-secondary-500">
          View All
          <ChevronRight className="h-4 w-4" strokeWidth={2.5} />
        </Link>
      </div>

      {status === "guest" && (
        <div className="flex flex-col items-center gap-2 rounded-xl2 border border-primary-100 bg-white p-6 text-center">
          <p className="text-sm text-ink-400">Log in to see your test history.</p>
          <Link
            to="/login?redirect=/"
            className="flex items-center gap-1.5 rounded-lg bg-primary-600 px-3 py-2 text-xs font-semibold text-white transition-colors hover:bg-primary-700"
          >
            <LogIn className="h-3.5 w-3.5" strokeWidth={2.25} />
            Log In
          </Link>
        </div>
      )}

      {status === "loading" && (
        <div className="flex justify-center py-8 text-ink-400">
          <Loader2 className="h-5 w-5 animate-spin" strokeWidth={2.25} />
        </div>
      )}

      {status === "error" && (
        <p className="rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-600">
          Couldn't load your test history right now.
        </p>
      )}

      {status === "success" && items.length === 0 && (
        <p className="rounded-xl2 border border-primary-100 bg-white p-4 text-sm text-ink-400">
          No attempts yet — take your first Practice or Mock Test to see it here.
        </p>
      )}

      {status === "success" && items.length > 0 && (
        <div className="flex flex-col gap-2.5 sm:gap-3">
          {items.map((test) => (
            <Link
              key={test.attemptId}
              to={`/tests/result/${test.attemptId}`}
              className="block rounded-xl2 border border-primary-100 bg-white p-3.5 shadow-card transition-shadow hover:shadow-cardHover sm:p-4"
            >
              <div className="flex items-start justify-between gap-2">
                <span className="text-sm font-bold leading-snug text-ink-900">{test.title}</span>
                <span className="flex shrink-0 items-center gap-1 rounded-md bg-mint-50 px-1.5 py-0.5 text-[10px] font-bold text-mint-500">
                  <CheckCircle2 className="h-3 w-3" strokeWidth={2.5} />
                  Completed
                </span>
              </div>

              <div className="mt-3 flex items-center justify-between">
                <span className="text-lg font-extrabold text-primary-600">
                  {test.percentageScore ?? 0}
                  <span className="text-sm font-medium text-ink-400">%</span>
                </span>

                <span className="flex items-center gap-3 text-xs text-ink-400">
                  <span className="flex items-center gap-1">
                    <Target className="h-3.5 w-3.5" strokeWidth={2} />
                    {test.accuracyPercent}% Accuracy
                  </span>
                  <span className="flex items-center gap-1">
                    <Clock className="h-3.5 w-3.5" strokeWidth={2} />
                    {formatDuration(test.timeTakenSeconds)}
                  </span>
                </span>
              </div>
            </Link>
          ))}
        </div>
      )}
    </section>
  );
}