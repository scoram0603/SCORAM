import { Search, SlidersHorizontal } from "lucide-react";

export default function SearchBar({ value, onChange, onFilterClick, onSubmit }) {
  function handleKeyDown(e) {
    if (e.key === "Enter") onSubmit?.(value);
  }

  return (
    <div className="flex items-center gap-3 px-4 pb-4 lg:hidden">
      <label className="relative flex-1">
        <span className="sr-only">Search exam, subject, topic</span>
        <button
          type="button"
          onClick={() => onSubmit?.(value)}
          aria-label="Search"
          className="absolute left-4 top-1/2 -translate-y-1/2 text-ink-400"
        >
          <Search className="h-[18px] w-[18px]" strokeWidth={2} />
        </button>
        <input
          type="search"
          value={value}
          onChange={(e) => onChange?.(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Search exam, subject, topic..."
          className="h-[52px] w-full rounded-xl2 border border-primary-100 bg-white pl-11 pr-3 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500"
        />
      </label>

      <button
        type="button"
        onClick={onFilterClick}
        aria-label="Filters"
        className="flex h-[52px] w-[52px] shrink-0 items-center justify-center rounded-xl2 border border-primary-100 bg-white text-primary-600 transition-colors hover:bg-primary-50 active:bg-primary-100"
      >
        <SlidersHorizontal className="h-5 w-5" strokeWidth={2.25} />
      </button>
    </div>
  );
}
