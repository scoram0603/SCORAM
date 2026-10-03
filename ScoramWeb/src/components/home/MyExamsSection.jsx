import { Link } from "react-router-dom";
import { Plus, Star } from "lucide-react";
import { useMyExams } from "../../context/MyExamsContext";
import ChooseMyExamsPrompt from "../exams/ChooseMyExamsPrompt";

// "MY EXAMS" -- the student's exams as chips, with one-tap access to change them. Used on Home
// (replacing the public "Popular Exams" strip for signed-in students, since showing other exams there
// would contradict My Exams being a strict scope) and on Profile. Replaces the old "Preparing for"
// widget; the data behind it is unchanged.
//
// Loading: skeleton. No exams (skipped / cleared): the "Choose My Exams" prompt -- never a feed of
// everything. Renders nothing for a signed-out visitor (the caller decides where this is shown).
export default function MyExamsSection({ className = "" }) {
  const { exams, hasLoaded } = useMyExams();

  if (!hasLoaded) {
    return (
      <div className={className} aria-busy="true" aria-label="Loading My Exams">
        <div className="h-[68px] animate-pulse rounded-xl2 bg-primary-50" />
      </div>
    );
  }

  if (exams.length === 0) {
    return (
      <div className={className}>
        <ChooseMyExamsPrompt compact message="Select your exams to personalize your SCORAM experience." />
      </div>
    );
  }

  return (
    <div className={className}>
      <div className="flex flex-wrap items-center gap-2 rounded-xl2 border border-primary-100 bg-white p-3.5 shadow-card">
        <span className="shrink-0 text-xs font-semibold uppercase tracking-wide text-ink-400">My Exams</span>
        <div className="flex min-w-0 flex-1 flex-wrap gap-1.5">
          {exams.map((exam) => (
            <span key={exam.examId} className="flex items-center gap-1 rounded-full bg-primary-50 px-2.5 py-1 text-xs font-bold text-primary-600">
              {exam.isPrimary && exams.length > 1 && <Star className="h-3 w-3" strokeWidth={2.5} fill="currentColor" />}
              {exam.examName}
            </span>
          ))}
          <Link
            to="/my-exams"
            className="flex items-center gap-0.5 rounded-full border border-dashed border-primary-400 px-2.5 py-1 text-xs font-bold text-primary-600 hover:bg-primary-50"
          >
            <Plus className="h-3 w-3" strokeWidth={2.75} />
            Add
          </Link>
        </div>
        <Link to="/my-exams" className="shrink-0 text-xs font-bold text-secondary-500 hover:text-secondary-600">
          Edit
        </Link>
      </div>
    </div>
  );
}
