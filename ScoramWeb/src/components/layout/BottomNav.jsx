import { NavLink } from "react-router-dom";
import { Home, Search, BarChart3, MessageCircle, User } from "lucide-react";

const ICONS = { Home, Search, BarChart3, MessageCircle, User };

// "Ask" (a floating CTA button that broke the tab rhythm) has been removed -- every entry here is
// now a plain tab, so the isCta/floating-button branch that used to live in this component is gone
// too. Discussions/"Ask a question" is still fully reachable, just not from the bottom nav (see
// Sidebar/MobileDrawer's Community section, and the "Ask" entry point already inside Discussions
// itself) -- per the brief, only the bottom-nav placement was to be removed.
export default function BottomNav({ items }) {
  return (
    <nav className="safe-bottom sticky bottom-0 z-30 border-t border-primary-100 bg-white/95 backdrop-blur supports-[backdrop-filter]:bg-white/85 lg:hidden">
      <ul className="flex items-stretch justify-between px-2">
        {items.map((item) => {
          const Icon = ICONS[item.icon];

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