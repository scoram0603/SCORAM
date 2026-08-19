import { Layers, CalendarClock } from "lucide-react";

// Same visual language as QuestionCard (the Paper-based question card) but adapted for a Question
// Bank question: no single exam/year/difficulty (a Question Bank question can belong to several
// exam+year pairs at once -- see askedIn), and Subject/Topic come from the Question Bank's own
// master tables rather than free-text fields.
export default function QuestionBankCard({ question }) {
  const askedIn = question.askedIn || [];
  const visibleTags = askedIn.slice(0, 3);
  const extraCount = askedIn.length - visibleTags.length;

  return (
    <div className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
      <div className="flex flex-wrap gap-1.5">
        <Tag className="bg-violet-50 text-violet-500">{question.subject}</Tag>
        <Tag className="bg-primary-50 text-primary-600">{question.topic}</Tag>
      </div>

      <p className="mt-3 text-[15px] font-semibold leading-snug text-ink-900">{question.questionText}</p>

      <div className="mt-3 grid grid-cols-2 gap-2 text-sm text-ink-600">
        <Option label="A" text={question.optionA} />
        <Option label="B" text={question.optionB} />
        <Option label="C" text={question.optionC} />
        <Option label="D" text={question.optionD} />
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-1.5">
        <CalendarClock className="h-3.5 w-3.5 text-ink-400" strokeWidth={2} />
        {visibleTags.map((a) => (
          <Tag key={`${a.examId}-${a.year}`} className="bg-secondary-50 text-secondary-500">
            {a.examName} {a.year}
          </Tag>
        ))}
        {extraCount > 0 && <span className="text-xs font-semibold text-ink-400">+{extraCount} more</span>}
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

function Option({ label, text }) {
  return (
    <span className="flex items-start gap-1.5">
      <span className="font-bold text-ink-400">{label}.</span>
      <span className="min-w-0 flex-1 truncate">{text}</span>
    </span>
  );
}
