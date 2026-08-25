import { FileCheck2, GraduationCap, Zap, Users2 } from "lucide-react";
import AssetImage from "./AssetImage";
import ScrollReveal from "./ScrollReveal";

const METHODS = [
  { icon: FileCheck2, title: "Official Solution", tint: "bg-primary-50 text-primary-600" },
  { icon: GraduationCap, title: "Teacher's Method", tint: "bg-teal-50 text-teal-500" },
  { icon: Zap, title: "Shortcut / Trick", tint: "bg-accent-50 text-accent-500" },
  { icon: Users2, title: "Community Method", tint: "bg-violet-50 text-violet-500" },
];

export default function SolutionsSection() {
  return (
    <section className="bg-gradient-to-br from-primary-700 to-primary-900 py-20 text-white lg:py-28">
      <div className="mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-4 sm:px-6 lg:grid-cols-2 lg:gap-16 lg:px-8">
        <ScrollReveal>
          <p className="text-sm font-semibold uppercase tracking-wide text-accent-500">
            Multiple Solving Methods
          </p>
          <h2 className="mt-2 text-3xl font-extrabold tracking-tight sm:text-4xl">
            One Question. Multiple Ways to Solve.
          </h2>
          <p className="mt-4 max-w-lg text-[15px] leading-relaxed text-white/75">
            Understand the official answer, then see an alternative teaching method and a
            time-saving shortcut — so you learn the concept, not just the answer.
          </p>

          <div className="mt-7 grid grid-cols-2 gap-3">
            {METHODS.map((method) => (
              <div key={method.title} className="flex items-center gap-3 rounded-xl bg-white/10 px-4 py-3.5 backdrop-blur-sm">
                <span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg ${method.tint}`}>
                  <method.icon className="h-4.5 w-4.5" strokeWidth={2.25} />
                </span>
                <span className="text-sm font-semibold">{method.title}</span>
              </div>
            ))}
          </div>
        </ScrollReveal>

        <ScrollReveal delay={120} className="mx-auto w-full max-w-sm">
          <AssetImage
            src="/assets/landing/solutions-preview.png"
            alt="SCORAM interface comparing multiple solving methods for the same question"
            label="solutions-preview.png"
            aspect="aspect-[4/5]"
            className="w-full rounded-2xl object-cover shadow-cardHover"
          />
        </ScrollReveal>
      </div>
    </section>
  );
}
