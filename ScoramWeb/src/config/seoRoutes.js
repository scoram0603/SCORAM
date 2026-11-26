// SEO ROUTE REGISTRY -- the single source of truth for every public, indexable page's title, meta
// description, H1 and sitemap entry. Used by:
//   - components/seo/RouteSeo.jsx   (runtime <head> tags once the SPA has loaded)
//   - scripts/prerender.mjs         (build step: bakes per-page <head> + a crawlable link shell into
//                                    dist/<route>/index.html, and generates dist/sitemap.xml)
//
// Keep this file dependency-free (no import.meta.env, no React) -- the build script imports it from Node.
//
// WHY THESE PAGES EXIST AS SEPARATE URLS: Google builds the indented "sitelinks" under a brand result
// (e.g. Login / Register / Download App) from distinct, crawlable, indexable pages that are linked
// prominently from the home page. Sitelinks are chosen algorithmically and cannot be forced, but a page
// that is blocked in robots.txt, shares a URL with another page (/login?mode=register), has no unique
// title, or is not linked from the main navigation is never eligible. See SEO_REPORT.md.

export const SITE_NAME = "SCORAM";
export const SITE_URL = "https://scoram.in";
export const PLAY_STORE_URL = "https://play.google.com/store/apps/details?id=in.scoram.app";

export const HOME_TITLE = "SCORAM – Competitive Exam Preparation | PYQs & Mock Tests";
export const HOME_DESCRIPTION =
  "SCORAM is an all-in-one platform for SSC, Railway and government exam preparation — previous year papers, topic-wise question bank, mock tests, quizzes and community discussions.";

// key -> page definition. `indexable: false` pages are excluded from the sitemap and get noindex.
export const SEO_ROUTES = {
  home: {
    path: "/",
    title: HOME_TITLE,
    description: HOME_DESCRIPTION,
    h1: "SCORAM – Competitive Exam Preparation",
    changefreq: "weekly",
    priority: "1.0",
  },
  login: {
    path: "/login",
    title: "SCORAM Login – Sign In to Your Exam Preparation Account",
    description:
      "Log in to your SCORAM account with password or OTP to continue practising previous year papers, mock tests and your personal progress for SSC, Railway and other exams.",
    h1: "Login to SCORAM",
    changefreq: "monthly",
    priority: "0.9",
  },
  register: {
    path: "/register",
    title: "SCORAM Register – Create Your Free Exam Preparation Account",
    description:
      "Create your SCORAM account in minutes. Get previous year papers, a topic-wise question bank, mock tests, quizzes and progress tracking for SSC, Railway and government exams.",
    h1: "Create your SCORAM account",
    changefreq: "monthly",
    priority: "0.9",
  },
  downloadApp: {
    path: "/download-app",
    title: "Download SCORAM App – Exam Preparation on Android",
    description:
      "Download the SCORAM app from Google Play and practise previous year papers, question bank and mock tests for SSC, Railway and government exams anywhere, any time.",
    h1: "Download the SCORAM App",
    changefreq: "monthly",
    priority: "0.9",
  },
  pyq: {
    path: "/pyq",
    title: "Previous Year Papers (PYP) Practice – Full-Length Timed Tests | SCORAM",
    description:
      "Attempt full previous year question papers for SSC, Railway and other government exams as timed tests. Browse by exam, year and language on SCORAM.",
    h1: "Previous Year Paper Practice",
    changefreq: "weekly",
    priority: "0.8",
  },
  questionBank: {
    path: "/question-bank",
    title: "PYQ Question Bank – Topic-Wise Questions with Solutions | SCORAM",
    description:
      "Search and practise topic-wise previous year questions for SSC, Railway and competitive exams, with detailed multi-method solutions on SCORAM.",
    h1: "PYQ Question Bank",
    changefreq: "weekly",
    priority: "0.8",
  },
  tests: {
    path: "/tests",
    title: "Practice Tests & Mock Tests for Competitive Exams | SCORAM",
    description:
      "Timed practice tests and full mock tests that mirror the real exam experience for SSC, Railway and other government exams. Start practising on SCORAM.",
    h1: "Practice Tests & Mock Tests",
    changefreq: "weekly",
    priority: "0.8",
  },
  search: {
    path: "/search",
    title: "Find Previous Year Questions (PYQ Search) | SCORAM",
    description:
      "Search previous year questions by keyword and exam. Find the exact question you are practising and see its solution on SCORAM.",
    h1: "Find Previous Year Questions",
    changefreq: "weekly",
    priority: "0.6",
  },
  discussions: {
    path: "/discussions",
    title: "Exam Discussions – Ask Doubts & Discuss Questions | SCORAM",
    description:
      "Join SCORAM discussions to ask doubts, share approaches and learn from other aspirants preparing for SSC, Railway and government exams.",
    h1: "Discussions",
    changefreq: "daily",
    priority: "0.6",
  },
  chat: {
    path: "/chat",
    title: "Community Group Chat for Exam Aspirants | SCORAM",
    description:
      "Chat with other competitive exam aspirants in the SCORAM community. Stay motivated, share resources and prepare together.",
    h1: "Community Chat",
    changefreq: "daily",
    priority: "0.5",
  },
  ourTeam: {
    path: "/our-team",
    title: "The Vision – Meet the People Behind SCORAM",
    description:
      "Meet the people behind SCORAM — built with a purpose to help aspirants prepare with greater clarity, confidence, and direction.",
    h1: "The People Building SCORAM",
    changefreq: "yearly",
    priority: "0.4",
  },
  privacy: {
    path: "/privacy-policy",
    title: "SCORAM Privacy Policy",
    description: "How SCORAM collects, uses and protects your personal information.",
    h1: "Privacy Policy",
    changefreq: "yearly",
    priority: "0.2",
  },
  terms: {
    path: "/terms-and-conditions",
    title: "SCORAM Terms & Conditions",
    description: "The terms and conditions for using the SCORAM website and app.",
    h1: "Terms & Conditions",
    changefreq: "yearly",
    priority: "0.2",
  },
  deleteAccount: {
    path: "/delete-account",
    title: "Delete Your SCORAM Account",
    description: "How to request deletion of your SCORAM account and associated data.",
    h1: "Delete Account",
    changefreq: "yearly",
    priority: "0.2",
  },
};

// Pages whose <head> is written by the build (prerender). Legal pages keep their own, richer titles
// at runtime, so they are listed in the sitemap but not prerendered.
export const PRERENDER_KEYS = [
  "home", "login", "register", "downloadApp", "pyq", "questionBank", "tests",
  "search", "discussions", "chat", "ourTeam",
];

// The links that go into the crawlable shell and the Organization/SiteNavigationElement data.
// Order = importance. Login / Register / Download App deliberately lead.
export const PRIMARY_LINKS = [
  { label: "Login", path: "/login" },
  { label: "Register", path: "/register" },
  { label: "Download App", path: "/download-app" },
  { label: "PYP Practice", path: "/pyq" },
  { label: "PYQ Question Bank", path: "/question-bank" },
  { label: "Practice & Mock Tests", path: "/tests" },
  { label: "Discussions", path: "/discussions" },
  { label: "Community Chat", path: "/chat" },
  { label: "The Vision", path: "/our-team" },
];

const abs = (siteUrl, path) => `${siteUrl}${path === "/" ? "/" : path}`;

/** Organization + WebSite + SiteNavigationElement -- identical on every page (site-wide entity data). */
export function buildSiteJsonLd(siteUrl = SITE_URL) {
  return {
    "@context": "https://schema.org",
    "@graph": [
      {
        "@type": "EducationalOrganization",
        "@id": `${siteUrl}/#organization`,
        name: SITE_NAME,
        alternateName: ["Scoram", "SCORAM Exam Preparation"],
        url: `${siteUrl}/`,
        logo: `${siteUrl}/assets/landing/scoram-logo.png`,
        description: HOME_DESCRIPTION,
        email: "info@scoram.in",
        telephone: "+91 97566 47906",
        sameAs: [PLAY_STORE_URL],
      },
      {
        "@type": "WebSite",
        "@id": `${siteUrl}/#website`,
        url: `${siteUrl}/`,
        name: SITE_NAME,
        alternateName: ["Scoram", "scoram.in"],
        inLanguage: "en-IN",
        publisher: { "@id": `${siteUrl}/#organization` },
      },
      ...PRIMARY_LINKS.map((link) => ({
        "@type": "SiteNavigationElement",
        "@id": `${siteUrl}${link.path}#nav`,
        name: link.label,
        url: abs(siteUrl, link.path),
      })),
    ],
  };
}

/** Page-level JSON-LD: BreadcrumbList for sub-pages, plus MobileApplication on /download-app. */
export function buildPageJsonLd(key, siteUrl = SITE_URL) {
  const route = SEO_ROUTES[key];
  if (!route || key === "home") return null;
  const graph = [
    {
      "@type": "BreadcrumbList",
      itemListElement: [
        { "@type": "ListItem", position: 1, name: "Home", item: `${siteUrl}/` },
        { "@type": "ListItem", position: 2, name: route.h1, item: abs(siteUrl, route.path) },
      ],
    },
  ];
  if (key === "downloadApp") {
    graph.push({
      "@type": "MobileApplication",
      name: "SCORAM",
      operatingSystem: "ANDROID",
      applicationCategory: "EducationalApplication",
      description: route.description,
      url: abs(siteUrl, route.path),
      installUrl: PLAY_STORE_URL,
      publisher: { "@id": `${siteUrl}/#organization` },
    });
  }
  return { "@context": "https://schema.org", "@graph": graph };
}
