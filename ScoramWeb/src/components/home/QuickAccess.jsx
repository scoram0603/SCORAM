import { BookOpen, Layers, ClipboardList, HelpCircle, ClipboardCheck, MessageCircle, ChevronRight } from "lucide-react";
import { Link } from "react-router-dom";
import { quickAccessItems } from "../../data/mockData";

const ICONS = { BookOpen, Layers, ClipboardList, HelpCircle, ClipboardCheck, MessageCircle };

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
  chat: "/chat",
};

export default function QuickAccess() {
  return (
    <section className="px-4 pb-6 sm:px-6 lg:px-8">
      <div className="mb-3 flex items-center justify-between sm:mb-4">
        <h3 className="text-[17px] font-bold text-ink-900 sm:text-lg">Quick Access</h3>
        <Link to="/tests" className="flex items-center gap-0.5 text-sm font-semibold text-secondary-500">
          View All
          <ChevronRight className="h-4 w-4" strokeWidth={2.5} />
        </Link>
      </div>

      {/* Compact 2-up grid (3-up from sm) -- each card is a small icon + short title + one-line
          subtitle, sized so 6 cards fit in far less vertical space than the old 72px-icon cards. */}
      <div className="grid grid-cols-2 gap-2.5 sm:grid-cols-3 sm:gap-3 lg:grid-cols-6">
        {quickAccessItems.map((item) => {
          const Icon = ICONS[item.icon];
          return (
            <Link
              key={item.key}
              to={ROUTE_FOR_KEY[item.key] ?? `/${item.key}`}
              className="group flex items-center gap-2.5 rounded-xl2 border border-primary-100 bg-white p-3 text-left shadow-card transition-all hover:-translate-y-0.5 hover:shadow-cardHover lg:flex-col lg:items-start lg:gap-2"
            >
              <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${TINTS[item.tint]}`}>
                <Icon className="h-[18px] w-[18px]" strokeWidth={2.25} />
              </span>
              <span className="min-w-0">
                <span className="block truncate text-[13px] font-bold leading-tight text-ink-900">{item.label}</span>
                <span className="mt-0.5 block truncate text-[11px] leading-snug text-ink-400">{item.description}</span>
              </span>
            </Link>
          );
        })}
      </div>
    </section>
  );
}
