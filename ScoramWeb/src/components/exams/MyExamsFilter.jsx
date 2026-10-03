import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Check, ChevronDown, X } from "lucide-react";
import { useAuth } from "../../context/AuthContext";
import { useMyExams } from "../../context/MyExamsContext";
import OrganizationExamFilterDropdown from "./OrganizationExamFilterDropdown";

// "MY EXAMS" -- the exam filter used on PYP / Question Bank / Mock Tests / Practice.
//
// For a signed-in student it ONLY ever offers their own exams. There is deliberately no "All
// Exams" / "Other Exams" / browse-everything option: My Exams is a strict scope, enforced by the API
// too, and changing it happens in My Exams (the footer link below), never per screen.
//   * 1 exam  -> a static badge (nothing to choose between).
//   * 2+ exams -> a dropdown that can NARROW to some of them. `selected = []` means "all of my
//     exams" -- the whole scope, not every exam in the catalog.
// A signed-out visitor isn't scoped and keeps the old public exam picker.
//
// Same props as OrganizationExamFilterDropdown so it drops into the same filter-bar slots.
export default function MyExamsFilter({ label = "Exam", selected = [], onChange, disabled = false }) {
  const { isAuthenticated } = useAuth();
  const { exams, hasLoaded } = useMyExams();
  const [open, setOpen] = useState(false);
  const rootRef = useRef(null);

  useEffect(() => {
    if (!open) return;
    function onDown(e) {
      if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false);
    }
    function onKey(e) {
      if (e.key === "Escape") setOpen(false);
    }
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  if (!isAuthenticated) {
    return (
      <OrganizationExamFilterDropdown label={label} placeholder="All exams" selected={selected} onChange={onChange} disabled={disabled} />
    );
  }

  if (!hasLoaded || exams.length === 0) return null;

  if (exams.length === 1) {
    return (
      <div>
        {label && <span className="mb-1 block text-xs font-semibold text-ink-600">{label}</span>}
        <div className="flex h-11 w-full items-center rounded-xl2 border border-primary-100 bg-primary-50/50 px-3 text-sm font-semibold text-primary-700">
          <span className="truncate">{exams[0].examName}</span>
        </div>
      </div>
    );
  }

  const selectedSet = new Set(selected);
  const summary =
    selected.length === 0
      ? "All my exams"
      : selected.length === 1
        ? exams.find((e) => e.examId === selected[0])?.examName || "1 selected"
        : `${selected.length} of ${exams.length} exams`;

  function toggle(examId) {
    onChange(selectedSet.has(examId) ? selected.filter((id) => id !== examId) : [...selected, examId]);
  }

  return (
    <div className="relative" ref={rootRef}>
      {label && <span className="mb-1 block text-xs font-semibold text-ink-600">{label}</span>}
      <button
        type="button"
        disabled={disabled}
        onClick={() => !disabled && setOpen((v) => !v)}
        className={`flex h-11 w-full items-center justify-between gap-1.5 rounded-xl2 border px-3 text-left text-sm transition-colors ${
          open ? "border-secondary-500" : "border-primary-100"
        } ${disabled ? "bg-primary-50 text-ink-400" : "bg-white text-ink-900"}`}
      >
        <span className="truncate font-medium">{summary}</span>
        <span className="flex shrink-0 items-center gap-1">
          {selected.length > 0 && !disabled && (
            <X
              className="h-3.5 w-3.5 text-ink-400 hover:text-ink-600"
              strokeWidth={2.25}
              role="button"
              aria-label="Show all my exams"
              onClick={(e) => { e.stopPropagation(); onChange([]); }}
            />
          )}
          <ChevronDown className="h-4 w-4 text-ink-400" strokeWidth={2} />
        </span>
      </button>

      {open && !disabled && (
        <div className="absolute z-20 mt-1.5 w-64 max-w-[90vw] overflow-hidden rounded-xl2 border border-primary-100 bg-white shadow-cardHover">
          <div className="max-h-72 overflow-y-auto p-1.5">
            {exams.map((exam) => {
              const isSel = selectedSet.has(exam.examId);
              return (
                <button
                  key={exam.examId}
                  type="button"
                  onClick={() => toggle(exam.examId)}
                  className={`flex w-full items-center gap-2.5 rounded-xl px-2.5 py-2 text-left transition-colors ${isSel ? "bg-primary-50" : "hover:bg-surface"}`}
                >
                  <span className={`flex h-5 w-5 shrink-0 items-center justify-center rounded-md border-2 ${isSel ? "border-primary-600 bg-primary-600 text-white" : "border-ink-400"}`}>
                    {isSel && <Check className="h-3.5 w-3.5" strokeWidth={3} />}
                  </span>
                  <span className="min-w-0 flex-1 truncate text-sm font-semibold text-ink-900">{exam.examName}</span>
                </button>
              );
            })}
          </div>
          <div className="flex items-center justify-between border-t border-primary-50 px-3 py-1.5">
            <Link to="/my-exams" onClick={() => setOpen(false)} className="text-[11px] font-semibold text-secondary-500 hover:text-secondary-600">Update My Exams</Link>
            <button type="button" onClick={() => setOpen(false)} className="text-[11px] font-semibold text-primary-600 hover:text-primary-700">Done</button>
          </div>
        </div>
      )}
    </div>
  );
}
