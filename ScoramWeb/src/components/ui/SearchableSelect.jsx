import { useEffect, useMemo, useRef, useState } from "react";
import { Check, ChevronDown, Search, X } from "lucide-react";

// Reusable searchable dropdown -- replaces a plain <select> "Any subject"/"Any exam" style filter
// with one that has its own search box (type to filter the option list) and, in multi={true} mode,
// lets more than one value be picked at once (checkboxes, OR-matched on the backend -- see
// QuestionBankController.Search). Single-select mode (multi={false}) still gets the search box, it
// just closes and applies immediately on pick instead of needing an explicit "Done".
//
// options: [{ value, label }]. selected: an array of currently-picked values (even in single mode,
// for a consistent prop shape -- callers just read selected[0] there). onChange receives the new
// array.
export default function SearchableSelect({
  label,
  placeholder = "Any",
  options = [],
  selected = [],
  onChange,
  disabled = false,
  multi = true,
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const rootRef = useRef(null);
  const searchRef = useRef(null);

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

  useEffect(() => {
    if (open) {
      setQuery("");
      // Focus after the panel actually mounts, not the click that opened it.
      requestAnimationFrame(() => searchRef.current?.focus());
    }
  }, [open]);

  const filteredOptions = useMemo(() => {
    const term = query.trim().toLowerCase();
    if (!term) return options;
    return options.filter((o) => String(o.label).toLowerCase().includes(term));
  }, [options, query]);

  const selectedSet = useMemo(() => new Set(selected.map(String)), [selected]);

  function toggleValue(value) {
    const strValue = String(value);
    if (multi) {
      if (selectedSet.has(strValue)) onChange(selected.filter((v) => String(v) !== strValue));
      else onChange([...selected, value]);
    } else {
      onChange(selectedSet.has(strValue) ? [] : [value]);
      setOpen(false);
    }
  }

  function clearAll(e) {
    e.stopPropagation();
    onChange([]);
  }

  const selectedLabels = options.filter((o) => selectedSet.has(String(o.value))).map((o) => o.label);
  const summary =
    selectedLabels.length === 0
      ? placeholder
      : selectedLabels.length === 1
        ? selectedLabels[0]
        : `${selectedLabels.length} selected`;

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
        <span className={`truncate ${selectedLabels.length === 0 ? "text-ink-400" : "font-medium"}`}>{summary}</span>
        <span className="flex shrink-0 items-center gap-1">
          {selectedLabels.length > 0 && !disabled && (
            <X
              className="h-3.5 w-3.5 text-ink-300 hover:text-ink-500"
              strokeWidth={2.25}
              onClick={clearAll}
              role="button"
              aria-label={`Clear ${label || "filter"}`}
            />
          )}
          <ChevronDown className="h-4 w-4 text-ink-400" strokeWidth={2} />
        </span>
      </button>

      {open && !disabled && (
        <div className="absolute z-20 mt-1.5 w-full min-w-[220px] overflow-hidden rounded-xl2 border border-primary-100 bg-white shadow-cardHover">
          <label className="relative block border-b border-primary-50 p-2">
            <Search className="pointer-events-none absolute left-4 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-ink-300" strokeWidth={2.25} />
            <input
              ref={searchRef}
              type="text"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder={`Search ${label ? label.toLowerCase() : "options"}...`}
              className="w-full rounded-lg bg-primary-50 py-1.5 pl-7 pr-2 text-xs text-ink-900 placeholder:text-ink-400 focus:outline-none"
            />
          </label>
          <div className="max-h-56 overflow-y-auto py-1">
            {filteredOptions.length === 0 && <p className="px-3 py-3 text-center text-xs text-ink-400">No matches.</p>}
            {filteredOptions.map((option) => {
              const isSelected = selectedSet.has(String(option.value));
              return (
                <button
                  key={option.value}
                  type="button"
                  onClick={() => toggleValue(option.value)}
                  className={`flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-primary-50 ${
                    isSelected ? "text-primary-700 font-semibold" : "text-ink-800"
                  }`}
                >
                  {multi ? (
                    <span
                      className={`flex h-4 w-4 shrink-0 items-center justify-center rounded border ${
                        isSelected ? "border-primary-600 bg-primary-600" : "border-primary-200 bg-white"
                      }`}
                    >
                      {isSelected && <Check className="h-3 w-3 text-white" strokeWidth={3} />}
                    </span>
                  ) : (
                    <span className={`h-1.5 w-1.5 shrink-0 rounded-full ${isSelected ? "bg-primary-600" : "bg-transparent"}`} />
                  )}
                  <span className="truncate">{option.label}</span>
                </button>
              );
            })}
          </div>
          {multi && selectedLabels.length > 0 && (
            <div className="flex items-center justify-between border-t border-primary-50 px-3 py-1.5">
              <button type="button" onClick={() => onChange([])} className="text-[11px] font-semibold text-secondary-500 hover:text-secondary-600">
                Clear
              </button>
              <button type="button" onClick={() => setOpen(false)} className="text-[11px] font-semibold text-primary-600 hover:text-primary-700">
                Done ({selectedLabels.length})
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
