import { useState } from "react";
import { CheckCircle2, XCircle, RotateCcw } from "lucide-react";
import { API_BASE_URL } from "../../api/client";
import { MathText } from "./MathText";

function imgSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

const OPTION_LETTERS = ["A", "B", "C", "D"];

// Click-to-check MCQ practice interaction (replaces the old all-at-once "View Answer" button):
// the correct option is never shown up front. The student picks an option; if it's right, that
// option turns green; if it's wrong, that option turns red AND the correct one turns green next to
// it, so they still learn the right answer. Once picked, the explanation appears and the options
// lock (a small "Try Again" resets back to the unanswered state for re-practice, since Question
// Bank is meant for repeatable practice, not a one-shot graded test).
//
// Shared between QuestionBankFeedCard (the search feed) and QuestionBankQuestionDetail (the
// standalone/shared-link page) so both stay in sync automatically.
export default function InteractiveOptions({ question, onInteract, size = "md" }) {
  const [selected, setSelected] = useState(null); // null | "A" | "B" | "C" | "D"

  const answered = selected !== null;
  const isCompact = size === "sm";

  function pick(letter) {
    if (answered) return; // locked until Try Again
    setSelected(letter);
    onInteract?.();
  }

  function reset() {
    setSelected(null);
  }

  return (
    <div>
      <div className={`grid grid-cols-1 gap-2 sm:grid-cols-2`}>
        {OPTION_LETTERS.map((letter) => {
          const text = question[`option${letter}`];
          const imageUrl = question[`option${letter}ImageUrl`];
          const isCorrectOption = letter === question.correctOption;
          const isPicked = letter === selected;

          let stateClasses = "border-primary-100 text-ink-600";
          if (answered && isCorrectOption) stateClasses = "border-mint-500 bg-mint-50 text-mint-700";
          else if (answered && isPicked && !isCorrectOption) stateClasses = "border-red-400 bg-red-50 text-red-700";
          else if (answered) stateClasses = "border-primary-100 text-ink-400"; // unpicked, incorrect -- fades back

          return (
            <button
              type="button"
              key={letter}
              disabled={answered}
              onClick={() => pick(letter)}
              className={`flex items-start gap-1.5 rounded-lg border px-3 text-left text-sm transition-colors ${
                isCompact ? "py-2" : "py-2.5"
              } ${stateClasses} ${!answered ? "hover:border-primary-300 hover:bg-primary-50/60 cursor-pointer" : "cursor-default"}`}
            >
              <span className="font-bold">{letter}.</span>
              <span className="min-w-0 flex-1">
                <MathText text={text} />
                {imgSrc(imageUrl) && <img src={imgSrc(imageUrl)} alt="" className="mt-1 max-h-20 rounded border border-primary-100" />}
              </span>
              {answered && isCorrectOption && <CheckCircle2 className="h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.25} />}
              {answered && isPicked && !isCorrectOption && <XCircle className="h-4 w-4 shrink-0 text-red-500" strokeWidth={2.25} />}
            </button>
          );
        })}
      </div>

      {answered && (
        <div className="mt-2.5 flex items-center justify-between gap-2">
          <p className={`text-xs font-semibold ${selected === question.correctOption ? "text-mint-600" : "text-red-500"}`}>
            {selected === question.correctOption ? "Correct!" : `Not quite — correct answer is ${question.correctOption}.`}
          </p>
          <button
            type="button"
            onClick={reset}
            className="flex shrink-0 items-center gap-1 text-[11px] font-semibold text-ink-400 hover:text-primary-600"
          >
            <RotateCcw className="h-3 w-3" strokeWidth={2.25} />
            Try Again
          </button>
        </div>
      )}

      {answered && question.explanation && (
        <div className="mt-2.5 rounded-lg bg-primary-50/60 p-3.5 text-sm leading-snug text-ink-600">
          <p className="mb-1 text-xs font-bold text-primary-600">Explanation</p>
          <MathText text={question.explanation} />
          {imgSrc(question.explanationImageUrl) && (
            <img src={imgSrc(question.explanationImageUrl)} alt="" className="mt-2 max-h-64 rounded-lg border border-primary-100" />
          )}
        </div>
      )}
    </div>
  );
}
