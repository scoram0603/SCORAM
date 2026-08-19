import { useEffect } from "react";
import { X } from "lucide-react";

// Auto-dismisses after 5s unless the person interacts with it first.
export default function Toast({ title, body, onClick, onDismiss }) {
  useEffect(() => {
    const timer = setTimeout(onDismiss, 5000);
    return () => clearTimeout(timer);
  }, [onDismiss]);

  return (
    <div
      role="alert"
      className="pointer-events-auto flex w-[320px] max-w-[calc(100vw-32px)] items-start gap-3 rounded-xl2 border border-primary-100 bg-white p-3.5 shadow-floating"
    >
      <button
        type="button"
        onClick={() => {
          onClick?.();
          onDismiss();
        }}
        className="min-w-0 flex-1 text-left"
      >
        <p className="truncate text-sm font-bold text-ink-900">{title}</p>
        <p className="mt-0.5 line-clamp-2 text-xs text-ink-400">{body}</p>
      </button>
      <button type="button" onClick={onDismiss} aria-label="Dismiss" className="shrink-0 text-ink-400 hover:text-ink-600">
        <X className="h-4 w-4" strokeWidth={2.25} />
      </button>
    </div>
  );
}
