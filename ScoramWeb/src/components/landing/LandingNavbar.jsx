import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Menu, X, ArrowRight } from "lucide-react";
import logo from "../../assets/scoram-logo-horizontal.png";
import { navLinks } from "../../data/landingContent";

function NavItem({ item, onClick, className }) {
  if (item.to) {
    return (
      <Link to={item.to} onClick={onClick} className={className}>
        {item.label}
      </Link>
    );
  }
  return (
    <a href={item.href} onClick={onClick} className={className}>
      {item.label}
    </a>
  );
}

export default function LandingNavbar() {
  const [scrolled, setScrolled] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 8);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  // Lock body scroll while the mobile drawer is open.
  useEffect(() => {
    document.body.style.overflow = drawerOpen ? "hidden" : "";
    return () => {
      document.body.style.overflow = "";
    };
  }, [drawerOpen]);

  return (
    <header
      id="top"
      className={`sticky top-0 z-40 transition-all ${
        scrolled ? "bg-white/95 shadow-card backdrop-blur" : "bg-white/70 backdrop-blur-sm"
      }`}
    >
      <nav className="mx-auto flex h-16 max-w-7xl items-center justify-between px-4 sm:px-6 lg:h-20 lg:px-8" aria-label="Main">
        <a href="#top" className="flex shrink-0 items-center">
          <img src={logo} alt="SCORAM — Learn, Discuss, Score" className="h-9 w-auto lg:h-11" />
        </a>

        <div className="hidden items-center gap-7 xl:flex">
          {navLinks.map((item) => (
            <NavItem
              key={item.label}
              item={item}
              className="text-[15px] font-medium text-ink-600 transition-colors hover:text-primary-600"
            />
          ))}
        </div>

        <div className="hidden items-center gap-3 lg:flex">
          <Link
            to="/login"
            className="rounded-xl px-4 py-2.5 text-sm font-semibold text-primary-600 transition-colors hover:bg-primary-50"
          >
            Login
          </Link>
          <Link
            to="/login?mode=register"
            className="flex items-center gap-1.5 rounded-xl bg-primary-600 px-5 py-2.5 text-sm font-semibold text-white shadow-floating transition-colors hover:bg-primary-700"
          >
            Sign Up
            <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
          </Link>
        </div>

        <button
          type="button"
          onClick={() => setDrawerOpen(true)}
          aria-label="Open menu"
          className="flex h-10 w-10 items-center justify-center rounded-xl border border-primary-100 text-primary-600 lg:hidden"
        >
          <Menu className="h-5 w-5" strokeWidth={2.25} />
        </button>
      </nav>

      {/* Mobile drawer */}
      <div
        className={`fixed inset-0 z-50 transition-opacity lg:hidden ${
          drawerOpen ? "pointer-events-auto opacity-100" : "pointer-events-none opacity-0"
        }`}
      >
        <div
          className="absolute inset-0 bg-primary-900/50 backdrop-blur-sm"
          onClick={() => setDrawerOpen(false)}
        />
        <div
          className={`absolute right-0 top-0 flex h-full w-[82%] max-w-sm flex-col bg-white shadow-cardHover transition-transform duration-300 ${
            drawerOpen ? "translate-x-0" : "translate-x-full"
          }`}
        >
          <div className="flex items-center justify-between border-b border-primary-100 px-5 py-4">
            <img src={logo} alt="SCORAM" className="h-8 w-auto" />
            <button
              type="button"
              onClick={() => setDrawerOpen(false)}
              aria-label="Close menu"
              className="flex h-9 w-9 items-center justify-center rounded-xl text-ink-600 hover:bg-surface"
            >
              <X className="h-5 w-5" strokeWidth={2.25} />
            </button>
          </div>

          <div className="flex flex-1 flex-col gap-1 overflow-y-auto px-3 py-4">
            {navLinks.map((item) => (
              <NavItem
                key={item.label}
                item={item}
                onClick={() => setDrawerOpen(false)}
                className="rounded-xl px-3 py-3 text-[15px] font-medium text-ink-900 hover:bg-surface"
              />
            ))}
          </div>

          <div className="flex flex-col gap-2 border-t border-primary-100 p-4 safe-bottom">
            <Link
              to="/login"
              onClick={() => setDrawerOpen(false)}
              className="rounded-xl border border-primary-100 px-4 py-3 text-center text-sm font-semibold text-primary-600"
            >
              Login
            </Link>
            <Link
              to="/login?mode=register"
              onClick={() => setDrawerOpen(false)}
              className="rounded-xl bg-primary-600 px-4 py-3 text-center text-sm font-semibold text-white"
            >
              Sign Up
            </Link>
          </div>
        </div>
      </div>
    </header>
  );
}
