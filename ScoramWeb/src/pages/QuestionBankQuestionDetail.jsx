import { useEffect, useState } from "react";
import { ArrowLeft, Loader2, CheckCircle2, Eye, Flag, Share2 } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { getQuestionBankQuestion } from "../api/questionBank";
import SolutionsPanel from "../components/questions/SolutionsPanel";
import ReportQuestionModal from "../components/questions/ReportQuestionModal";
import CommentThread from "../components/questions/CommentThread";
import LikeButton from "../components/questions/LikeButton";
import ShareQuestionModal from "../components/chat/ShareQuestionModal";
import { useAuth } from "../context/AuthContext";

const OPTION_LETTERS = ["A", "B", "C", "D"];

// Section 20 of the spec: same fields as the legacy PYQ QuestionDetail page, plus a Like/Dislike and
// full Discussion thread matching what PYQ questions already have (both reuse the same shared
// backend infra as Report/Solution above -- see CommentThread's questionType prop and LikeButton).
// "Asked In" is a LIST of exam+year pairs (a Question Bank question can appear in several), so there's
// no single difficulty/exam tag the way the legacy page has.
export default function QuestionBankQuestionDetail() {
  const { questionId } = useParams();
  const navigate = useNavigate();
  const { isAuthenticated } = useAuth();
  const [question, setQuestion] = useState(null);
  const [status, setStatus] = useState("loading");
  const [revealed, setRevealed] = useState(false);
  const [reportOpen, setReportOpen] = useState(false);
  const [shareOpen, setShareOpen] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setStatus("loading");
    setRevealed(false);
    getQuestionBankQuestion(questionId)
      .then((data) => {
        if (cancelled) return;
        setQuestion(data);
        setStatus("success");
      })
      .catch(() => {
        if (!cancelled) setStatus("error");
      });
    return () => {
      cancelled = true;
    };
  }, [questionId]);

  const loginRedirect = `/login?redirect=/question-bank/${questionId}`;

  return (
    <div className="px-4 pb-10 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <button
        type="button"
        onClick={() => navigate(-1)}
        className="flex items-center gap-1.5 text-sm font-semibold text-secondary-500"
      >
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
        Back
      </button>

      {status === "loading" && (
        <div className="flex justify-center py-16 text-ink-400">
          <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
        </div>
      )}

      {status === "error" && (
        <p className="mt-4 rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-600">
          Couldn't load this question right now.
        </p>
      )}

      {status === "success" && question && (
        <>
          <div className="mt-4 rounded-xl2 border border-primary-100 bg-white p-4 shadow-card sm:p-5">
            <div className="flex flex-wrap gap-1.5">
              <Tag className="bg-violet-50 text-violet-500">{question.subject}</Tag>
              <Tag className="bg-primary-50 text-primary-600">{question.topic}</Tag>
            </div>

            <p className="mt-3 text-[15px] font-semibold leading-snug text-ink-900">{question.questionText}</p>

            <div className="mt-4 grid grid-cols-1 gap-2 sm:grid-cols-2">
              {OPTION_LETTERS.map((letter) => {
                const text = question[`option${letter}`];
                const isCorrect = revealed && question.correctOption === letter;
                return (
                  <div
                    key={letter}
                    className={`flex items-start gap-1.5 rounded-lg border px-3 py-2.5 text-sm ${
                      isCorrect ? "border-mint-500 bg-mint-50 text-mint-700" : "border-primary-100 text-ink-600"
                    }`}
                  >
                    <span className="font-bold">{letter}.</span>
                    <span className="min-w-0 flex-1">{text}</span>
                    {isCorrect && <CheckCircle2 className="h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.25} />}
                  </div>
                );
              })}
            </div>

            {!revealed ? (
              <button
                type="button"
                onClick={() => setRevealed(true)}
                className="mt-4 flex items-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-primary-700"
              >
                <Eye className="h-4 w-4" strokeWidth={2.25} />
                Reveal answer
              </button>
            ) : (
              question.explanation && (
                <div className="mt-4 rounded-lg bg-primary-50/60 p-3.5 text-sm leading-snug text-ink-600">
                  <p className="mb-1 text-xs font-bold text-primary-600">Explanation</p>
                  {question.explanation}
                </div>
              )
            )}

            {question.sourceReference && (
              <p className="mt-2 text-xs text-ink-400">Source: {question.sourceReference}</p>
            )}

            {question.askedIn?.length > 0 && (
              <div className="mt-4 border-t border-primary-100 pt-3">
                <p className="mb-1.5 text-xs font-bold text-ink-600">Asked In</p>
                <div className="flex flex-wrap gap-1.5">
                  {question.askedIn.map((a) => (
                    <Tag key={`${a.examId}-${a.year}`} className="bg-secondary-50 text-secondary-500">
                      {a.examName} — {a.year}
                    </Tag>
                  ))}
                </div>
              </div>
            )}

            <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-primary-100 pt-3">
              <LikeButton
                questionId={questionId}
                likeCount={question.likeCount}
                dislikeCount={question.dislikeCount}
                myVote={question.myVote}
                questionType="bank"
                onChange={(result) => setQuestion((prev) => ({ ...prev, ...result }))}
                onRequireLogin={() => navigate(loginRedirect)}
              />
              <button
                type="button"
                onClick={() => (isAuthenticated ? setShareOpen(true) : navigate(loginRedirect))}
                className="flex items-center gap-1.5 text-xs font-semibold text-ink-400 hover:text-primary-600"
              >
                <Share2 className="h-3.5 w-3.5" strokeWidth={2} />
                Share
              </button>
              <button
                type="button"
                onClick={() => setReportOpen(true)}
                className="flex items-center gap-1.5 text-xs font-semibold text-ink-400 hover:text-red-500"
              >
                <Flag className="h-3.5 w-3.5" strokeWidth={2} />
                Report Question
              </button>
            </div>
          </div>

          <SolutionsPanel
            questionId={questionId}
            questionType="bank"
            onRequireLogin={() => navigate(loginRedirect)}
          />

          <CommentThread
            questionId={questionId}
            questionType="bank"
            onRequireLogin={() => navigate(loginRedirect)}
          />

          <ReportQuestionModal
            questionId={questionId}
            questionType="bank"
            open={reportOpen}
            onClose={() => setReportOpen(false)}
          />

          <ShareQuestionModal questionId={questionId} open={shareOpen} onClose={() => setShareOpen(false)} />
        </>
      )}
    </div>
  );
}

function Tag({ children, className = "" }) {
  return <span className={`rounded-md px-2 py-1 text-[11px] font-semibold ${className}`}>{children}</span>;
}
