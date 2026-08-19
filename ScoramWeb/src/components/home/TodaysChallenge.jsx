import { useEffect, useState } from "react";
import { Flame, ChevronRight, Layers, ArrowRight, ChevronUp } from "lucide-react";
import { Link } from "react-router-dom";
import { getTodaysChallenge } from "../../api/questions";
import QuestionCard from "../questions/QuestionCard";

export default function TodaysChallenge() {
  const [question, setQuestion] = useState(null);
  const [status, setStatus] = useState("loading"); // loading | ready | empty | error
  const [expanded, setExpanded] = useState(false);

  useEffect(() => {
    getTodaysChallenge()
      .then((q) => {
        setQuestion(q);
        setStatus("ready");
      })
      .catch((err) => {
        setStatus(err.status === 404 ? "empty" : "error");
      });
  }, []);

  if (status === "loading" || status === "error") return null; // quietly skip; not worth a section for a transient hiccup
  if (status === "empty") return null; // no Published questions yet -- nothing honest to show here

  return (
    <section className="px-4 pb-6 sm:px-6 lg:px-0">
      <div className="mb-3 flex items-center justify-between sm:mb-4">
        <h3 className="flex items-center gap-1.5 text-[17px] font-bold text-ink-900 sm:text-lg">
          <Flame className="h-5 w-5 text-accent-500" strokeWidth={2.25} fill="currentColor" fillOpacity={0.15} />
          Today's Challenge
        </h3>
        <Link to="/search" className="flex items-center gap-0.5 text-sm font-semibold text-secondary-500">
          View All
          <ChevronRight className="h-4 w-4" strokeWidth={2.5} />
        </Link>
      </div>

      {expanded ? (
        <div>
          <QuestionCard question={question} />
          <button
            type="button"
            onClick={() => setExpanded(false)}
            className="mt-2 flex items-center gap-1 text-xs font-semibold text-ink-400 hover:text-ink-600"
          >
            <ChevronUp className="h-3.5 w-3.5" strokeWidth={2.5} />
            Collapse
          </button>
        </div>
      ) : (
        <div className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card sm:p-5">
          <div className="flex flex-wrap gap-1.5">
            <Tag className="bg-secondary-50 text-secondary-500">{question.examName} {question.year}</Tag>
            <Tag className="bg-mint-50 text-mint-500">{question.subject}</Tag>
            <Tag className="bg-accent-50 text-accent-600">{question.difficultyLevel}</Tag>
          </div>

          <p className="mt-3 text-[15px] font-semibold leading-snug text-ink-900 sm:text-base">{question.questionText}</p>

          <div className="mt-4 flex items-center justify-between">
            <span className="flex items-center gap-1 text-xs text-ink-400">
              <Layers className="h-3.5 w-3.5" strokeWidth={2} />
              {question.solutionCount} {question.solutionCount === 1 ? "Method" : "Methods"}
            </span>

            <button
              type="button"
              onClick={() => setExpanded(true)}
              className="flex items-center gap-1 rounded-lg bg-primary-600 px-3 py-2 text-xs font-semibold text-white transition-colors hover:bg-primary-700"
            >
              View Question
              <ArrowRight className="h-3.5 w-3.5" strokeWidth={2.5} />
            </button>
          </div>
        </div>
      )}
    </section>
  );
}

function Tag({ children, className = "" }) {
  return <span className={`rounded-md px-2 py-1 text-[11px] font-semibold ${className}`}>{children}</span>;
}
