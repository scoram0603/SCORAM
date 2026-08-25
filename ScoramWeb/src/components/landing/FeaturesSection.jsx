import ScrollReveal from "./ScrollReveal";
import { features } from "../../data/landingContent";

export default function FeaturesSection() {
  return (
    <section id="features" className="mx-auto max-w-7xl px-4 py-20 sm:px-6 lg:px-8 lg:py-28">
      <ScrollReveal className="mx-auto max-w-2xl text-center">
        <p className="text-sm font-semibold uppercase tracking-wide text-accent-500">Our Features</p>
        <h2 className="mt-2 text-3xl font-extrabold tracking-tight text-ink-900 sm:text-4xl">
          Everything You Need to Prepare Better
        </h2>
        <span className="mx-auto mt-4 block h-1 w-14 rounded-full bg-teal-500" />
      </ScrollReveal>

      <div className="mt-12 grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3 lg:gap-6">
        {features.map((feature, i) => (
          <ScrollReveal key={feature.title} delay={i * 60}>
            <div className="group h-full rounded-2xl border border-primary-100 bg-white p-6 shadow-card transition-all hover:-translate-y-1 hover:shadow-cardHover">
              <span className="flex h-12 w-12 items-center justify-center rounded-xl bg-primary-50 text-primary-600 transition-colors group-hover:bg-primary-600 group-hover:text-white">
                <feature.icon className="h-6 w-6" strokeWidth={2} />
              </span>
              <h3 className="mt-4 text-lg font-bold text-ink-900">{feature.title}</h3>
              <p className="mt-1.5 text-sm leading-relaxed text-ink-600">{feature.description}</p>
            </div>
          </ScrollReveal>
        ))}
      </div>
    </section>
  );
}
