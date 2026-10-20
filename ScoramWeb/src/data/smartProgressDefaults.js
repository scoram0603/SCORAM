// Smart Progress landing section -- SAFE DEFAULT COPY and the resolver that merges admin-edited content over it.
//
// Every claim below corresponds to a real, shipped feature documented in ScoramAPI/SMART_PROGRESS.md and present on
// the website (Progress, Topic progress, Mistake Book + revision, Study Plan). Nothing promises a score, a rank or an
// exam result, and no numbers are quoted. Insights are derived from a student's own COMPLETED attempts, hence the
// "once you have practised" wording. Admins can edit all of this under Admin > Landing Page.
export const DEFAULT_SMART_PROGRESS = {
  published: true,
  heading: "Smart Progress — Understand Your Preparation",
  description:
    "Track your practice performance, understand where you need improvement, and make more informed decisions about what to study next.",
  highlights: [
    "Subject-wise and topic-wise accuracy from your completed attempts",
    "Trends that compare your recent practice with the period before",
    "A Mistake Book built from your own wrong and skipped answers, with revision practice",
    "A daily study plan based on your real practice activity",
  ],
  ctaText: "Start practising",
  ctaLink: "/login?mode=register",
};

export const SMART_PROGRESS_FOOTNOTE =
  "Insights appear once you have completed some practice. Progress reflects your own attempts only.";

const MAX_HIGHLIGHTS = 6;

/** Relative site path ("/x", not "//x") or https URL -- blocks javascript:, data:, protocol-relative etc. */
export function isSafeLink(link) {
  if (typeof link !== "string" || link.length === 0 || /[\s\u0000-\u001f]/.test(link)) return false;
  if (link.startsWith("/")) return !link.startsWith("//") && !link.includes("\\");
  try {
    return new URL(link).protocol === "https:";
  } catch {
    return false;
  }
}

/**
 * Content to render for the section. Missing / malformed API data -> the safe defaults. Returns null when an admin
 * has set the section to hidden (published: false). Pure function -- unit tested.
 */
export function resolveSmartProgress(apiContent) {
  if (!apiContent || typeof apiContent !== "object") return DEFAULT_SMART_PROGRESS;

  const heading = typeof apiContent.heading === "string" ? apiContent.heading.trim() : "";
  const description = typeof apiContent.description === "string" ? apiContent.description.trim() : "";
  if (!heading || !description) return DEFAULT_SMART_PROGRESS;

  if (apiContent.published === false) return null;

  const highlights = Array.isArray(apiContent.highlights)
    ? apiContent.highlights.filter((h) => typeof h === "string" && h.trim()).map((h) => h.trim()).slice(0, MAX_HIGHLIGHTS)
    : [];

  const ctaText = typeof apiContent.ctaText === "string" ? apiContent.ctaText.trim() : "";
  const ctaLink = typeof apiContent.ctaLink === "string" ? apiContent.ctaLink.trim() : "";
  const cta = ctaText && isSafeLink(ctaLink) ? { ctaText, ctaLink } : { ctaText: "", ctaLink: "" };

  return { published: true, heading, description, highlights, ...cta };
}
