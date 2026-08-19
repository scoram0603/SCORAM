import { Layers } from "lucide-react";
import { API_BASE_URL } from "../../api/client";

const DIFFICULTY_STYLES = {
  Easy: "bg-mint-50 text-mint-500",
  Medium: "bg-accent-50 text-accent-600",
  Hard: "bg-red-50 text-red-600",
};

function imgSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

export default function QuestionCard({ question }) {
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

      <div className="mt-3 grid grid-cols-2 gap-2 text-sm text-ink-600">
        <Option label="A" text={question.optionA} imageUrl={question.optionAImageUrl} />
        <Option label="B" text={question.optionB} imageUrl={question.optionBImageUrl} />
        <Option label="C" text={question.optionC} imageUrl={question.optionCImageUrl} />
        <Option label="D" text={question.optionD} imageUrl={question.optionDImageUrl} />
      </div>

      <div className="mt-3 flex items-center gap-1 text-xs text-ink-400">
        <Layers className="h-3.5 w-3.5" strokeWidth={2} />
        {question.solutionCount} {question.solutionCount === 1 ? "Method" : "Methods"}
      </div>
    </div>
  );
}

function Tag({ children, className = "" }) {
  return <span className={`rounded-md px-2 py-1 text-[11px] font-semibold ${className}`}>{children}</span>;
}

function Option({ label, text, imageUrl }) {
  return (
    <span className="flex items-start gap-1.5">
      <span className="font-bold text-ink-400">{label}.</span>
      <span className="min-w-0 flex-1">
        <span className="block truncate">{text}</span>
        {imgSrc(imageUrl) && <img src={imgSrc(imageUrl)} alt="" className="mt-1 max-h-16 rounded border border-primary-100" />}
      </span>
    </span>
  );
}
