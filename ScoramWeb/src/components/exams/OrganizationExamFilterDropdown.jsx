import { useEffect, useRef, useState } from "react";
import { ChevronDown, X } from "lucide-react";
import OrganizationExamPicker from "./OrganizationExamPicker";

// Same trigger-button + popover shell as SearchableSelect (ui/SearchableSelect.jsx) -- so this
// drops into the same filter-bar slots and looks identical at rest -- but the panel content is
// OrganizationExamPicker's "pick an Organization, then pick from its exams" instead of one flat
// searchable list. A dedicated component rather than a mode on SearchableSelect itself: that one is
// generic across many different option types (subjects, years, ...), and organization-grouping is
// specific to exams.
//
// The picker panel is always mounted (just hidden via CSS when closed, not conditionally rendered)
// so its exam list loads immediately on page load -- otherwise the trigger button's own summary
// text (e.g. showing an already-selected exam's name) would have nothing to resolve names from
// until the student actually opened the dropdown once.
export default function OrganizationExamFilterDropdown({
  label = "Exam",
  placeholder = "All exams",
  selected = [],
  onChange,
  disabled = false,
}) {
  const [open, setOpen] = useState(false);
  const [examNames, setExamNames] = useState({}); // examId -> examName, for the trigger summary
  const rootRef = useRef(null);

  useEffect(() => {
    if (!open) return;
    function handleClickOutside(e) {
      if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false);
    }
    function handleEscape(e) {
      if (e.key === "Escape") setOpen(false);
    }
    document.addEventListener("mousedown", handleClickOutside);
    document.addEventListener("keydown", handleEscape);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
      document.removeEventListener("keydown", handleEscape);
    };
  }, [open]);

  function handleToggle(examId, examName) {
    setExamNames((prev) => ({ ...prev, [examId]: examName }));
    const isSelected = selected.includes(examId);
    onChange(isSelected ? selected.filter((id) => id !== examId) : [...selected, examId]);
  }

  function handleExamsLoaded(allExams) {
    setExamNames((prev) => {
      const next = { ...prev };
      for (const exam of allExams) next[exam.id] = exam.name;
      return next;
    });
  }

  const summary =
    selected.length === 0
      ? placeholder
      : selected.length === 1
        ? examNames[selected[0]] || "1 selected"
        : `${selected.length} selected`;

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
        <span className={`truncate ${selected.length === 0 ? "text-ink-400" : "font-medium"}`}>{summary}</span>
        <span className="flex shrink-0 items-center gap-1">
          {selected.length > 0 && !disabled && (
            <X
              className="h-3.5 w-3.5 text-ink-300 hover:text-ink-500"
              strokeWidth={2.25}
              onClick={(e) => { e.stopPropagation(); onChange([]); }}
              role="button"
              aria-label={`Clear ${label || "filter"}`}
            />
          )}
          <ChevronDown className="h-4 w-4 text-ink-400" strokeWidth={2} />
        </span>
      </button>

      <div
        className={`absolute z-20 mt-1.5 w-80 max-w-[90vw] overflow-hidden rounded-xl2 border border-primary-100 bg-white shadow-cardHover ${
          open && !disabled ? "block" : "hidden"
        }`}
      >
        <div className="max-h-80 overflow-y-auto p-2">
          <OrganizationExamPicker
            selectedIds={selected}
            onToggle={handleToggle}
            onExamsLoaded={handleExamsLoaded}
          />
        </div>
        {selected.length > 0 && (
          <div className="flex items-center justify-between border-t border-primary-50 px-3 py-1.5">
            <button type="button" onClick={() => onChange([])} className="text-[11px] font-semibold text-secondary-500 hover:text-secondary-600">
              Clear
            </button>
            <button type="button" onClick={() => setOpen(false)} className="text-[11px] font-semibold text-primary-600 hover:text-primary-700">
              Done ({selected.length})
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
