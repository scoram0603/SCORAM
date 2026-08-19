import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { getAttemptDetail } from "../api/mockTests";
import TestResultView from "../components/tests/TestResultView";

export default function TestResultPage() {
  const { attemptId } = useParams();
  const navigate = useNavigate();
  const [result, setResult] = useState(null);
  const [status, setStatus] = useState("loading");

  useEffect(() => {
    let cancelled = false;
    setStatus("loading");
    getAttemptDetail(attemptId)
      .then((data) => {
        if (cancelled) return;
        setResult(data);
        setStatus("success");
      })
      .catch(() => {
        if (!cancelled) setStatus("error");
      });
    return () => {
      cancelled = true;
    };
  }, [attemptId]);

  if (status === "loading") {
    return (
      <div className="flex min-h-[60vh] items-center justify-center text-ink-400">
        <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
      </div>
    );
  }

  if (status === "error" || !result) {
    return (
      <div className="flex min-h-[60vh] flex-col items-center justify-center gap-3 px-6 text-center">
        <p className="text-sm text-red-600">Couldn't load this result. Please try again.</p>
        <button type="button" onClick={() => navigate("/tests")} className="text-sm font-semibold text-secondary-500">
          Back to Tests
        </button>
      </div>
    );
  }

  return (
    <TestResultView
      result={result}
      onBack={() => navigate("/tests")}
      onViewQuestion={(questionId) => navigate(`/questions/${questionId}`)}
    />
  );
}
