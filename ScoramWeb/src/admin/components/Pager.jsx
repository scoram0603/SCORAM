import { ChevronLeft, ChevronRight } from "lucide-react";

export default function Pager({ page, pageSize, totalCount, onPage }) {
  const totalPages = Math.max(1, Math.ceil((totalCount || 0) / pageSize));
  if (totalPages <= 1) return null;
  return (
    <div className="flex items-center justify-between gap-3 pt-3 text-xs text-ink-600">
      <span>Page {page} of {totalPages} · {totalCount.toLocaleString("en-IN")} total</span>
      <span className="flex gap-1">
        <button type="button" disabled={page <= 1} onClick={() => onPage(page - 1)} aria-label="Previous page"
          className="rounded-lg bg-primary-50 p-1.5 text-primary-600 hover:bg-primary-100 disabled:opacity-40"><ChevronLeft className="h-4 w-4" /></button>
        <button type="button" disabled={page >= totalPages} onClick={() => onPage(page + 1)} aria-label="Next page"
          className="rounded-lg bg-primary-50 p-1.5 text-primary-600 hover:bg-primary-100 disabled:opacity-40"><ChevronRight className="h-4 w-4" /></button>
      </span>
    </div>
  );
}
