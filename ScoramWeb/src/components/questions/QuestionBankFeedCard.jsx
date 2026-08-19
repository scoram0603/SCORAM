import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { CheckCircle2, Eye, EyeOff, MessageSquare, Share2, Flag, Layers } from "lucide-react";
import LikeButton from "./LikeButton";
import CommentThread from "./CommentThread";
import SolutionsPanel from "./SolutionsPanel";
import ReportQuestionModal from "./ReportQuestionModal";
import ShareQuestionModal from "../chat/ShareQuestionModal";
import { useAuth } from "../../context/AuthContext";

const OPTION_LETTERS = ["A", "B", "C", "D"];

function Tag({ children, className = "" }) {
  return <span className={`rounded-md px-2 py-1 text-[11px] font-semibold ${className}`}>{children}</span>;
}

// FEED REDESIGN -- one question per card, everything inline (View Answer, Discuss, Like/Dislike,
// Share) instead of clicking through to a separate page for each. Reuses the exact same
// LikeButton/CommentThread/SolutionsPanel/ShareQuestionModal components the standalone detail page
// (QuestionBankQuestionDetail.jsx, which still exists as the landing page for shared/direct links)
// uses -- just collapsed behind toggles here since a feed with everything always-expanded for every
// card would be unreadable.
//
// PREMIUM UI PASS -- tag colors follow the brand hierarchy from the design brief: navy for
// classification (subject), teal for secondary/community info (topic), orange for the
// exam+year highlight badge. `compact` (from the List/Compact toggle) tightens spacing and
// truncates the question text instead of showing full options inline.
export default function QuestionBankFeedCard({ question, onQuestionChange, compact = false }) {
  const navigate = useNavigate();
  const { isAuthenticated } = useAuth();
  const [revealed, setRevealed] = useState(false);
  const [activePanel, setActivePanel] = useState(null); // null | "discuss" | "methods"
  const [reportOpen, setReportOpen] = useState(false);
  const [shareOpen, setShareOpen] = useState(false);
  const [expanded, setExpanded] = useState(!compact);

  const loginRedirect = `/login?redirect=/question-bank/${question.id}`;
  const showBody = expanded || !compact;

  function togglePanel(panel) {
    setActivePanel((prev) => (prev === panel ? null : panel));
    if (compact) setExpanded(true);
  }

  return (
    <div className={`rounded-xl2 border border-primary-100 bg-white shadow-card transition-shadow hover:shadow-cardHover ${compact ? "p-3.5" : "p-4 sm:p-5"}`}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap gap-1.5">
          <Tag className="bg-primary-50 text-primary-600">{question.subject}</Tag>
          <Tag className="bg-teal-50 text-teal-600">{question.topic}</Tag>
        </div>
        {question.askedIn?.[0] && (
          <Tag className="bg-accent-50 text-accent-600">
            {question.askedIn[0].examName} — {question.askedIn[0].year}
          </Tag>
        )}
      </div>

      {compact ? (
        <button
          type="button"
          onClick={() => setExpanded((e) => !e)}
          className={`mt-2.5 text-left text-sm font-semibold leading-snug text-ink-900 ${expanded ? "" : "line-clamp-2"}`}
        >
          {question.questionText}
        </button>
      ) : (
        <p className="mt-3 text-[15px] font-semibold leading-snug text-ink-900">{question.questionText}</p>
      )}

      {showBody && (
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
      )}

      {showBody && revealed && question.explanation && (
        <div className="mt-3 rounded-lg bg-primary-50/60 p-3.5 text-sm leading-snug text-ink-600">
          <p className="mb-1 text-xs font-bold text-primary-600">Explanation</p>
          {question.explanation}
        </div>
      )}

      {/* Action row -- the 4 requested inline actions, plus Report tucked at the end (existing feature). */}
      <div className="mt-4 flex flex-wrap items-center gap-2 border-t border-primary-100 pt-3">
        <button
          type="button"
          onClick={() => {
            setRevealed((r) => !r);
            if (compact) setExpanded(true);
          }}
          className={`flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors ${
            revealed ? "bg-mint-50 text-mint-600" : "bg-primary-50 text-primary-600 hover:bg-primary-100"
          }`}
        >
          {revealed ? <EyeOff className="h-3.5 w-3.5" strokeWidth={2.25} /> : <Eye className="h-3.5 w-3.5" strokeWidth={2.25} />}
          {revealed ? "Hide Answer" : "View Answer"}
        </button>

        <button
          type="button"
          onClick={() => togglePanel("discuss")}
          className={`flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors ${
            activePanel === "discuss" ? "bg-secondary-50 text-secondary-600" : "bg-primary-50 text-primary-600 hover:bg-primary-100"
          }`}
        >
          <MessageSquare className="h-3.5 w-3.5" strokeWidth={2.25} />
          Discuss{question.commentCount > 0 ? ` (${question.commentCount})` : ""}
        </button>

        <LikeButton
          questionId={question.id}
          likeCount={question.likeCount}
          dislikeCount={question.dislikeCount}
          myVote={question.myVote}
          questionType="bank"
          onChange={(result) => onQuestionChange(question.id, result)}
          onRequireLogin={() => navigate(loginRedirect)}
        />

        <button
          type="button"
          onClick={() => togglePanel("methods")}
          className="flex items-center gap-1.5 rounded-lg bg-primary-50 px-3 py-1.5 text-xs font-semibold text-primary-600 hover:bg-primary-100"
        >
          <Layers className="h-3.5 w-3.5" strokeWidth={2.25} />
          {question.solutionCount ?? 0} Methods
        </button>

        <button
          type="button"
          onClick={() => (isAuthenticated ? setShareOpen(true) : navigate(loginRedirect))}
          className="ml-auto flex items-center gap-1.5 text-xs font-semibold text-ink-400 hover:text-primary-600"
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
        </button>
      </div>

      {activePanel === "discuss" && (
        <div className="mt-3 border-t border-primary-100 pt-3">
          <CommentThread questionId={question.id} questionType="bank" onRequireLogin={() => navigate(loginRedirect)} />
        </div>
      )}

      {activePanel === "methods" && (
        <div className="mt-3 border-t border-primary-100 pt-3">
          <SolutionsPanel questionId={question.id} questionType="bank" onRequireLogin={() => navigate(loginRedirect)} />
        </div>
      )}

      <ReportQuestionModal questionId={question.id} questionType="bank" open={reportOpen} onClose={() => setReportOpen(false)} />
      <ShareQuestionModal questionId={question.id} open={shareOpen} onClose={() => setShareOpen(false)} />
    </div>
  );
}
