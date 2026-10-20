import { Link, useLocation, useNavigate } from "react-router-dom";
import { isExternalHref, isSamePageTopClick, normalizeLandingHref } from "../../utils/scrollNav";

// ONE link component for the landing navbar/footer/CTAs. Items are { to } or { href } as in
// data/landingContent.js. In-page anchors ("#top", "#faq") are turned into real router links ("/", "/#faq"),
// so they also work from /our-team, /privacy-policy etc. (a bare "#faq" there only changed the hash on the
// wrong page). Clicking a link to the page you are already on scrolls back to its top -- the old "#top"
// anchor did nothing because its target is the sticky header, which is already "at the top" of the viewport.
export default function LandingLink({ item, className, onClick, children, ...rest }) {
  const { pathname, hash } = useLocation();
  const navigate = useNavigate();
  const target = normalizeLandingHref(item.to ?? item.href);
  const content = children ?? item.label;

  if (isExternalHref(target)) {
    return <a href={target} onClick={onClick} className={className} {...rest}>{content}</a>;
  }

  function handleClick(event) {
    onClick?.(event);
    if (event.defaultPrevented || !isSamePageTopClick(pathname, target)) return;
    event.preventDefault();
    if (hash) navigate(target, { replace: true }); // drop a stale "#faq" from the address bar
    const reduce = typeof window.matchMedia === "function" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    window.scrollTo({ top: 0, behavior: reduce ? "auto" : "smooth" });
  }

  return <Link to={target} onClick={handleClick} className={className} {...rest}>{content}</Link>;
}
