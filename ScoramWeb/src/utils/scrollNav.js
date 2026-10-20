// ROUTE SCROLL RULES -- pure helpers behind <ScrollManager /> and <LandingLink />, kept free of React/DOM so
// the rules can be unit-tested.

/** "#top" -> "/" (the landing page), "#faq" -> "/#faq"; everything else (routes, mailto:, tel:, http) unchanged. */
export function normalizeLandingHref(href) {
  if (typeof href !== "string") return href;
  if (href === "#top" || href === "#") return "/";
  if (href.startsWith("#")) return `/${href}`;
  return href;
}

/** True for links that leave the SPA router (mailto:, tel:, absolute URLs). */
export function isExternalHref(href) {
  return typeof href === "string" && /^(mailto:|tel:|https?:\/\/|\/\/)/i.test(href);
}

function decodeHashId(hash) {
  const raw = (hash || "").replace(/^#/, "");
  try { return decodeURIComponent(raw); } catch { return raw; }
}

/**
 * What should the viewport do after a navigation?
 *   { type: "hash", id }  -> scroll to the element with that id (same-page anchors, "/#faq" from another route)
 *   { type: "top" }       -> reset to the top (a new route was opened)
 *   { type: "none" }      -> leave scrolling alone (browser back/forward restores its own position; query-string
 *                            only changes such as filters keep the page where it is; first load without a hash)
 * navType is React Router's "PUSH" | "REPLACE" | "POP".
 */
export function scrollDecision({ prevPathname, pathname, hash, navType }) {
  const id = decodeHashId(hash);
  if (id) return id === "top" ? { type: "top" } : { type: "hash", id };
  if (navType === "POP") return { type: "none" };
  if (prevPathname !== null && prevPathname !== undefined && prevPathname !== pathname) return { type: "top" };
  return { type: "none" };
}

/** A click on a link whose target is the page we are already on (no hash) means "back to the top of this page". */
export function isSamePageTopClick(currentPathname, to) {
  if (typeof to !== "string") return false;
  if (to.includes("#") || to.includes("?")) return false;
  return to === currentPathname;
}
