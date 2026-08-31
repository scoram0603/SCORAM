import { Star } from "lucide-react";
import { Link } from "react-router-dom";
import { useMyExams } from "../../context/MyExamsContext";

// "MY EXAMS" -- Home screen context indicator (spec section 11): shows what the student's
// preparation is currently defaulted to, with a one-tap way out to change it. Renders nothing for
// a signed-out visitor, a student who hasn't configured My Exams yet (AppLayout already routes
// them to onboarding before they'd ever see Home in that state), or while still loading.
export default function PreparingFor() {
  const { exams, hasLoaded } = useMyExams();

  if (!hasLoaded || exams.length === 0) return null;

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
