import { Quote } from "lucide-react";
import ScrollReveal from "./ScrollReveal";
import { testimonials } from "../../data/landingContent";

export default function TestimonialsSection() {
  return (
    <section className="bg-surface py-20 lg:py-28">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <ScrollReveal className="mx-auto max-w-2xl text-center">
          <p className="text-sm font-semibold uppercase tracking-wide text-accent-500">Student Voices</p>
          <h2 className="mt-2 text-3xl font-extrabold tracking-tight text-ink-900 sm:text-4xl">
            Students Preparing with SCORAM
          </h2>
        </ScrollReveal>

        <div className="mt-12 grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3 lg:gap-6">
          {testimonials.map((t, i) => (
            <ScrollReveal key={t.name + i} delay={i * 80}>
              <div className="flex h-full flex-col rounded-2xl border border-primary-100 bg-white p-6 shadow-card">
                <Quote className="h-7 w-7 text-primary-100" strokeWidth={2} fill="currentColor" />
                <p className="mt-3 flex-1 text-[15px] leading-relaxed text-ink-600">{t.quote}</p>
                <div className="mt-5 flex items-center gap-3 border-t border-primary-50 pt-4">
                  <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary-50 text-sm font-bold text-primary-600">
                    {t.name.split(" ").map((p) => p[0]).slice(0, 2).join("")}
                  </span>
                  <span>
                    <span className="block text-sm font-bold text-ink-900">{t.name}</span>
                    <span className="block text-xs text-ink-400">{t.exam}</span>
                  </span>
                </div>
              </div>
            </ScrollReveal>
          ))}
        </div>
      </div>
    </section>
  );
}
