import { Link } from "react-router-dom";
import { Target } from "lucide-react";

// "MY EXAMS" empty state -- shown wherever exam-specific content would be, for a signed-in student
// who hasn't chosen any exams (skipped, or removed them all). Deliberately NOT a mixed feed of every
// exam: with no My Exams there is nothing to personalize, so we ask instead of guessing.
//
// `message` lets a screen say what the student is missing ("...to see relevant PYP papers.").
// `compact` is the slim inline variant used inside Home's cards.
export default function ChooseMyExamsPrompt({
  title = "My Exams are not selected yet.",
  message = "Choose your exams to personalize your SCORAM content.",
  compact = false,
}) {
  return (
    <div
      className={`flex flex-col items-center rounded-xl2 border border-dashed border-primary-100 bg-primary-50/40 text-center ${
        compact ? "gap-2 p-4" : "gap-3 px-6 py-10"
      }`}
    >
      <span className="flex h-11 w-11 items-center justify-center rounded-full bg-primary-100 text-primary-600">
        <Target className="h-5 w-5" strokeWidth={2.25} />
      </span>
      <div>
        <p className="text-sm font-bold text-ink-900">{title}</p>
        <p className="mt-0.5 text-sm text-ink-600">{message}</p>
      </div>
      <Link
        to="/my-exams"
        className="rounded-xl2 bg-primary-600 px-4 py-2 text-sm font-bold text-white transition-colors hover:bg-primary-700"
      >
        Choose My Exams
      </Link>
    </div>
  );
}
