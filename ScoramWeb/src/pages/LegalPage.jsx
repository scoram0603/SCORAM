import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import LandingNavbar from "../components/landing/LandingNavbar";
import LandingFooter from "../components/landing/LandingFooter";
import Seo from "../components/seo/Seo";
import { LEGAL, isPlaceholder } from "../config/legal";

// Renders a legal-config value. Unresolved placeholders ("[...]") are highlighted so they are
// impossible to miss in review -- and obviously wrong if one ever ships by mistake.
export function Fill({ children }) {
  const text = typeof children === "string" ? children : String(children ?? "");
  if (!isPlaceholder(text)) return <>{text}</>;
  return (
    <mark className="rounded border border-dashed border-accent-500 bg-accent-100 px-1 py-0.5 font-semibold text-ink-900">
      {text}
    </mark>
  );
}

function MetaItem({ label, children }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-semibold text-ink-400">{label}</dt>
      <dd className="mt-0.5 break-words text-sm font-semibold text-ink-900">{children}</dd>
    </div>
  );
}

function SectionNav({ sections, activeId, onNavigate }) {
  return (
    <ol className="space-y-0.5 text-sm">
      {sections.map((s, i) => {
        const active = s.id === activeId;
        return (
          <li key={s.id}>
            <a
              href={`#${s.id}`}
              onClick={onNavigate}
              aria-current={active ? "location" : undefined}
              className={`flex gap-2 rounded-lg px-2.5 py-1.5 leading-snug transition-colors ${
                active
                  ? "bg-primary-50 font-semibold text-primary-600"
                  : "text-ink-600 hover:bg-surface hover:text-primary-600"
              }`}
            >
              <span className="w-5 shrink-0 tabular-nums text-ink-400">{i + 1}.</span>
              <span>{s.title}</span>
            </a>
          </li>
        );
      })}
    </ol>
  );
}

/**
 * Shared shell for Privacy Policy / Terms & Conditions / Delete Account.
 * `sections` is [{ id, title, content }] -- headings are numbered because legal documents cross-
 * reference each other by section number ("see Section 9").
 */
export default function LegalPage({
  title,
  subtitle,
  seoTitle,
  description,
  path,
  sections,
  showMeta = true,
  intro,
  children,
}) {
  const [activeId, setActiveId] = useState(sections[0]?.id);
  const [tocOpen, setTocOpen] = useState(false);

  // Highlight the section currently under the sticky header while scrolling (desktop TOC).
  useEffect(() => {
    if (typeof IntersectionObserver === "undefined") return undefined;
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries.filter((e) => e.isIntersecting).sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top);
        if (visible[0]) setActiveId(visible[0].target.id);
      },
      { rootMargin: "-96px 0px -65% 0px", threshold: 0 }
    );
    sections.forEach((s) => {
      const el = document.getElementById(s.id);
      if (el) observer.observe(el);
    });
    return () => observer.disconnect();
  }, [sections]);

  return (
    <div className="min-h-screen overflow-x-hidden bg-white">
      <Seo title={seoTitle || title} path={path} description={description} />
      <LandingNavbar />

      <main className="mx-auto max-w-6xl px-4 pb-20 pt-10 sm:px-6 lg:px-8 lg:pt-14">
        <header className="max-w-3xl">
          <h1 className="text-3xl font-extrabold tracking-tight text-ink-900 sm:text-4xl">{title}</h1>
          {subtitle && <p className="mt-2 text-base font-medium text-primary-500">{subtitle}</p>}
          {intro && <p className="mt-4 text-[15px] leading-relaxed text-ink-600">{intro}</p>}
        </header>

        {showMeta && (
          <dl className="mt-8 grid max-w-3xl gap-x-8 gap-y-4 rounded-xl2 border border-primary-100 bg-surface p-5 sm:grid-cols-2">
            <MetaItem label="Effective date">{LEGAL.effectiveDate}</MetaItem>
            <MetaItem label="Last updated">{LEGAL.lastUpdated}</MetaItem>
            <MetaItem label="App name">{LEGAL.appName}</MetaItem>
            <MetaItem label="Developer / legal entity">
              <Fill>{LEGAL.developerName}</Fill>
            </MetaItem>
            <MetaItem label="Contact">
              <a href={`mailto:${LEGAL.contactEmail}`} className="text-secondary-500 hover:underline">
                {LEGAL.contactEmail}
              </a>
            </MetaItem>
          </dl>
        )}

        <div className="mt-10 grid gap-10 lg:grid-cols-[250px_minmax(0,1fr)] lg:gap-14">
          {/* Mobile / tablet: collapsible. Desktop: sticky rail. */}
          <aside className="lg:order-first">
            <details
              className="rounded-xl2 border border-primary-100 bg-white lg:hidden"
              open={tocOpen}
              onToggle={(e) => setTocOpen(e.currentTarget.open)}
            >
              <summary className="cursor-pointer select-none px-4 py-3 text-sm font-bold text-ink-900">On this page</summary>
              <div className="border-t border-primary-100 p-2">
                <SectionNav sections={sections} activeId={activeId} onNavigate={() => setTocOpen(false)} />
              </div>
            </details>

            <nav aria-label="Table of contents" className="sticky top-24 hidden max-h-[calc(100vh-7rem)] overflow-y-auto pr-2 lg:block">
              <p className="mb-2 px-2.5 text-sm font-bold text-ink-900">On this page</p>
              <SectionNav sections={sections} activeId={activeId} />
            </nav>
          </aside>

          <article className="legal-prose min-w-0 max-w-3xl">
            {sections.map((s, i) => (
              <section key={s.id} id={s.id} aria-labelledby={`${s.id}-heading`} className="scroll-mt-24">
                <h2 id={`${s.id}-heading`}>
                  <span className="mr-2 text-ink-400">{i + 1}.</span>
                  {s.title}
                </h2>
                {s.content}
              </section>
            ))}
            {children}

            <footer className="mt-14 flex flex-wrap gap-x-6 gap-y-2 border-t border-primary-100 pt-6 text-sm">
              <Link to={LEGAL.paths.privacy} className="font-semibold text-secondary-500 hover:underline">Privacy Policy</Link>
              <Link to={LEGAL.paths.terms} className="font-semibold text-secondary-500 hover:underline">Terms &amp; Conditions</Link>
              <Link to={LEGAL.paths.deleteAccount} className="font-semibold text-secondary-500 hover:underline">Delete your account</Link>
              <a href={`mailto:${LEGAL.contactEmail}`} className="font-semibold text-secondary-500 hover:underline">Contact support</a>
            </footer>
          </article>
        </div>
      </main>

      <LandingFooter />
    </div>
  );
}
