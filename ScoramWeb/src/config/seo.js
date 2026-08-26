// SEO CONFIGURATION -- centralized so metadata never gets hardcoded across pages.
//
// siteUrl MUST be the real production domain before launch -- set VITE_SITE_URL in .env.production.
// Defaults to https://scoram.in (the domain already referenced in ScoramAPI's CORS allow-list) --
// change this if that isn't the actual production domain.
export const SITE_URL = import.meta.env.VITE_SITE_URL || "https://scoram.in";

export const seoConfig = {
  siteName: "SCORAM",
  siteUrl: SITE_URL,
  defaultTitle: "SCORAM – Competitive Exam Preparation | PYQs, Previous Year Papers & Mock Tests",
  titleTemplate: "%s | SCORAM",
  defaultDescription:
    "Prepare for SSC, Railway and other competitive exams with SCORAM. Practice previous year papers, a topic-wise question bank, mock tests, quizzes, and learn through community discussions.",
  defaultKeywords: [
    "SCORAM",
    "SCORAM learning platform",
    "SCORAM exam preparation",
    "competitive exam preparation",
    "government exam preparation",
    "SSC exam preparation",
    "SSC CGL preparation",
    "SSC CHSL preparation",
    "railway exam preparation",
    "RRB NTPC preparation",
    "previous year question papers",
    "PYQ practice",
    "competitive exam question bank",
    "mock tests for competitive exams",
    "exam preparation app",
  ],
  // Required asset -- see LANDING_REPORT.md. 1200x630, used for og:image / twitter:image.
  ogImage: "/assets/seo/scoram-og-image.png",
  logo: "/assets/landing/scoram-logo.png",
  twitterCardType: "summary_large_image",
  contact: {
    email: "info@scoram.in",
    phone: "+91 97566 47906",
  },
  // Fill in once real, live social profiles exist -- an empty array here means the footer and
  // JSON-LD both correctly omit social links rather than pointing at placeholder "#" URLs.
  socialLinks: [],
};
