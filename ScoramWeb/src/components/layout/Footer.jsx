import narayanixLogo from "../../assets/narayanix-logo.png";

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
    <footer className="flex items-center justify-center gap-1.5 py-4 text-xs text-ink-400">
      <img src={narayanixLogo} alt="" className="h-4 w-4 object-contain" />
      A NarayaniX Product
    </footer>
  );
}
