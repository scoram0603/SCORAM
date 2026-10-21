import { useState } from "react";
import { Link } from "react-router-dom";
import { ChevronDown, Pencil, Plus, Star } from "lucide-react";
import { useMyExams } from "../../context/MyExamsContext";
import ChooseMyExamsPrompt from "../exams/ChooseMyExamsPrompt";

// "MY EXAMS" -- the student's exams as chips, with one-tap access to change them. Used on Home
// (replacing the public "Popular Exams" strip for signed-in students, since showing other exams there
// would contradict My Exams being a strict scope) and on Profile. Replaces the old "Preparing for"
// widget; the data behind it is unchanged.
//
// Layout: a header row (title + count on the left, Edit on the right) and the exams as wrapping chips
// underneath. Long lists collapse to the first few chips with a "+N more" toggle so the card never
// turns into a wall of tags; every chip wraps/truncates instead of overflowing on narrow screens.
//
// Loading: skeleton. No exams (skipped / cleared): the "Choose My Exams" prompt -- never a feed of
// everything. Renders nothing for a signed-out visitor (the caller decides where this is shown).
const COLLAPSED_COUNT = 8;
const focusRing = "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary-400 focus-visible:ring-offset-1";

export default function MyExamsSection({ className = "" }) {
  const { exams, hasLoaded } = useMyExams();
  const [expanded, setExpanded] = useState(false);

  if (!hasLoaded) {
    return (
      <div className={className} aria-busy="true" aria-label="Loading My Exams">
        <div className="h-[104px] animate-pulse rounded-xl2 bg-primary-50" />
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

  // Primary exam first, then the rest in their saved order.
  const ordered = [...exams].sort((a, b) => Number(Boolean(b.isPrimary)) - Number(Boolean(a.isPrimary)));
  const collapsible = ordered.length > COLLAPSED_COUNT + 1;
  const visible = collapsible && !expanded ? ordered.slice(0, COLLAPSED_COUNT) : ordered;
  const hiddenCount = ordered.length - visible.length;

  return (
    <section className={className} aria-labelledby="home-my-exams-title">
      <div className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
        <div className="flex items-center justify-between gap-3">
          <h2 id="home-my-exams-title" className="flex items-center gap-2 text-sm font-extrabold text-ink-900">
            My Exams
            <span className="rounded-full bg-primary-50 px-2 py-0.5 text-[11px] font-bold text-primary-600">{ordered.length}</span>
          </h2>
          <Link
            to="/my-exams"
            className={`flex shrink-0 items-center gap-1.5 rounded-lg border border-primary-100 px-3 py-1.5 text-xs font-bold text-primary-600 transition-colors hover:bg-primary-50 ${focusRing}`}
          >
            <Pencil className="h-3 w-3" strokeWidth={2.5} />
            Edit
          </Link>
        </div>

        <ul className="mt-3 flex flex-wrap gap-2">
          {visible.map((exam) => (
            <li key={exam.examId} className="max-w-full">
              <span
                title={exam.examName}
                className="flex max-w-full items-center gap-1 rounded-full bg-primary-50 px-3 py-1.5 text-xs font-bold text-primary-600"
              >
                {exam.isPrimary && ordered.length > 1 && (
                  <Star className="h-3 w-3 shrink-0 text-amber-500" strokeWidth={2.5} fill="currentColor" aria-label="Primary exam" />
                )}
                <span className="truncate">{exam.examName}</span>
              </span>
            </li>
          ))}
          {collapsible && (
            <li>
              <button
                type="button"
                onClick={() => setExpanded((v) => !v)}
                aria-expanded={expanded}
                className={`flex items-center gap-1 rounded-full border border-primary-100 px-3 py-1.5 text-xs font-bold text-ink-600 hover:bg-primary-50 ${focusRing}`}
              >
                {expanded ? "Show less" : `+${hiddenCount} more`}
                <ChevronDown className={`h-3 w-3 transition-transform ${expanded ? "rotate-180" : ""}`} strokeWidth={2.5} />
              </button>
            </li>
          )}
          <li>
            <Link
              to="/my-exams"
              className={`flex items-center gap-1 rounded-full border border-dashed border-primary-400 px-3 py-1.5 text-xs font-bold text-primary-600 hover:bg-primary-50 ${focusRing}`}
            >
              <Plus className="h-3 w-3" strokeWidth={2.75} />
              Add
            </Link>
          </li>
        </ul>
      </div>
    </section>
  );
}
