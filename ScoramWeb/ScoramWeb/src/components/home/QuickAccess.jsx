import { BookOpen, ClipboardList, HelpCircle, ClipboardCheck, TrendingUp, ChevronRight } from "lucide-react";
import { Link } from "react-router-dom";
import { quickAccessItems } from "../../data/mockData";

const ICONS = { BookOpen, ClipboardList, HelpCircle, ClipboardCheck, TrendingUp };

const TINTS = {
  secondary: "bg-secondary-50 text-secondary-500",
  mint: "bg-mint-50 text-mint-500",
  violet: "bg-violet-50 text-violet-500",
  accent: "bg-accent-50 text-accent-500",
  teal: "bg-teal-50 text-teal-500",
};

// Maps each card's data-key to an actual route. "quizzes" and "progress" fall through to
// their own /key route, which renders a "Coming Soon" screen -- Exam Utility / Gamification
// genuinely aren't built yet, so those cards are honest about that rather than pretending.
const ROUTE_FOR_KEY = {
  pyq: "/search",  // Browse-by-Exam lives inside the Find PYQs page now
  mock: "/tests",  // was pointing at a non-existent "mock" tab before -- real page is "/tests"
  test: "/tests",
};

export default function QuickAccess() {
  return (
    <section className="px-4 pb-6 sm:px-6 lg:px-8">
      <div className="mb-3 flex items-center justify-between sm:mb-4">
        <h3 className="text-[17px] font-bold text-ink-900 sm:text-lg">Quick Access</h3>
        <Link
          to={ROUTE_FOR_KEY.pyq}
          className="flex items-center gap-0.5 text-sm font-semibold text-secondary-500"
        >
          View All
          <ChevronRight className="h-4 w-4" strokeWidth={2.5} />
        </Link>
      </div>

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 sm:gap-4 lg:grid-cols-5">
        {quickAccessItems.map((item) => {
          const Icon = ICONS[item.icon];
          return (
            <Link
              key={item.key}
              to={ROUTE_FOR_KEY[item.key] ?? `/${item.key}`}
              className="group flex flex-col items-start gap-3 rounded-xl2 border border-primary-100 bg-white p-4 text-left shadow-card transition-all hover:-translate-y-0.5 hover:shadow-cardHover"
            >
              <span className={`flex h-[72px] w-[72px] items-center justify-center rounded-2xl ${TINTS[item.tint]}`}>
                <Icon className="h-7 w-7" strokeWidth={2} />
              </span>
              <span>
                <span className="block text-sm font-bold leading-tight text-ink-900">{item.label}</span>
                <span className="mt-0.5 block text-xs leading-snug text-ink-400">{item.description}</span>
              </span>
            </Link>
          );
        })}
      </div>
    </section>
  );
}
