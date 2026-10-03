import { useState } from "react";
import { MessageSquareText } from "lucide-react";
import FeedbackModal from "./FeedbackModal";

// Floating Feedback button -- bottom-right on Home. Sits above the mobile bottom navigation and the
// device safe area (bottom: nav height + env(safe-area-inset-bottom)), and drops to the corner on
// desktop where there is no bottom nav. Icon-only with a "Feedback" label that appears on hover/
// focus (and as the accessible name) -- no text button.
export default function FeedbackButton() {
  const [open, setOpen] = useState(false);

  return (
    <>
      <div
        className="group fixed right-4 z-40 lg:right-6"
        style={{ bottom: "calc(env(safe-area-inset-bottom, 0px) + 5.25rem)" }}
      >
        <span
          role="tooltip"
          className="pointer-events-none absolute right-full top-1/2 mr-2 -translate-y-1/2 whitespace-nowrap rounded-lg bg-ink-900 px-2.5 py-1 text-xs font-semibold text-white opacity-0 shadow-card transition-opacity group-hover:opacity-100 group-focus-within:opacity-100"
        >
          Feedback
        </span>
        <button
          type="button"
          onClick={() => setOpen(true)}
          aria-label="Feedback"
          aria-haspopup="dialog"
          title="Feedback"
          className="flex h-12 w-12 items-center justify-center rounded-full bg-primary-600 text-white shadow-cardHover transition-transform hover:scale-105 hover:bg-primary-700 focus:outline-none focus-visible:ring-2 focus-visible:ring-secondary-500 focus-visible:ring-offset-2 lg:h-14 lg:w-14"
        >
          <MessageSquareText className="h-5 w-5 lg:h-6 lg:w-6" strokeWidth={2.25} />
        </button>
      </div>

      {open && <FeedbackModal source="Home" onClose={() => setOpen(false)} />}
    </>
  );
}
