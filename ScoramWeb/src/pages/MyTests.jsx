import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, Loader2, Play, Eye, PenLine, Trophy, BookOpen } from "lucide-react";
import { getMyTestAttempts } from "../api/testAttempts";
import { timeAgo } from "../utils/format";

const TABS = [
  { key: "", label: "All" },
  { key: "InProgress", label: "In Progress" },
  { key: "Submitted", label: "Completed" },
];

export default function MyTests() {
  const navigate = useNavigate();
  const [tab, setTab] = useState("");
  const [attempts, setAttempts] = useState(null);
  const [status, setStatus] = useState("loading");

  useEffect(() => {
    setStatus("loading");
    getMyTestAttempts({ status: tab, page: 1, pageSize: 50 })
      .then((res) => {
        setAttempts(res.items);
        setStatus("success");
      })
      .catch(() => setStatus("error"));
  }, [tab]);

  function handleOpen(a) {
    if (a.canResume) navigate(`/tests/attempt/${a.attemptId}`);
    else navigate(`/tests/result/${a.attemptId}`);
  }

  return (
    <div className="px-4 pb-10 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <button type="button" onClick={() => navigate("/tests")} className="flex items-center gap-1.5 text-sm font-semibold text-secondary-500">
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
        Tests
      </button>

      <h1 className="mt-3 text-xl font-extrabold text-ink-900 sm:text-2xl">My Tests</h1>

      <div className="mt-4 flex gap-2">
        {TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setTab(t.key)}
            className={`rounded-xl2 px-3.5 py-2 text-xs font-semibold transition-colors ${tab === t.key ? "bg-primary-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"}`}
          >
            {t.label}
          </button>
        ))}
      </div>

      <div className="mt-4">
        {status === "loading" && (
          <div className="flex justify-center py-16 text-ink-400">
            <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
          </div>
        )}
        {status === "error" && <p className="rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-600">Couldn't load your tests right now.</p>}
        {status === "success" && attempts.length === 0 && <p className="rounded-xl2 border border-primary-100 bg-white p-4 text-sm text-ink-400">Nothing here yet.</p>}

        <div className="flex flex-col gap-2">
          {attempts?.map((a) => (
            <button
              key={a.attemptId}
              type="button"
              onClick={() => handleOpen(a)}
              className="flex items-center justify-between gap-3 rounded-xl2 border border-primary-100 bg-white p-4 text-left shadow-card transition-colors hover:border-primary-300"
            >
              <div className="flex min-w-0 items-center gap-3">
                <span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full ${a.testKind === "Mock" ? "bg-accent-50 text-accent-600" : a.testKind === "PreviousYearPaper" ? "bg-secondary-50 text-secondary-500" : "bg-mint-50 text-mint-500"}`}>
                  {a.testKind === "Mock" ? <Trophy className="h-4 w-4" strokeWidth={2.25} /> : a.testKind === "PreviousYearPaper" ? <BookOpen className="h-4 w-4" strokeWidth={2.25} /> : <PenLine className="h-4 w-4" strokeWidth={2.25} />}
                </span>
                <div className="min-w-0">
                  <p className="truncate text-sm font-bold text-ink-900">{a.title}</p>
                  <p className="mt-0.5 text-xs text-ink-400">
                    {a.status === "InProgress" ? "In progress" : `${a.percentageScore ?? 0}% · ${timeAgo(a.submittedAt)}`}
                  </p>
                </div>
              </div>
              <span className="flex shrink-0 items-center gap-1 text-xs font-semibold text-secondary-500">
                {a.canResume ? <Play className="h-3.5 w-3.5" strokeWidth={2.25} /> : <Eye className="h-3.5 w-3.5" strokeWidth={2.25} />}
                {a.canResume ? "Resume" : "View"}
              </span>
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}
