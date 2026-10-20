import { useEffect, useRef } from "react";
import { useLocation, useNavigationType } from "react-router-dom";
import { scrollDecision } from "../../utils/scrollNav";

// Mounted once (main.jsx, inside <BrowserRouter>) so it covers the student site AND the admin app.
//   * a new route opens at the top (instead of inheriting the previous page's scroll position);
//   * "/#faq"-style links scroll to their section, also when coming from another route -- the target may
//     render a moment later (lazy data), so we look for it for up to ~1.5s and stop at the first hit;
//   * browser back/forward is left to the browser's own scroll restoration, and query-string-only changes
//     (search filters) never move the page.
// The sticky landing header is covered by `scroll-padding-top` in index.css, so anchors aren't hidden beneath it.
const ANCHOR_WAIT_MS = 1500;

function prefersReducedMotion() {
  return typeof window.matchMedia === "function" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

export default function ScrollManager() {
  const { pathname, hash, key } = useLocation();
  const navType = useNavigationType();
  const prevPathname = useRef(null);

  useEffect(() => {
    const decision = scrollDecision({ prevPathname: prevPathname.current, pathname, hash, navType });
    prevPathname.current = pathname;
    if (decision.type === "none") return undefined;

    if (decision.type === "top") {
      window.scrollTo(0, 0);
      return undefined;
    }

    let frame = 0;
    const started = performance.now();
    const seek = () => {
      const el = document.getElementById(decision.id);
      if (el) {
        el.scrollIntoView({ block: "start", behavior: prefersReducedMotion() ? "auto" : "smooth" });
        return;
      }
      if (performance.now() - started < ANCHOR_WAIT_MS) frame = requestAnimationFrame(seek);
    };
    seek();
    return () => cancelAnimationFrame(frame);
    // `key` changes on every navigation, including re-clicking the same anchor.
  }, [key, pathname, hash, navType]);

  return null;
}
