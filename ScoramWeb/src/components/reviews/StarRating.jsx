import { useId, useState } from "react";
import { Star } from "lucide-react";
import { RATING_LABELS } from "../../utils/reviews";

// Read-only stars (landing cards, admin tables).
export function StarDisplay({ rating, className = "h-4 w-4" }) {
  return (
    <span className="inline-flex items-center gap-0.5 text-amber-400" role="img" aria-label={`${rating} out of 5 stars`}>
      {[1, 2, 3, 4, 5].map((n) => (
        <Star key={n} className={className} strokeWidth={2} fill={n <= rating ? "currentColor" : "none"} aria-hidden="true" />
      ))}
    </span>
  );
}

// Accessible rating input: a native radio group (arrow keys move + select, Tab enters/leaves, labelled for
// screen readers), with the stars as the visible labels and a visible focus ring.
export default function StarRatingInput({ value, onChange, disabled = false, invalid = false, describedBy }) {
  const name = useId();
  const [hover, setHover] = useState(0);
  const shown = hover || value || 0;

  return (
    <div>
      <div
        role="radiogroup"
        aria-label="Your rating out of 5 stars"
        aria-invalid={invalid || undefined}
        aria-describedby={describedBy}
        className="flex items-center gap-1"
        onMouseLeave={() => setHover(0)}
      >
        {[1, 2, 3, 4, 5].map((n) => (
          <label
            key={n}
            className={`relative cursor-pointer rounded-md p-1 text-amber-400 focus-within:ring-2 focus-within:ring-secondary-500 ${disabled ? "cursor-not-allowed opacity-60" : ""}`}
            onMouseEnter={() => !disabled && setHover(n)}
          >
            <input
              type="radio"
              name={name}
              value={n}
              checked={value === n}
              disabled={disabled}
              onChange={() => onChange(n)}
              className="sr-only"
              aria-label={`${n} star${n > 1 ? "s" : ""}, ${RATING_LABELS[n]}`}
            />
            <Star className="h-9 w-9 transition-transform hover:scale-110" strokeWidth={1.75} fill={n <= shown ? "currentColor" : "none"} aria-hidden="true" />
          </label>
        ))}
      </div>
      <p className="mt-1 h-5 text-sm font-semibold text-ink-600" aria-live="polite">
        {shown ? `${shown} / 5 — ${RATING_LABELS[shown]}` : "Tap a star to rate"}
      </p>
    </div>
  );
}
