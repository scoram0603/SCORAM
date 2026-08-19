import { CheckCircle2, XCircle, MinusCircle, Clock, Target, ArrowLeft, ArrowRight } from "lucide-react";

const OPTION_LETTERS = ["A", "B", "C", "D"];

export default function TestResultView({ result, onBack, onViewQuestion }) {
  return (
    <div className="px-4 pb-10 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <button
        type="button"
        onClick={onBack}
        className="flex items-center gap-1.5 text-sm font-semibold text-secondary-500"
      >
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
        Back to Tests
      </button>

      <h1 className="mt-3 text-xl font-extrabold text-ink-900 sm:text-2xl">{result.mockTestTitle}</h1>

      <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
        <StatCard label="Score" value={`${result.score}/${result.maxPossibleScore}`} tint="primary" />
        <StatCard label="Accuracy" value={`${result.accuracyPercent}%`} tint="secondary" icon={Target} />
        <StatCard label="Correct" value={result.correctCount} tint="mint" icon={CheckCircle2} />
        <StatCard label="Wrong" value={result.wrongCount} tint="accent" icon={XCircle} />
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-ink-400">
        <span className="flex items-center gap-1">
          <Clock className="h-3.5 w-3.5" strokeWidth={2} />
          Time taken: {Math.floor(result.timeTakenSeconds / 60)}m {result.timeTakenSeconds % 60}s
        </span>
        <span className="flex items-center gap-1">
          <MinusCircle className="h-3.5 w-3.5" strokeWidth={2} />
          Skipped: {result.skippedCount}
        </span>
      </div>

      <h2 className="mt-8 text-[15px] font-bold text-ink-900">Detailed Solutions</h2>
      <div className="mt-3 flex flex-col gap-3">
        {result.questions.map((q, i) => (
          <div key={q.questionId} className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card sm:p-5">
            <div className="flex items-start justify-between gap-2">
              <p className="text-sm font-semibold leading-snug text-ink-900">
                <span className="text-ink-400">Q{i + 1}.</span> {q.questionText}
              </p>
              {q.isCorrect ? (
                <CheckCircle2 className="h-5 w-5 shrink-0 text-mint-500" strokeWidth={2.25} />
              ) : q.selectedOption ? (
                <XCircle className="h-5 w-5 shrink-0 text-red-500" strokeWidth={2.25} />
              ) : (
                <MinusCircle className="h-5 w-5 shrink-0 text-ink-400" strokeWidth={2.25} />
              )}
            </div>

            <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-2">
              {OPTION_LETTERS.map((letter) => {
                const text = q[`option${letter}`];
                const isCorrectOption = q.correctOption === letter;
                const isSelectedOption = q.selectedOption === letter;
                return (
                  <div
                    key={letter}
                    className={`rounded-lg border px-3 py-2 text-sm ${
                      isCorrectOption
                        ? "border-mint-500 bg-mint-50 text-mint-700"
                        : isSelectedOption
                        ? "border-red-400 bg-red-50 text-red-700"
                        : "border-primary-100 text-ink-600"
                    }`}
                  >
                    <span className="font-bold">{letter}.</span> {text}
                  </div>
                );
              })}
            </div>

            {q.explanation && (
              <p className="mt-3 rounded-lg bg-primary-50/60 p-3 text-xs leading-snug text-ink-600">
                <span className="font-bold text-primary-600">Explanation: </span>
                {q.explanation}
              </p>
            )}

            {onViewQuestion && (
              <button
                type="button"
                onClick={() => onViewQuestion(q.questionId)}
                className="mt-3 flex items-center gap-1 text-xs font-semibold text-secondary-500 hover:text-secondary-600"
              >
                Discuss this question
                <ArrowRight className="h-3.5 w-3.5" strokeWidth={2.5} />
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function StatCard({ label, value, tint, icon: Icon }) {
  const tints = {
    primary: "bg-primary-50 text-primary-600",
    secondary: "bg-secondary-50 text-secondary-500",
    mint: "bg-mint-50 text-mint-500",
    accent: "bg-accent-50 text-accent-600",
  };
  return (
    <div className="rounded-xl2 border border-primary-100 bg-white p-3 shadow-card sm:p-4">
      <span className={`flex h-8 w-8 items-center justify-center rounded-lg ${tints[tint]}`}>
        {Icon ? <Icon className="h-4 w-4" strokeWidth={2.25} /> : null}
      </span>
      <p className="mt-2 text-lg font-extrabold text-ink-900">{value}</p>
      <p className="text-xs text-ink-400">{label}</p>
    </div>
  );
}
