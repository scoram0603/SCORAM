import { useEffect, useState } from "react";
import { ArrowLeft, Loader2, Flag, Share2 } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { getQuestionBankQuestion } from "../api/questionBank";
import SolutionsPanel from "../components/questions/SolutionsPanel";
import ReportQuestionModal from "../components/questions/ReportQuestionModal";
import CommentThread from "../components/questions/CommentThread";
import LikeButton from "../components/questions/LikeButton";
import BookmarkButton from "../components/questions/BookmarkButton";
import ShareQuestionModal from "../components/chat/ShareQuestionModal";
import InteractiveOptions from "../components/questions/InteractiveOptions";
import { useAuth } from "../context/AuthContext";
import { API_BASE_URL } from "../api/client";

function imgSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

// Section 20 of the spec: same fields as the legacy PYQ QuestionDetail page, plus a Like/Dislike and
// full Discussion thread matching what PYQ questions already have (both reuse the same shared
// backend infra as Report/Solution above -- see CommentThread's questionType prop and LikeButton).
// "Asked In" is a LIST of exam+year pairs (a Question Bank question can appear in several), so there's
// no single difficulty/exam tag the way the legacy page has.
//
// Options are click-to-check (InteractiveOptions) -- the correct answer only appears once the
// student picks something, same interaction as the search feed's cards.
export default function QuestionBankQuestionDetail() {
  const { questionId } = useParams();
  const navigate = useNavigate();
  const { isAuthenticated } = useAuth();
  const [question, setQuestion] = useState(null);
  const [status, setStatus] = useState("loading");
  const [reportOpen, setReportOpen] = useState(false);
  const [shareOpen, setShareOpen] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setStatus("loading");
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
              {question.language && <Tag className="bg-mint-50 text-mint-600">{question.language}</Tag>}
            </div>

            <p className="mt-3 text-[15px] font-semibold leading-snug text-ink-900">{question.questionText}</p>
            {imgSrc(question.questionImageUrl) && (
              <img src={imgSrc(question.questionImageUrl)} alt="" className="mt-2 max-h-64 rounded-lg border border-primary-100" />
            )}

            <div className="mt-4">
              <InteractiveOptions question={question} />
            </div>

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
              <BookmarkButton
                type="questionBank"
                id={questionId}
                isBookmarked={question.isBookmarked}
                onChange={(isBookmarked) => setQuestion((prev) => ({ ...prev, isBookmarked }))}
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
