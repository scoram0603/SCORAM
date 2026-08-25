import { useEffect } from "react";
import { seoConfig } from "../../config/seo";

// Upserts a <meta> tag by its identifying attribute (name or property) instead of appending a new
// one on every render/navigation -- keeps <head> clean across client-side route changes.
function setMeta(attr, key, content) {
  if (!content) return;
  let tag = document.head.querySelector(`meta[${attr}="${key}"]`);
  if (!tag) {
    tag = document.createElement("meta");
    tag.setAttribute(attr, key);
    document.head.appendChild(tag);
  }
  tag.setAttribute("content", content);
}

function setLink(rel, href) {
  if (!href) return;
  let tag = document.head.querySelector(`link[rel="${rel}"]`);
  if (!tag) {
    tag = document.createElement("link");
    tag.setAttribute("rel", rel);
    document.head.appendChild(tag);
  }
  tag.setAttribute("href", href);
}

function setJsonLd(id, data) {
  let tag = document.getElementById(id);
  if (!tag) {
    tag = document.createElement("script");
    tag.id = id;
    tag.type = "application/ld+json";
    document.head.appendChild(tag);
  }
  tag.textContent = JSON.stringify(data);
}

/**
 * Drop this once near the top of any public, indexable page. Props are all optional and fall back
 * to seoConfig's site-wide defaults.
 *
 * NOTE: this is a client-side-rendered SPA (Vite + React, no SSR/prerendering configured), so these
 * tags are written to <head> by JavaScript after the page loads. Googlebot renders JS and picks
 * these up fine; some other crawlers/link-unfurlers (Slack, WhatsApp, older bots) only read the
 * static index.html and will show the fallback tags baked into index.html instead. See
 * LANDING_REPORT.md for the SSR/prerendering options if that becomes a problem.
 */
export default function Seo({
  title,
  description = seoConfig.defaultDescription,
  keywords = seoConfig.defaultKeywords,
  path = "/",
  image = seoConfig.ogImage,
  type = "website",
  jsonLd,
}) {
  useEffect(() => {
    const resolvedTitle = title
      ? seoConfig.titleTemplate.replace("%s", title)
      : seoConfig.defaultTitle;
    document.title = resolvedTitle;

    setMeta("name", "description", description);
    if (keywords?.length) setMeta("name", "keywords", keywords.join(", "));

    const canonicalUrl = `${seoConfig.siteUrl}${path}`;
    setLink("canonical", canonicalUrl);

    setMeta("property", "og:title", resolvedTitle);
    setMeta("property", "og:description", description);
    setMeta("property", "og:type", type);
    setMeta("property", "og:url", canonicalUrl);
    setMeta("property", "og:site_name", seoConfig.siteName);
    setMeta("property", "og:image", `${seoConfig.siteUrl}${image}`);

    setMeta("name", "twitter:card", seoConfig.twitterCardType);
    setMeta("name", "twitter:title", resolvedTitle);
    setMeta("name", "twitter:description", description);
    setMeta("name", "twitter:image", `${seoConfig.siteUrl}${image}`);

    if (jsonLd) setJsonLd("landing-jsonld", jsonLd);
  }, [title, description, keywords, path, image, type, jsonLd]);

  return null;
}
