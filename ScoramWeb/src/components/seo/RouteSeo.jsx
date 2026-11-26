import Seo from "./Seo";
import { SEO_ROUTES, buildPageJsonLd } from "../../config/seoRoutes";
import { SITE_URL } from "../../config/seo";

// <RouteSeo routeKey="pyq" /> -- applies the title / description / canonical / JSON-LD registered for
// that page in config/seoRoutes.js, so wording lives in ONE place (shared with the build-time prerender).
// Pass `noindex` for pages that should stay out of search results.
export default function RouteSeo({ routeKey, noindex = false }) {
  const route = SEO_ROUTES[routeKey];
  if (!route) return null;
  return (
    <Seo
      title={route.title}
      description={route.description}
      path={route.path}
      noindex={noindex}
      jsonLd={buildPageJsonLd(routeKey, SITE_URL)}
    />
  );
}
