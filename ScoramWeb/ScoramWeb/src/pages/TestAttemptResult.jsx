import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  ArrowLeft, Loader2, CheckCircle2, XCircle, MinusCircle, Clock, Target, ChevronDown, Flag,
} from "lucide-react";
import { getAttempt } from "../api/testAttempts";
import SolutionsPanel from "../components/questions/SolutionsPanel";
import CommentThread from "../components/questions/CommentThread";
import LikeButton from "../components/questions/LikeButton";
import ReportQuestionModal from "../components/questions/ReportQuestionModal";

const OPTION_LETTERS = ["A", "B", "C", "D"];

export default function TestAttemptResult() {
  const { attemptId } = useParams();
  const navigate = useNavigate();
  const [result, setResult] = useState(null);
  const [status, setStatus] = useState("loading");
  const [expandedIndex, setExpandedIndex] = useState(0);

  useEffect(() => {
    let cancelled = false;
    getAttempt(attemptId)
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
      <div className="px-4 py-10 text-center">
        <p className="text-sm text-ink-600">Couldn't load this result.</p>
        <button type="button" onClick={() => navigate("/tests/my")} className="mt-2 text-sm font-semibold text-secondary-500">
          Back to My Tests
        </button>
      </div>
    );
  }

  return (
    <div className="px-4 pb-10 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <button type="button" onClick={() => navigate("/tests/my")} className="flex items-center gap-1.5 text-sm font-semibold text-secondary-500">
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
        My Tests
      </button>

      <div className="mt-3 rounded-xl2 border border-primary-100 bg-white p-5 shadow-card">
        <p className="text-xs font-bold uppercase tracking-wide text-secondary-500">{result.testKind === "Mock" ? "Mock Test" : "Practice Test"}</p>
        <h1 className="mt-1 text-lg font-extrabold text-ink-900">{result.title}</h1>

        <div className="mt-4 flex items-end gap-2">
          <span className="text-3xl font-extrabold text-ink-900">{result.score}</span>
          <span className="pb-1 text-sm text-ink-400">/ {result.maxPossibleScore}</span>
          <span className="ml-auto rounded-xl2 bg-primary-50 px-3 py-1.5 text-sm font-bold text-primary-600">{result.percentageScore}%</span>
        </div>

        <div className="mt-4 grid grid-cols-3 gap-2 sm:grid-cols-5">
          <Stat icon={CheckCircle2} color="text-mint-500" label="Correct" value={result.correctCount} />
          <Stat icon={XCircle} color="text-red-500" label="Wrong" value={result.wrongCount} />
          <Stat icon={MinusCircle} color="text-ink-400" label="Skipped" value={result.skippedCount} />
          <Stat icon={Target} color="text-secondary-500" label="Accuracy" value={`${result.accuracyPercent}%`} />
          <Stat icon={Clock} color="text-accent-600" label="Time" value={formatDuration(result.timeTakenSeconds)} />
        </div>
        {result.rank && <p className="mt-3 text-xs font-semibold text-ink-400">Rank #{result.rank}{result.percentile ? ` · ${result.percentile}th percentile` : ""}</p>}
      </div>

      <h2 className="mt-6 text-sm font-bold text-ink-900">Question-wise Analysis</h2>
      <div className="mt-2 flex flex-col gap-2">
        {result.questions.map((q, i) => (
          <QuestionReviewCard
            key={q.studentAnswerId}
            question={q}
            index={i}
            expanded={expandedIndex === i}
            onToggle={() => setExpandedIndex(expandedIndex === i ? -1 : i)}
          />
        ))}
      </div>
    </div>
  );
}

function Stat({ icon: Icon, color, label, value }) {
  return (
    <div className="flex flex-col items-center rounded-lg bg-surface p-2.5 text-center">
      <Icon className={`h-4 w-4 ${color}`} strokeWidth={2.25} />
      <span className="mt-1 text-sm font-extrabold text-ink-900">{value}</span>
      <span className="text-[10px] font-medium text-ink-400">{label}</span>
    </div>
  );
}

function QuestionReviewCard({ question: q, index, expanded, onToggle }) {
  const [reportOpen, setReportOpen] = useState(false);
  const navigate = useNavigate();
  const questionType = q.isQuestionBank ? "bank" : "paper";
  const sourceQuestionId = q.isQuestionBank ? q.sourceQuestionBankQuestionId : q.sourceQuestionId;

  return (
    <div className={`rounded-xl2 border bg-white shadow-card ${q.wasSkipped ? "border-primary-100" : q.isCorrect ? "border-mint-200" : "border-red-200"}`}>
      <button type="button" onClick={onToggle} className="flex w-full items-center gap-3 p-4 text-left">
        <span className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-xs font-bold ${
          q.wasSkipped ? "bg-primary-50 text-ink-400" : q.isCorrect ? "bg-mint-50 text-mint-500" : "bg-red-50 text-red-500"
        }`}>
          {index + 1}
        </span>
        <span className="min-w-0 flex-1 truncate text-sm font-medium text-ink-900">{q.questionText}</span>
        {q.wasSkipped ? (
          <MinusCircle className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
        ) : q.isCorrect ? (
          <CheckCircle2 className="h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.25} />
        ) : (
          <XCircle className="h-4 w-4 shrink-0 text-red-500" strokeWidth={2.25} />
        )}
        <ChevronDown className={`h-4 w-4 shrink-0 text-ink-400 transition-transform ${expanded ? "rotate-180" : ""}`} strokeWidth={2} />
      </button>

      {expanded && (
        <div className="border-t border-primary-100 px-4 pb-4 pt-3">
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            {OPTION_LETTERS.map((letter) => {
              const isCorrectOption = q.correctOption === letter;
              const isSelected = q.selectedOption === letter;
              return (
                <div
                  key={letter}
                  className={`flex items-start gap-1.5 rounded-lg border px-3 py-2 text-sm ${
                    isCorrectOption
                      ? "border-mint-500 bg-mint-50 text-mint-700"
                      : isSelected
                      ? "border-red-400 bg-red-50 text-red-600"
                      : "border-primary-100 text-ink-600"
                  }`}
                >
                  <span className="font-bold">{letter}.</span>
                  <span className="min-w-0 flex-1">{q[`option${letter}`]}</span>
                  {isCorrectOption && <CheckCircle2 className="h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.25} />}
                  {isSelected && !isCorrectOption && <XCircle className="h-4 w-4 shrink-0 text-red-500" strokeWidth={2.25} />}
                </div>
              );
            })}
          </div>

          {q.explanation && (
            <div className="mt-3 rounded-lg bg-primary-50/60 p-3 text-sm leading-snug text-ink-600">
              <p className="mb-1 text-xs font-bold text-primary-600">Explanation</p>
              {q.explanation}
            </div>
          )}

          {(q.subject || q.topic) && (
            <p className="mt-2 text-xs text-ink-400">{[q.subject, q.topic].filter(Boolean).join(" / ")}</p>
          )}

          {sourceQuestionId && (
            <>
              <div className="mt-3 flex items-center justify-between border-t border-primary-100 pt-3">
                <LikeButton
                  questionId={sourceQuestionId}
                  likeCount={0}
                  dislikeCount={0}
                  myVote={null}
                  questionType={questionType}
                  onRequireLogin={() => navigate(`/login?redirect=/tests/my`)}
                />
                <button
                  type="button"
                  onClick={() => setReportOpen(true)}
                  className="flex items-center gap-1.5 text-xs font-semibold text-ink-400 hover:text-red-500"
                >
                  <Flag className="h-3.5 w-3.5" strokeWidth={2} />
                  Report Question
                </button>
              </div>

              <SolutionsPanel questionId={sourceQuestionId} questionType={questionType} onRequireLogin={() => navigate(`/login?redirect=/tests/my`)} />
              <CommentThread questionId={sourceQuestionId} questionType={questionType} onRequireLogin={() => navigate(`/login?redirect=/tests/my`)} />

              <ReportQuestionModal questionId={sourceQuestionId} questionType={questionType} open={reportOpen} onClose={() => setReportOpen(false)} />
            </>
          )}
        </div>
      )}
    </div>
  );
}

function formatDuration(totalSeconds) {
  const m = Math.floor(totalSeconds / 60);
  const s = totalSeconds % 60;
  return m > 0 ? `${m}m ${s}s` : `${s}s`;
}
