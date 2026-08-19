import { useEffect, useState } from "react";
import { ArrowLeft, Loader2, CheckCircle2, Layers, Eye, ArrowRight } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { getQuestionById } from "../api/questions";
import { API_BASE_URL } from "../api/client";
import CommentThread from "../components/questions/CommentThread";
import SolutionsPanel from "../components/questions/SolutionsPanel";
import LikeButton from "../components/questions/LikeButton";

const DIFFICULTY_STYLES = {
  Easy: "bg-mint-50 text-mint-500",
  Medium: "bg-accent-50 text-accent-600",
  Hard: "bg-red-50 text-red-600",
};
const OPTION_LETTERS = ["A", "B", "C", "D"];

function imgSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

export default function QuestionDetail() {
  const { questionId } = useParams();
  const navigate = useNavigate();
  const [question, setQuestion] = useState(null);
  const [status, setStatus] = useState("loading");
  const [revealed, setRevealed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setStatus("loading");
    setRevealed(false);
    getQuestionById(questionId)
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
              <Tag className="bg-secondary-50 text-secondary-500">
                {question.examName} {question.year}
              </Tag>
              <Tag className="bg-violet-50 text-violet-500">{question.subject}</Tag>
              <Tag className={DIFFICULTY_STYLES[question.difficultyLevel] || "bg-primary-50 text-primary-600"}>
                {question.difficultyLevel}
              </Tag>
            </div>

            <p className="mt-3 text-[15px] font-semibold leading-snug text-ink-900">{question.questionText}</p>
            {imgSrc(question.questionImageUrl) && (
              <img src={imgSrc(question.questionImageUrl)} alt="" className="mt-2 max-h-64 rounded-lg border border-primary-100" />
            )}

            <div className="mt-4 grid grid-cols-1 gap-2 sm:grid-cols-2">
              {OPTION_LETTERS.map((letter) => {
                const text = question[`option${letter}`];
                const imageUrl = question[`option${letter}ImageUrl`];
                const isCorrect = revealed && question.correctOption === letter;
                return (
                  <div
                    key={letter}
                    className={`flex items-start gap-1.5 rounded-lg border px-3 py-2.5 text-sm ${
                      isCorrect ? "border-mint-500 bg-mint-50 text-mint-700" : "border-primary-100 text-ink-600"
                    }`}
                  >
                    <span className="font-bold">{letter}.</span>
                    <span className="min-w-0 flex-1">
                      {text}
                      {imgSrc(imageUrl) && <img src={imgSrc(imageUrl)} alt="" className="mt-1 max-h-20 rounded border border-primary-100" />}
                    </span>
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
              <>
                {question.explanation && (
                  <div className="mt-4 rounded-lg bg-primary-50/60 p-3.5 text-sm leading-snug text-ink-600">
                    <p className="mb-1 text-xs font-bold text-primary-600">Explanation</p>
                    {question.explanation}
                    {imgSrc(question.explanationImageUrl) && (
                      <img src={imgSrc(question.explanationImageUrl)} alt="" className="mt-2 max-h-64 rounded-lg border border-primary-100" />
                    )}
                  </div>
                )}
                {question.sourceReference && (
                  <p className="mt-2 text-xs text-ink-400">Source: {question.sourceReference}</p>
                )}
              </>
            )}

            <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-primary-100 pt-3">
              <div className="flex flex-wrap items-center gap-3">
                <LikeButton
                  questionId={questionId}
                  likeCount={question.likeCount}
                  dislikeCount={question.dislikeCount}
                  myVote={question.myVote}
                  questionType="paper"
                  onChange={(result) => setQuestion((prev) => ({ ...prev, ...result }))}
                  onRequireLogin={() => navigate(`/login?redirect=/questions/${questionId}`)}
                />
                <span className="flex items-center gap-1 text-xs text-ink-400">
                  <Layers className="h-3.5 w-3.5" strokeWidth={2} />
                  {question.solutionCount} {question.solutionCount === 1 ? "Method" : "Methods"}
                </span>
              </div>
              {question.examId && (
                <button
                  type="button"
                  onClick={() => navigate(`/search?examId=${question.examId}`)}
                  className="flex items-center gap-1 text-xs font-semibold text-secondary-500 hover:text-secondary-600"
                >
                  More from {question.examName} {question.year}
                  <ArrowRight className="h-3.5 w-3.5" strokeWidth={2.5} />
                </button>
              )}
            </div>
          </div>

          <SolutionsPanel
            questionId={questionId}
            onRequireLogin={() => navigate(`/login?redirect=/questions/${questionId}`)}
          />

          <CommentThread
            questionId={questionId}
            onRequireLogin={() => navigate(`/login?redirect=/questions/${questionId}`)}
          />
        </>
      )}
    </div>
  );
}

function Tag({ children, className = "" }) {
  return <span className={`rounded-md px-2 py-1 text-[11px] font-semibold ${className}`}>{children}</span>;
}
