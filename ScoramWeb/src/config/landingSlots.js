import defaultLogo from "../assets/scoram-logo-horizontal.png";
import { API_BASE_URL } from "../api/client";

// LANDING SLOTS -- single, central definition of every landing-page image an admin can replace
// (Admin > Landing Page), and of the BUNDLED DEFAULT that is shown when nothing is configured, when the
// configured file is missing, or when it fails to load. The keys MUST match LandingContentCatalog.cs on the API.
//
// Rules this file enforces (see resolveSlotSources):
//   * the default for a slot is only ever used for THAT slot (a founder photo can never become the logo);
//   * a configured URL is only accepted if it is a media URL produced by the upload feature;
//   * candidates are tried in order, each at most once -- there is no retry loop.
// `default: null` means "no bundled image exists" (the Smart Progress section works without one).
export const LANDING_SLOTS = {
  logo: { default: defaultLogo, alt: "SCORAM" },
  "hero-preview": { default: "/assets/landing/scoram-app-preview.png" },
  "pyp-preview": { default: "/assets/landing/pyp-preview.png" },
  "question-bank-preview": { default: "/assets/landing/question-bank-preview.png" },
  "solutions-preview": { default: "/assets/landing/solutions-preview.png" },
  "community-preview": { default: "/assets/landing/community-preview.png" },
  "app-promo-preview": { default: "/assets/landing/mobile-app-preview.png" },
  "smart-progress-image": { default: null },
  "founder-sarvoch": { default: "/assets/team/sarvoch.jpg" },
  "founder-durgesh": { default: "/assets/team/durgesh.jpg" },
};

const OWN_MEDIA_PREFIX = "/uploads/landing/";

/** True only for a URL the upload feature itself produced (never external / traversal / other folders). */
export function isOwnMediaUrl(url) {
  return (
    typeof url === "string" &&
    url.startsWith(OWN_MEDIA_PREFIX) &&
    !url.includes("..") &&
    url.indexOf("/", OWN_MEDIA_PREFIX.length) === -1
  );
}

/** Absolute URL for a configured media path ("/uploads/landing/x.png" -> API origin + path). */
export function toAbsoluteMediaUrl(url, apiBase = API_BASE_URL) {
  return url.startsWith("http") ? url : `${apiBase}${url}`;
}

/**
 * Ordered list of image URLs to try for a slot: [admin-configured (if valid), bundled default (if any)].
 * Unknown slots, empty / invalid configuration and a missing `media` map all simply yield the default.
 * Pure function -- unit tested.
 */
export function resolveSlotSources(slotKey, media, { apiBase = API_BASE_URL, fallbackSrc = null } = {}) {
  const slot = LANDING_SLOTS[slotKey];
  const bundled = slot ? slot.default : fallbackSrc;
  const configured = media && typeof media === "object" ? media[slotKey] : null;

  const sources = [];
  if (isOwnMediaUrl(configured)) sources.push(toAbsoluteMediaUrl(configured, apiBase));
  if (bundled) sources.push(bundled);
  return sources;
}
