import { NavLink } from "react-router-dom";
import { Home, Search, HelpCircle, ClipboardCheck, User } from "lucide-react";

const ICONS = { Home, Search, HelpCircle, ClipboardCheck, User };

export default function BottomNav({ items, onAskClick }) {
  return (
    <nav className="safe-bottom sticky bottom-0 z-30 border-t border-primary-100 bg-white/95 backdrop-blur supports-[backdrop-filter]:bg-white/85 lg:hidden">
      <ul className="flex items-stretch justify-between px-2">
        {items.map((item) => {
          const Icon = ICONS[item.icon];

          if (item.isCta) {
            return (
              <li key={item.key} className="flex flex-1 flex-col items-center justify-start">
                <button
                  type="button"
                  onClick={onAskClick}
                  aria-label={item.label}
                  className="-mt-6 flex h-14 w-14 items-center justify-center rounded-full bg-accent-500 text-white shadow-floating transition-transform hover:bg-accent-600 active:scale-95"
                >
                  <Icon className="h-7 w-7" strokeWidth={2.5} />
                </button>
                <span className="mt-1 text-[11px] font-semibold text-accent-600">{item.label}</span>
              </li>
            );
          }

          return (
            <li key={item.to} className="flex flex-1">
              <NavLink
                to={item.to}
                end={item.to === "/"}
                className={({ isActive }) =>
                  `flex flex-1 flex-col items-center gap-1 py-2.5 text-[11px] font-medium transition-colors ${
                    isActive ? "text-secondary-500" : "text-ink-400"
                  }`
                }
              >
                {({ isActive }) => (
                  <>
                    <Icon className="h-5 w-5" strokeWidth={isActive ? 2.5 : 2} />
                    {item.label}
                  </>
                )}
              </NavLink>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
