import { Star, Target } from "lucide-react";
import { Link } from "react-router-dom";
import { useMyExams } from "../../context/MyExamsContext";

// "MY EXAMS" -- Home screen context indicator (spec section 11): shows what the student's
// preparation is currently defaulted to, with a one-tap way out to change it. Renders nothing for
// a signed-out visitor or while still loading. A student who explicitly skipped onboarding
// (MyExamsContext's `skipped`) lands here with hasLoaded=true and zero exams -- rather than
// showing nothing, prompt them to personalize, same as a fresh visitor would see on /select-exams.
export default function PreparingFor() {
  const { exams, hasLoaded } = useMyExams();

  if (!hasLoaded) return null;

  if (exams.length === 0) {
    return (
      <section className="px-4 pb-4 sm:px-0">
        <div className="flex flex-wrap items-center gap-3 rounded-xl2 border border-dashed border-primary-200 bg-primary-50/40 p-3.5 shadow-card sm:p-4">
          <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary-100 text-primary-600">
            <Target className="h-[18px] w-[18px]" strokeWidth={2.25} />
          </span>
          <p className="min-w-0 flex-1 text-sm font-medium text-ink-600">
            Choose your target exams to personalize SCORAM
          </p>
          <Link
            to="/select-exams"
            className="shrink-0 rounded-lg bg-primary-600 px-3 py-1.5 text-xs font-bold text-white hover:bg-primary-700"
          >
            Select Exams
          </Link>
        </div>
      </section>
    );
  }

  return (
    <section className="px-4 pb-4 sm:px-0">
      <div className="flex flex-wrap items-center gap-2 rounded-xl2 border border-primary-100 bg-white p-3.5 shadow-card sm:p-4">
        <span className="shrink-0 text-xs font-semibold uppercase tracking-wide text-ink-400">Preparing for</span>
        <div className="flex flex-1 flex-wrap gap-1.5">
          {exams.map((exam) => (
            <span
              key={exam.examId}
              className="flex items-center gap-1 rounded-full bg-primary-50 px-2.5 py-1 text-xs font-bold text-primary-600"
            >
              {exam.isPrimary && <Star className="h-3 w-3" strokeWidth={2.5} fill="currentColor" />}
              {exam.examName}
            </span>
          ))}
        </div>
        <Link to="/my-exams" className="shrink-0 text-xs font-bold text-secondary-500 hover:text-secondary-600">
          Edit
        </Link>
      </div>
    </section>
  );
}
