import { Link } from "react-router-dom";
import { ArrowRight, CheckCircle2 } from "lucide-react";
import AssetImage from "./AssetImage";
import ScrollReveal from "./ScrollReveal";

const POINTS = [
  "Attempt full previous-year papers, not one question at a time",
  "Real exam pattern — same sections, same timing",
  "Instant, detailed review after every attempt",
];

export default function PYPSection() {
  return (
    <section className="bg-surface py-20 lg:py-28">
      <div className="mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-4 sm:px-6 lg:grid-cols-2 lg:gap-16 lg:px-8">
        <ScrollReveal>
          <p className="text-sm font-semibold uppercase tracking-wide text-primary-600">PYP Practice</p>
          <h2 className="mt-2 text-3xl font-extrabold tracking-tight text-ink-900 sm:text-4xl">
            Practice Real Previous Year Papers
          </h2>
          <p className="mt-4 max-w-lg text-[15px] leading-relaxed text-ink-600">
            Experience real exam patterns, previous-year questions, and timed practice to
            understand exactly what the exam demands.
          </p>
          <ul className="mt-6 space-y-3">
            {POINTS.map((point) => (
              <li key={point} className="flex items-start gap-2.5 text-sm text-ink-600">
                <CheckCircle2 className="mt-0.5 h-4.5 w-4.5 shrink-0 text-teal-500" strokeWidth={2.25} />
                {point}
              </li>
            ))}
          </ul>
          <Link
            to="/pyq"
            className="mt-7 inline-flex items-center gap-2 rounded-xl bg-primary-600 px-6 py-3.5 text-[15px] font-semibold text-white shadow-floating transition-transform hover:-translate-y-0.5 hover:bg-primary-700"
          >
            Explore PYP Practice
            <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
          </Link>
        </ScrollReveal>

        <ScrollReveal delay={120} className="mx-auto w-full max-w-sm">
          <AssetImage
            src="/assets/landing/pyp-preview.png"
            alt="SCORAM previous year paper practice interface showing a timed full paper attempt"
            label="pyp-preview.png"
            aspect="aspect-[4/5]"
            className="w-full rounded-2xl border border-primary-100 object-cover shadow-cardHover"
          />
        </ScrollReveal>
      </div>
    </section>
  );
}
