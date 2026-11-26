# SCORAM SEO – Sitelinks / Professional Search Result (Nov 2026)

## What was wrong
1. `robots.txt` had `Disallow: /login` → Login could never be indexed or shown as a sitelink.
2. Register was only `/login?mode=register` (same URL as Login) → no separate page for Google to list.
3. No `/download-app` page (the app link went straight to Google Play, an external site).
4. Login/Register/Download App were not in one clear, crawlable place with plain-text link names.
5. Most public pages (/pyq, /question-bank, /tests …) had no unique title/description.
6. Home title was cut off in Google ("PYQs, Previous ...") and "The Vison" typo in /our-team title.

## What changed
- New indexable URLs: `/login`, `/register`, `/download-app` (old `/login?mode=register` redirects to `/register`).
- Navbar: **Login · Download App · Register** buttons (desktop + mobile drawer); footer Quick Links lead with them.
- `src/config/seoRoutes.js`: one registry of titles/descriptions/H1/sitemap for all public pages.
- Home title now ≤ 60 chars: `SCORAM – Competitive Exam Preparation | PYQs & Mock Tests`.
- JSON-LD: Organization + WebSite (+alternate names) + SiteNavigationElement site-wide; BreadcrumbList on sub-pages; MobileApplication on /download-app.
- `scripts/prerender.mjs` (runs after `vite build`): per-page `<head>` + crawlable link shell in raw HTML, and auto-generated `sitemap.xml`.
- `robots.txt` fixed; `sitemap.xml` updated.

## What YOU must do after deploying
1. Search Console → URL Inspection → **Request indexing** for: `/`, `/login`, `/register`, `/download-app`, `/pyq`, `/question-bank`, `/tests`.
2. Submit `https://scoram.in/sitemap.xml` again.
3. Wait: Google needs days to weeks to re-crawl and re-pick sitelinks.

## Honest limits
- Sitelinks are chosen by Google's algorithm; nobody can force them. This work makes the pages eligible and the signals strong. Adda247 / Careerwill have years of brand authority.
- Real ranking growth also needs backlinks, more crawlable content pages (per-exam pages like SSC CGL / RRB NTPC PYQ pages) and a Google Play listing that links to scoram.in.
- If your host rewrites every URL to `/index.html` before checking files, the prerendered per-page HTML is bypassed; the in-app `<Seo>` tags still work for Googlebot.
