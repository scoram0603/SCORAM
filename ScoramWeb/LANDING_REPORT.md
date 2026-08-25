# SCORAM Public Landing Page — Implementation Report

## 1. What this is

A new public marketing landing page for SCORAM, shown at `/` to **signed-out visitors only**.
Logged-in students continue to see the existing home feed exactly as before — nothing about the
admin panel, admin APIs, or existing student functionality was touched.

---

## 2. Files created (frontend — `ScoramWeb`)

**Landing page + sections** (`src/components/landing/`)
- `LandingNavbar.jsx` — sticky nav, desktop links + mobile drawer
- `HeroSection.jsx` — H1, CTAs, phone mockup, live stat cards
- `StatsSection.jsx` — trust/stats strip
- `FeaturesSection.jsx`
- `PYPSection.jsx`
- `QuestionBankSection.jsx`
- `SolutionsSection.jsx` (Multiple Solving Methods)
- `CommunitySection.jsx`
- `HowItWorksSection.jsx`
- `PopularExamsSection.jsx` — pulls real exams from the existing public `GET /api/exams`
- `TestimonialsSection.jsx` — **placeholder copy, see §5**
- `AppPromotionSection.jsx`
- `FAQSection.jsx` — includes FAQPage JSON-LD
- `FinalCTA.jsx`
- `LandingFooter.jsx`
- `ScrollReveal.jsx` — small IntersectionObserver fade/slide-up wrapper, no new dependency
- `AssetImage.jsx` — shows a clean labeled placeholder instead of a broken image icon for any
  image that hasn't been dropped in yet (see §5)

**Page/data/config**
- `src/pages/Landing.jsx` — assembles all sections, fetches live stats once
- `src/pages/LegalPage.jsx`, `src/pages/PrivacyPolicy.jsx`, `src/pages/Terms.jsx` — see §6
- `src/data/landingContent.js` — **all editable copy** (nav links, features, FAQ, testimonials,
  how-it-works, footer links) — edit this file to change wording without touching any component
- `src/config/seo.js` — site name/URL/description/keywords/OG image/contact, `VITE_SITE_URL`-aware
- `src/components/seo/Seo.jsx` — per-page title/meta/canonical/OG/Twitter/JSON-LD, no new dependency
- `src/api/publicStats.js` — wrapper for the new `GET /api/public-stats`

**Static files**
- `public/robots.txt`
- `public/sitemap.xml`
- `public/assets/landing/scoram-logo.png` — copied from the existing real logo (not generated)
- `public/assets/landing/`, `public/assets/seo/` — folders for the images listed in §5

## Files created (backend — `ScoramAPI`)

- `Controllers/PublicStatsController.cs` — new, anonymous, read-only `GET /api/public-stats`
- `DTOs/PublicStatsDTOs.cs`

This is the **only** backend change. Nothing else in `ScoramAPI` was touched — no existing
controller, DTO, or business logic was modified. It doesn't reuse `DashboardController` (that one
is Admin/SuperAdmin-gated and returns internal operational data that must never be public); this
is a brand-new, tiny endpoint returning three honest counts computed directly from the DB:
`Questions.Count + QuestionBankQuestions.Count`, `Exams.Count(!IsBlocked)`,
`Users.Count(IsActive)`.

## Files modified (small, isolated, disclosed individually)

1. **`src/layouts/AppLayout.jsx`** — added one `if` block at the top: if a signed-out visitor is
   at `/`, render `<Landing />` instead of the normal sidebar/header/bottom-nav/`<Outlet/>` chrome.
   Every other route, and every authenticated user on any route including `/`, is unaffected —
   `Home.jsx` and its route mapping in `App.jsx` are untouched.
2. **`src/pages/Login.jsx`** — one line: the `mode` state now reads `?mode=register` from the URL
   on first render (falls back to `"login"` exactly as before if absent). This is what makes the
   landing page's "Sign Up" buttons open straight into the registration form via
   `/login?mode=register`.
3. **`src/App.jsx`** — added two new top-level routes, `privacy-policy` and `terms` (see §6). No
   existing route was changed or moved.
4. **`index.html`** — added static fallback `<title>`/meta description/canonical/OG/Twitter tags
   (see §7) for crawlers that don't execute JS. The favicon, fonts, and everything else is
   untouched.

**Confirmation:** ran `npm run build` and `npm run lint` (oxlint) against the full project —
0 errors, and the only warnings are pre-existing ones in unrelated admin files I never touched.
No admin panel, admin API, question upload, test management, or existing student page was
modified.

---

## 3. Live, real data — not fabricated

The reference mockup showed numbers like "125,000+ Questions / 500+ Exams / 50,000+ Students /
4.8/5 Rating" — those are placeholder marketing figures, not real data, so I didn't use them.
Instead:

- **Questions, Exams, Students** on Hero + Stats sections come from the new
  `GET /api/public-stats` endpoint, computed live from your database.
- **Popular Exams** comes from the existing public `GET /api/exams`, same source and same
  "only exams with real questions" filter as the in-app `PopularExams` widget — never a
  hardcoded exam list.
- The star rating shown in the reference image was dropped entirely — there's no review data to
  back it, and the brief itself says not to fabricate ratings.
- If `/api/public-stats` is unreachable (e.g. API not running locally), the sections show `—`
  instead of a fake number — they never invent one.

---

## 4. How to run

**Backend:** run `ScoramAPI` exactly as you already do (`dotnet run`). The new controller needs no
migration, no registration — ASP.NET Core auto-discovers it.

**Frontend:**
```
cd ScoramWeb
npm install
npm run dev
```
Visit `/` while signed out to see the landing page; sign in and `/` shows the normal app home.

---

## 5. Required image assets

None of these exist yet. Until you drop them in, the page shows a clean labeled placeholder
(dashed box with the expected filename) instead of a broken image — so it's safe to test/demo
before assets are ready.

| # | Path | Recommended size | Description |
|---|------|------|-------------|
| 1 | `public/assets/landing/scoram-app-preview.png` | 900×1850 | SCORAM mobile app home screen (used in the hero phone mockup) |
| 2 | `public/assets/landing/pyp-preview.png` | 800×1000 | Screenshot of the PYP Practice timed-paper interface |
| 3 | `public/assets/landing/question-bank-preview.png` | 800×1000 | Screenshot of Question Bank with filters |
| 4 | `public/assets/landing/solutions-preview.png` | 800×1000 | UI showing multiple solving methods for one question |
| 5 | `public/assets/landing/community-preview.png` | 800×1000 | Screenshot of group chat / discussions |
| 6 | `public/assets/landing/mobile-app-preview.png` | 900×1600 | App promotion phone mockup |
| 7 | `public/assets/seo/scoram-og-image.png` | 1200×630 | Social share image (Open Graph / Twitter card) |

`public/assets/landing/scoram-logo.png` is **already in place** — copied from your existing real
logo (`src/assets/scoram-logo-square.png`), not generated.

---

## 6. Things I added that need your review (not silently assumed)

- **Testimonials** (`TestimonialsSection`, content in `landingContent.js`) are clearly placeholder
  copy — generic quotes, "Placeholder Student" as the name, no star rating, no "verified" badge.
  Swap in real student testimonials (with permission) before launch.
- **Privacy Policy / Terms pages** (`/privacy-policy`, `/terms`) — the brief's footer structure
  requires these links to go somewhere real rather than 404. I added minimal, generic draft pages,
  clearly marked in-file as templates **not reviewed by a lawyer** — please have actual counsel
  review before relying on them.
- **"Pricing" nav item** — there's no pricing page/route in the app (SCORAM appears to be free per
  the brief's own CTA copy), so it scroll-links to the FAQ section, which now includes an
  "Is SCORAM free to use?" entry.
- **Social media icons** in the footer are wired up but render nothing — `seoConfig.socialLinks`
  is empty by design, so no placeholder `#` links get published. Add real profile URLs there
  (with a `lucide-react` icon per entry) once they exist.
- **"Get the SCORAM App" CTA** currently scrolls to top — point it at real App Store / Play Store
  links once the app ships.
- **"Help Center" footer link** scroll-links to FAQ (no dedicated help center exists yet).

---

## 7. SEO Implementation Report

**Title tag:** `SCORAM – Competitive Exam Preparation | PYQ, Question Bank & Mock Tests`
**Meta description:** matches the brief's suggested copy (Prepare for SSC, Railway... practice
PYQs, question banks, mock tests, quizzes, community discussions).
**Target keywords:** the full list from your brief lives in `src/config/seo.js` →
`defaultKeywords`, used for the meta keywords tag and worked naturally into section headings/copy
(not stuffed).
**Canonical URL:** `https://scoram.in/` by default — **confirm this is the real production
domain**; it's the only domain currently referenced anywhere in the codebase (ScoramAPI's CORS
allow-list). If it's different, update:
  - `src/config/seo.js` → set `VITE_SITE_URL` in `.env.production`
  - the hardcoded absolute URLs in `index.html` (canonical + OG + Twitter)
  - `public/sitemap.xml` and `public/robots.txt`
**Sitemap:** `public/sitemap.xml` — only real, stable, public routes (`/`, `/pyq`,
`/question-bank`, `/tests`, `/search`, `/discussions`, `/chat`, `/privacy-policy`, `/terms`).
Per-question detail pages aren't included — a static sitemap can't enumerate every question ID;
that would need a server-generated dynamic sitemap, which is out of scope for this CSR app.
**Robots.txt:** `public/robots.txt` — allows everything except `/admin` and auth-gated pages
(`/login`, `/profile`, `/settings`, `/tests/practice`, `/tests/mock`, etc.), points to the sitemap.
**Open Graph / Twitter:** implemented in both `index.html` (static fallback) and `Seo.jsx`
(per-page, runtime). Needs the OG image asset (§5, item 7).
**Structured data (JSON-LD):** `EducationalOrganization` + `WebSite` + `FAQPage` (built from the
same FAQ content shown on the page — no fabricated ratings, reviews, or prices anywhere).
**Image SEO:** every landing image has descriptive alt text (see each component); filenames are
already descriptive kebab-case.
**Image performance:** `loading="lazy"` on all below-the-fold landing images.
**Semantic HTML / one H1:** single `<h1>` lives in `HeroSection` ("Learn Better. Discuss Freely.
Score Higher."); each subsequent section uses one `<h2>`; `<header>`/`<nav>`/`<main>`/`<footer>`
used throughout.
**Accessibility:** `aria-label`/`aria-expanded` on interactive nav/FAQ controls, visible focus is
inherited from the app's existing Tailwind defaults, all images have alt text.

### One real caveat — no SSR/prerendering

`ScoramWeb` is a client-rendered Vite SPA (no server-side rendering configured). All the metadata
above is written to `<head>` by JavaScript after the page loads (`Seo.jsx`), plus static fallback
tags baked into `index.html` for the very first paint. **Googlebot renders JavaScript and will see
both correctly.** Some other crawlers and link-unfurlers (Slack/WhatsApp previews, older bots)
only read the static `index.html` — they'll see the site-wide fallback tags rather than each
page's specific ones. If that becomes a problem, the fix is adding SSR or prerendering (e.g.
`vite-plugin-ssr`, prerendering just the public routes at build time) — that's a real
infrastructure change I didn't make since it touches the build/deploy pipeline, not just the
landing page.

### Google Search Console setup (manual steps)

1. Add `https://scoram.in` as a property in Search Console (Domain or URL-prefix).
2. Verify ownership (DNS TXT record is usually easiest for a domain property).
3. Submit `https://scoram.in/sitemap.xml` under Sitemaps.
4. Use URL Inspection → Request Indexing for `/` once it's live.
5. Check the Coverage/Indexing report over the following days/weeks for crawl errors.
6. Monitor Performance for impressions/clicks on the target keywords above.

(Submitting a sitemap doesn't guarantee indexing or ranking — it just helps Google find pages.)

---

## 8. Confirmation

- No admin panel, admin dashboard, admin API, question upload/PYQ wizard, Question Bank
  management, or test management was created or modified.
- Existing student application functionality is unchanged: `Home.jsx`, all protected routes, and
  every existing page/component/API works exactly as before for authenticated users.
- `npm run build` and `npm run lint` both pass clean on the full project.
- Backend change is additive-only (two new files); no existing `.cs` file was edited.
