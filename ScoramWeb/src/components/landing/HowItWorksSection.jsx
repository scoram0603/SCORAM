import ScrollReveal from "./ScrollReveal";
import { howItWorks } from "../../data/landingContent";

export default function HowItWorksSection() {
  return (
    <section className="bg-surface py-20 lg:py-28">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <ScrollReveal className="mx-auto max-w-2xl text-center">
          <p className="text-sm font-semibold uppercase tracking-wide text-primary-600">How It Works</p>
          <h2 className="mt-2 text-3xl font-extrabold tracking-tight text-ink-900 sm:text-4xl">
            Simple Steps to Get Started
          </h2>
        </ScrollReveal>

        {/* Desktop: horizontal timeline */}
        <div className="mt-16 hidden lg:grid lg:grid-cols-4 lg:gap-6">
          {howItWorks.map((item, i) => (
            <ScrollReveal key={item.step} delay={i * 80} className="relative">
              {i < howItWorks.length - 1 && (
                <span className="absolute left-[calc(50%+2.5rem)] top-8 h-px w-[calc(100%-5rem)] border-t-2 border-dashed border-primary-100" />
              )}
              <div className="flex flex-col items-center text-center">
                <span className="relative flex h-16 w-16 items-center justify-center rounded-full bg-white text-primary-600 shadow-card">
                  <item.icon className="h-6 w-6" strokeWidth={2.25} />
                </span>
                <span className="mt-4 text-sm font-extrabold text-accent-500">{item.step}</span>
                <h3 className="mt-1 text-base font-bold text-ink-900">{item.title}</h3>
                <p className="mt-1.5 max-w-[220px] text-sm leading-relaxed text-ink-600">{item.description}</p>
              </div>
            </ScrollReveal>
          ))}
        </div>

        {/* Mobile/tablet: vertical timeline */}
        <div className="mt-12 space-y-6 lg:hidden">
          {howItWorks.map((item, i) => (
            <ScrollReveal key={item.step} delay={i * 60} className="flex gap-4">
              <div className="flex flex-col items-center">
                <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-white text-primary-600 shadow-card">
                  <item.icon className="h-5 w-5" strokeWidth={2.25} />
                </span>
                {i < howItWorks.length - 1 && <span className="mt-2 h-full w-px border-l-2 border-dashed border-primary-100" />}
              </div>
              <div className="pb-2">
                <span className="text-xs font-extrabold text-accent-500">{item.step}</span>
                <h3 className="text-base font-bold text-ink-900">{item.title}</h3>
                <p className="mt-1 text-sm leading-relaxed text-ink-600">{item.description}</p>
              </div>
            </ScrollReveal>
          ))}
        </div>
      </div>
    </section>
  );
}
