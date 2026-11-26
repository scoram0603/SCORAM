// Post-build SEO step:  vite build && node scripts/prerender.mjs
//
// 1. For each public route in src/config/seoRoutes.js writes dist/<route>/index.html -- a copy of the built
//    index.html with that page's own <title>, meta description, canonical, Open Graph / Twitter tags and
//    JSON-LD, plus a visually-hidden crawlable link shell inside #root (React replaces it on load).
//    Search engines and link previews that read the raw HTML therefore see the right metadata per page
//    and a real <a href> to Login / Register / Download App, without running JavaScript.
// 2. Generates dist/sitemap.xml from the same route list (so the sitemap can never drift from the pages).
//
//    node scripts/prerender.mjs --sitemap-only   -> refresh public/sitemap.xml (source copy) and exit.
//
// Static hosts normally serve dist/login/index.html for /login before falling back to the SPA rewrite.
import { readFileSync, writeFileSync, mkdirSync, existsSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {
  SEO_ROUTES, PRERENDER_KEYS, PRIMARY_LINKS, SITE_URL, buildPageJsonLd, buildSiteJsonLd,
} from "../src/config/seoRoutes.js";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const siteUrl = (process.env.VITE_SITE_URL || SITE_URL).replace(/\/$/, "");
const today = new Date().toISOString().slice(0, 10);

const esc = (v) => String(v).replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
const abs = (path) => `${siteUrl}${path}`;

function buildSitemap() {
  const urls = Object.values(SEO_ROUTES)
    .map(
      (r) => `  <url>
    <loc>${abs(r.path)}</loc>
    <lastmod>${today}</lastmod>
    <changefreq>${r.changefreq}</changefreq>
    <priority>${r.priority}</priority>
  </url>`
    )
    .join("\n");
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls}\n</urlset>\n`;
}

if (process.argv.includes("--sitemap-only")) {
  writeFileSync(join(root, "public", "sitemap.xml"), buildSitemap());
  console.log("public/sitemap.xml updated");
  process.exit(0);
}

const dist = join(root, "dist");
const template = readFileSync(join(dist, "index.html"), "utf8");

function setTag(html, pattern, replacement) {
  if (!pattern.test(html)) throw new Error(`index.html is missing a tag matching ${pattern}`);
  return html.replace(pattern, replacement);
}

function renderRoute(key) {
  const r = SEO_ROUTES[key];
  const url = abs(r.path === "/" ? "/" : r.path);
  let html = template;
  html = setTag(html, /<title>[\s\S]*?<\/title>/, `<title>${esc(r.title)}</title>`);
  html = setTag(html, /<meta name="description"[^>]*>/, `<meta name="description" content="${esc(r.description)}" />`);
  html = setTag(html, /<link rel="canonical"[^>]*>/, `<link rel="canonical" href="${url}" />`);
  html = setTag(html, /<meta property="og:title"[^>]*>/, `<meta property="og:title" content="${esc(r.title)}" />`);
  html = setTag(html, /<meta property="og:description"[^>]*>/, `<meta property="og:description" content="${esc(r.description)}" />`);
  html = setTag(html, /<meta property="og:url"[^>]*>/, `<meta property="og:url" content="${url}" />`);
  html = setTag(html, /<meta name="twitter:title"[^>]*>/, `<meta name="twitter:title" content="${esc(r.title)}" />`);
  html = setTag(html, /<meta name="twitter:description"[^>]*>/, `<meta name="twitter:description" content="${esc(r.description)}" />`);

  // Site-wide entity data (Organization, WebSite, SiteNavigationElement) on every page.
  html = html.replace("</head>", `    <script id="site-jsonld" type="application/ld+json">${JSON.stringify(buildSiteJsonLd(siteUrl))}</script>\n  </head>`);
  const pageLd = buildPageJsonLd(key, siteUrl);
  if (pageLd) {
    html = html.replace("</head>", `    <script id="landing-jsonld" type="application/ld+json">${JSON.stringify(pageLd)}</script>\n  </head>`);
  }

  // Hidden-but-crawlable shell: mirrors the real navigation + page heading. Replaced by React on mount.
  const links = PRIMARY_LINKS.map((l) => `<li><a href="${l.path}">${esc(l.label)}</a></li>`).join("");
  const shell =
    `<div id="seo-shell" style="position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap">` +
    `<nav aria-label="Main"><ul>${links}</ul></nav>` +
    `<main><h1>${esc(r.h1)}</h1><p>${esc(r.description)}</p></main></div>`;
  html = setTag(html, /<div id="root"><\/div>/, `<div id="root">${shell}</div>`);
  return html;
}

for (const key of PRERENDER_KEYS) {
  const r = SEO_ROUTES[key];
  const out = r.path === "/" ? join(dist, "index.html") : join(dist, r.path, "index.html");
  if (!existsSync(dirname(out))) mkdirSync(dirname(out), { recursive: true });
  writeFileSync(out, renderRoute(key));
}
writeFileSync(join(dist, "sitemap.xml"), buildSitemap());
console.log(`prerendered ${PRERENDER_KEYS.length} routes + sitemap.xml`);
