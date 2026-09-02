import { useState } from "react";
import { Check, X, Layers } from "lucide-react";
import { API_BASE_URL } from "../../api/client";

// A separate component from QuestionCard.jsx on purpose: that one is reused inside admin's
// Question Editor, Bulk Import preview, and Paper Detail admin view, where "click an option to
// answer it" would be the wrong interaction entirely (an admin is editing/previewing the question,
// not answering it). This one is specifically for places a *student* is meant to actually attempt
// the question inline -- currently just Today's Challenge (TodaysChallenge.jsx).

const DIFFICULTY_STYLES = {
  Easy: "bg-mint-50 text-mint-500",
  Medium: "bg-accent-50 text-accent-600",
  Hard: "bg-red-50 text-red-600",
};

function imgSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

export default function InteractiveQuestionCard({ question }) {
  // Locked once set -- an answer is a commitment, not a guess-until-you-get-it-right toy; this also
  // matches how every timed test elsewhere in the app treats a submitted answer as final.
  const [selected, setSelected] = useState(null);
  const answered = selected !== null;

  return (
    <div className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
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
        <img src={imgSrc(question.questionImageUrl)} alt="" className="mt-2 max-h-48 rounded-lg border border-primary-100" />
      )}

      <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-2">
        <AnswerOption
          label="A" text={question.optionA} imageUrl={question.optionAImageUrl}
          optionKey="A" selected={selected} correctOption={question.correctOption}
          answered={answered} onSelect={setSelected}
        />
        <AnswerOption
          label="B" text={question.optionB} imageUrl={question.optionBImageUrl}
          optionKey="B" selected={selected} correctOption={question.correctOption}
          answered={answered} onSelect={setSelected}
        />
        <AnswerOption
          label="C" text={question.optionC} imageUrl={question.optionCImageUrl}
          optionKey="C" selected={selected} correctOption={question.correctOption}
          answered={answered} onSelect={setSelected}
        />
        <AnswerOption
          label="D" text={question.optionD} imageUrl={question.optionDImageUrl}
          optionKey="D" selected={selected} correctOption={question.correctOption}
          answered={answered} onSelect={setSelected}
        />
      </div>

      {answered && (
        <div
          className={`mt-3 flex items-start gap-2 rounded-lg p-3 text-sm ${
            selected === question.correctOption
              ? "bg-mint-50 text-mint-500"
              : "bg-red-50 text-red-700"
          }`}
        >
          {selected === question.correctOption ? (
            <Check className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.5} />
          ) : (
            <X className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.5} />
          )}
          <div className="min-w-0 flex-1">
            <p className="font-semibold">
              {selected === question.correctOption
                ? "Correct!"
                : `Not quite — the correct answer is ${question.correctOption}.`}
            </p>
            {question.explanation && (
              <p className="mt-1 text-ink-600">{question.explanation}</p>
            )}
            {imgSrc(question.explanationImageUrl) && (
              <img
                src={imgSrc(question.explanationImageUrl)}
                alt=""
                className="mt-2 max-h-40 rounded-lg border border-primary-100"
              />
            )}
          </div>
        </div>
      )}

      <div className="mt-3 flex items-center gap-1 text-xs text-ink-400">
        <Layers className="h-3.5 w-3.5" strokeWidth={2} />
        {question.solutionCount} {question.solutionCount === 1 ? "Method" : "Methods"}
      </div>
    </div>
  );
}

function AnswerOption({ label, text, imageUrl, optionKey, selected, correctOption, answered, onSelect }) {
  const isSelected = selected === optionKey;
  const isCorrectOption = optionKey === correctOption;

  // Precedence once answered: the option the student picked, right or wrong, always shows its own
  // verdict color; if they picked wrong, the actual correct option is also highlighted green so
  // they see what they should have picked -- everything else stays neutral.
  let stateClasses = "border-primary-100 bg-white hover:border-primary-300";
  if (answered) {
    if (isSelected && isCorrectOption) stateClasses = "border-mint-500 bg-mint-50";
    else if (isSelected && !isCorrectOption) stateClasses = "border-red-400 bg-red-50";
    else if (isCorrectOption) stateClasses = "border-mint-100 bg-mint-50/60";
    else stateClasses = "border-primary-100 bg-white opacity-60";
  }

  return (
    <button
      type="button"
      disabled={answered}
      onClick={() => onSelect(optionKey)}
      className={`flex items-start gap-1.5 rounded-lg border p-2 text-left text-sm text-ink-600 transition-colors disabled:cursor-default ${stateClasses}`}
    >
      <span className="font-bold text-ink-400">{label}.</span>
      <span className="min-w-0 flex-1">
        <span className="block">{text}</span>
        {imgSrc(imageUrl) && <img src={imgSrc(imageUrl)} alt="" className="mt-1 max-h-16 rounded border border-primary-100" />}
      </span>
      {answered && isSelected && (
        isCorrectOption
          ? <Check className="mt-0.5 h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.5} />
          : <X className="mt-0.5 h-4 w-4 shrink-0 text-red-600" strokeWidth={2.5} />
      )}
      {answered && !isSelected && isCorrectOption && (
        <Check className="mt-0.5 h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.5} />
      )}
    </button>
  );
}

function Tag({ children, className = "" }) {
  return <span className={`rounded-md px-2 py-1 text-[11px] font-semibold ${className}`}>{children}</span>;
}
