import { Menu } from "lucide-react";
import logo from "../../assets/scoram-logo-horizontal.png";
import NotificationBell from "./NotificationBell";

export default function Header({ onMenuClick, isAuthenticated }) {
  return (
    <header className="sticky top-0 z-30 flex items-center justify-between gap-3 bg-white/95 px-4 pb-3 pt-4 backdrop-blur supports-[backdrop-filter]:bg-white/80 lg:hidden">
      <button
        type="button"
        onClick={onMenuClick}
        aria-label="Open menu"
        className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-primary-600 transition-colors hover:bg-primary-50 active:bg-primary-100"
      >
        <Menu className="h-6 w-6" strokeWidth={2.25} />
      </button>

      <img src={logo} alt="Scoram — Learn, Discuss, Score" className="h-8 w-auto shrink-0 object-contain sm:h-9" />

      {isAuthenticated ? <NotificationBell variant="mobile" /> : <span className="h-10 w-10 shrink-0" />}
    </header>
  );
}
