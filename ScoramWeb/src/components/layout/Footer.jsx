import { Link } from "react-router-dom";
import narayanixLogo from "../../assets/narayanix-logo.png";
import { LEGAL } from "../../config/legal";

// variant="sidebar" -- compact, sits inside the (light) student sidebar under the profile card.
// variant="sidebar-dark" -- same, but light text for the admin sidebar's dark navy background.
// variant="page" (default) -- centered, for the bottom of a full-width page layout.
export default function Footer({ variant = "page" }) {
  if (variant === "sidebar" || variant === "sidebar-dark") {
    const isDark = variant === "sidebar-dark";
    return (
      <div className={`flex items-center justify-center gap-1.5 px-3 pb-4 pt-1 text-[11px] ${isDark ? "text-primary-100/70" : "text-ink-400"}`}>
        <img src={narayanixLogo} alt="" className="h-3.5 w-3.5 object-contain" />
        A NarayaniX Product
      </div>
    );
  }

  return (
    <footer className="flex flex-col items-center gap-2 py-4 text-xs text-ink-400">
      <div className="flex items-center gap-1.5">
        <img src={narayanixLogo} alt="" className="h-4 w-4 object-contain" />
        A NarayaniX Product
      </div>
      <nav aria-label="Legal" className="flex flex-wrap items-center justify-center gap-x-4 gap-y-1">
        <Link to={LEGAL.paths.privacy} className="hover:text-primary-600 hover:underline">Privacy Policy</Link>
        <Link to={LEGAL.paths.terms} className="hover:text-primary-600 hover:underline">Terms &amp; Conditions</Link>
        <Link to={LEGAL.paths.deleteAccount} className="hover:text-primary-600 hover:underline">Delete Account</Link>
        <a href={`mailto:${LEGAL.contactEmail}`} className="hover:text-primary-600 hover:underline">Support</a>
      </nav>
    </footer>
  );
}
