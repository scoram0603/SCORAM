import { BookOpen, Layers, ClipboardList, HelpCircle, ClipboardCheck, ChevronRight } from "lucide-react";
import { Link } from "react-router-dom";
import { quickAccessItems } from "../../data/mockData";

const ICONS = { BookOpen, Layers, ClipboardList, HelpCircle, ClipboardCheck };

const TINTS = {
  secondary: "bg-secondary-50 text-secondary-500",
  mint: "bg-mint-50 text-mint-500",
  violet: "bg-violet-50 text-violet-500",
  accent: "bg-accent-50 text-accent-500",
  teal: "bg-teal-50 text-teal-500",
};

// Maps each card's data-key to its real, distinct destination -- see Section 14 of the redesign
// brief ("make the difference between these sections clear"). Previously "pyq" pointed at /search
// ("Find PYQs" -- individual-question search) even though the card was meant to be real Previous
// Year Paper practice; that page exists at /pyq (pages/PreviousYearPapers.jsx) and is used now.
// "mock"/"test" go straight to their distinct list pages instead of both landing on the generic
// /tests hub.
const ROUTE_FOR_KEY = {
  pyq: "/pyq",
  "question-bank": "/question-bank",
  mock: "/tests/mock",
  test: "/tests/practice",
  quizzes: "/quizzes",
};

export default function QuickAccess() {
  return (
    <section className="pb-6">
      <div className="mb-3 flex items-center justify-between px-4 sm:mb-4 sm:px-6 lg:px-8">
        <h3 className="text-[17px] font-bold text-ink-900 sm:text-lg">Quick Access</h3>
        <Link to="/tests" className="flex items-center gap-0.5 text-sm font-semibold text-secondary-500">
          View All
          <ChevronRight className="h-4 w-4" strokeWidth={2.5} />
        </Link>
      </div>

      {/* Slidable row (matches Popular Exams' pattern) so it never has to wrap into extra rows on
          a narrow screen -- just swipe. Each card has a fixed width, which is what actually fixes
          the desktop text-overlap bug: the old grid used `lg:items-start`, which let flex children
          size to their own content instead of stretching to the card's width, so `truncate` had
          nothing to clip against and the subtitle rendered past the card edge into the next one.
          A fixed-width card sidesteps that ambiguity entirely.
          Mobile: icon + name only, no subtitle (compact, swipeable icon row).
          Desktop (lg+): icon + name + one-line subtitle, as before -- just no longer overlapping. */}
      <div className="no-scrollbar flex gap-2.5 overflow-x-auto px-4 pb-1 sm:gap-3 sm:px-6 lg:px-8">
        {quickAccessItems.map((item) => {
          const Icon = ICONS[item.icon];
          return (
            <Link
              key={item.key}
              to={ROUTE_FOR_KEY[item.key] ?? `/${item.key}`}
              className="flex w-[88px] shrink-0 flex-col items-center gap-2 rounded-xl2 border border-primary-100 bg-white p-3 text-center shadow-card transition-all hover:-translate-y-0.5 hover:shadow-cardHover lg:w-[212px] lg:flex-row lg:items-center lg:gap-3 lg:p-4 lg:text-left"
            >
              <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${TINTS[item.tint]}`}>
                <Icon className="h-[18px] w-[18px]" strokeWidth={2.25} />
              </span>
              <span className="w-full min-w-0 lg:w-auto lg:flex-1">
                <span className="block truncate text-[12px] font-bold leading-tight text-ink-900 lg:text-[13px]">
                  {item.label}
                </span>
                <span className="mt-0.5 hidden truncate text-[11px] leading-snug text-ink-400 lg:block">
                  {item.description}
                </span>
              </span>
            </Link>
          );
        })}
      </div>
    </section>
  );
}