import { API_BASE_URL } from "../../api/client";
import { MathText, RichQuestionBody } from "../questions/MathText";

function imgSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

const LETTERS = ["A", "B", "C", "D"];

// Question + four options through the app's existing renderers (Hindi/English, math, images).
// `state` per option letter: "correct" | "wrong" | "picked" | undefined. The state is always ALSO said in
// text (never colour alone). Options are buttons only when onPick is given.
export default function QuestionView({ q, picked, correctOption, onPick, disabled }) {
  return (
    <div>
      {imgSrc(q.questionImageUrl) && (
        <img src={imgSrc(q.questionImageUrl)} alt="" className="mb-3 max-h-56 rounded-lg border border-primary-100" />
      )}
      <RichQuestionBody contentBlocks={q.contentBlocks} fallbackText={q.questionText} className="text-sm leading-relaxed text-ink-900" />
      <ul className="mt-3 space-y-2">
        {LETTERS.map((letter) => {
          const isCorrect = correctOption === letter;
          const isPicked = picked === letter;
          const status = correctOption
            ? isCorrect ? "Correct answer" : isPicked ? "Your answer (wrong)" : null
            : isPicked ? "Selected" : null;
          const cls = correctOption
            ? isCorrect ? "border-mint-500 bg-mint-50" : isPicked ? "border-red-300 bg-red-50" : "border-primary-100 bg-white"
            : isPicked ? "border-secondary-500 bg-primary-50" : "border-primary-100 bg-white hover:bg-primary-50/50";
          const body = (
            <>
              <span className="flex gap-2">
                <span className="font-bold text-ink-600">{letter}.</span>
                <MathText text={q[`option${letter}`]} />
              </span>
              {imgSrc(q[`option${letter}ImageUrl`]) && (
                <img src={imgSrc(q[`option${letter}ImageUrl`])} alt="" className="mt-1.5 max-h-28 rounded border border-primary-100" />
              )}
              {status && <span className="mt-1 block text-[11px] font-bold text-ink-600">{status}</span>}
            </>
          );
          return (
            <li key={letter}>
              {onPick ? (
                <button
                  type="button"
                  disabled={disabled}
                  aria-pressed={isPicked}
                  onClick={() => onPick(letter)}
                  className={`w-full rounded-xl2 border p-3 text-left text-sm text-ink-900 transition-colors disabled:cursor-default ${cls}`}
                >
                  {body}
                </button>
              ) : (
                <div className={`rounded-xl2 border p-3 text-sm text-ink-900 ${cls}`}>{body}</div>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
