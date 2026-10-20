import { Link } from "react-router-dom";
import { ArrowRight, BarChart3 } from "lucide-react";
import ScrollReveal from "./ScrollReveal";
import { useLandingContent } from "../../hooks/useLandingContent";
import { useSlotImage } from "../../hooks/useSlotImage";
import { resolveSmartProgress, SMART_PROGRESS_FOOTNOTE } from "../../data/smartProgressDefaults";

// SMART PROGRESS -- landing section. Copy, highlights, CTA, image and visibility are editable under Admin > Landing
// Page; if nothing is configured (or the API call fails) the safe defaults in data/smartProgressDefaults.js render.
// The image is OPTIONAL: with none configured (there is deliberately no bundled screenshot -- a fabricated one would
// misrepresent the product) the section simply renders as a text + highlights layout.
export default function SmartProgressSection() {
  const { smartProgress } = useLandingContent();
  const content = resolveSmartProgress(smartProgress);
  const image = useSlotImage("smart-progress-image");

  if (!content) return null;

  const hasImage = !image.exhausted;
  const external = content.ctaLink.startsWith("http");

  return (
    <section id="smart-progress" className="bg-white py-20 lg:py-28" aria-labelledby="smart-progress-title">
      <div
        className={`mx-auto px-4 sm:px-6 lg:px-8 ${
          hasImage ? "grid max-w-7xl grid-cols-1 items-center gap-12 lg:grid-cols-2 lg:gap-16" : "max-w-4xl"
        }`}
      >
        <ScrollReveal>
          <p className="flex items-center gap-2 text-sm font-semibold uppercase tracking-wide text-teal-500">
            <BarChart3 className="h-4 w-4" strokeWidth={2.5} aria-hidden="true" />
            Smart Progress
          </p>
          <h2 id="smart-progress-title" className="mt-2 text-3xl font-extrabold tracking-tight text-ink-900 sm:text-4xl">
            {content.heading}
          </h2>
          <p className="mt-4 max-w-xl whitespace-pre-line text-[15px] leading-relaxed text-ink-600">{content.description}</p>

          {content.highlights.length > 0 && (
            <ul className={`mt-6 grid gap-3 ${hasImage ? "grid-cols-1" : "sm:grid-cols-2"}`}>
              {content.highlights.map((item) => (
                <li key={item} className="flex items-start gap-3 rounded-xl border border-primary-100 bg-surface px-4 py-3 text-sm font-medium text-ink-900">
                  <span className="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-teal-500" aria-hidden="true" />
                  <span className="min-w-0 break-words">{item}</span>
                </li>
              ))}
            </ul>
          )}

          <p className="mt-4 text-xs text-ink-400">{SMART_PROGRESS_FOOTNOTE}</p>

          {content.ctaText && (
            external ? (
              <a
                href={content.ctaLink}
                target="_blank"
                rel="noopener noreferrer"
                className="mt-6 inline-flex items-center gap-2 rounded-xl2 bg-primary-600 px-6 py-3 text-sm font-semibold text-white transition-colors hover:bg-primary-700"
              >
                {content.ctaText}
                <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
              </a>
            ) : (
              <Link
                to={content.ctaLink}
                className="mt-6 inline-flex items-center gap-2 rounded-xl2 bg-primary-600 px-6 py-3 text-sm font-semibold text-white transition-colors hover:bg-primary-700"
              >
                {content.ctaText}
                <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
              </Link>
            )
          )}
        </ScrollReveal>

        {hasImage && (
          <ScrollReveal delay={120} className="mx-auto w-full max-w-md">
            <img
              key={image.src}
              src={image.src}
              alt="SCORAM Smart Progress screen showing practice performance insights"
              loading="lazy"
              decoding="async"
              onError={image.onError}
              className="aspect-[4/3] w-full rounded-2xl border border-primary-100 object-cover shadow-cardHover"
            />
          </ScrollReveal>
        )}
      </div>
    </section>
  );
}
