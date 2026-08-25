import { Smartphone, Wifi, BellRing } from "lucide-react";
import AssetImage from "./AssetImage";
import ScrollReveal from "./ScrollReveal";

const POINTS = [
  { icon: Smartphone, label: "Practice on the go, phone or tablet" },
  { icon: Wifi, label: "Works smoothly even on slower connections" },
  { icon: BellRing, label: "Get notified about streaks and new tests" },
];

export default function AppPromotionSection() {
  return (
    <section className="mx-auto max-w-7xl px-4 py-20 sm:px-6 lg:px-8 lg:py-28">
      <div className="grid grid-cols-1 items-center gap-12 lg:grid-cols-2 lg:gap-16">
        <ScrollReveal className="order-2 mx-auto w-full max-w-xs lg:order-1">
          <AssetImage
            src="/assets/landing/mobile-app-preview.png"
            alt="SCORAM mobile application preview on a smartphone"
            label="mobile-app-preview.png"
            aspect="aspect-[9/16]"
            className="w-full rounded-[2rem] border-4 border-primary-900 object-cover shadow-cardHover"
          />
        </ScrollReveal>

        <ScrollReveal delay={120} className="order-1 lg:order-2">
          <p className="text-sm font-semibold uppercase tracking-wide text-teal-500">SCORAM App</p>
          <h2 className="mt-2 text-3xl font-extrabold tracking-tight text-ink-900 sm:text-4xl">
            Take Your Preparation Anywhere.
          </h2>
          <p className="mt-4 max-w-lg text-[15px] leading-relaxed text-ink-600">
            SCORAM's web app works great on mobile browsers today, with a dedicated app on the way
            for an even smoother experience.
          </p>
          <ul className="mt-6 space-y-3">
            {POINTS.map((point) => (
              <li key={point.label} className="flex items-center gap-3 text-sm font-medium text-ink-600">
                <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-teal-50 text-teal-500">
                  <point.icon className="h-4.5 w-4.5" strokeWidth={2.25} />
                </span>
                {point.label}
              </li>
            ))}
          </ul>
          <a
            href="#top"
            className="mt-7 inline-flex items-center gap-2 rounded-xl bg-primary-600 px-6 py-3.5 text-[15px] font-semibold text-white shadow-floating transition-transform hover:-translate-y-0.5 hover:bg-primary-700"
          >
            Get the SCORAM App
          </a>
          {/* CTA currently scrolls to top -- point this at the real app-store / play-store links
              once the app ships. See LANDING_REPORT.md. */}
        </ScrollReveal>
      </div>
    </section>
  );
}
