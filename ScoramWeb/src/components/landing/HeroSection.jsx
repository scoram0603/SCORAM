import { Link } from "react-router-dom";
import { ArrowRight, Compass, Target, LayoutGrid, Layers3 } from "lucide-react";
import AssetImage from "./AssetImage";
import ScrollReveal from "./ScrollReveal";
import { formatCount } from "../../utils/format";

// Floating stat cards around the phone mockup. Questions/Exams are live, real counts from
// GET /api/public-stats (see src/api/publicStats.js) -- never hardcoded marketing numbers.
// "Multiple Solving Methods" is a feature descriptor, not a claimed statistic, so it's static.
function FloatingStat({ icon: Icon, value, label, className }) {
  return (
    <div
      className={`absolute flex items-center gap-3 rounded-2xl bg-white px-4 py-3 shadow-cardHover ${className}`}
    >
      <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-accent-50 text-accent-500">
        <Icon className="h-5 w-5" strokeWidth={2.25} />
      </span>
      <span>
        <span className="block text-base font-extrabold leading-none text-ink-900">{value}</span>
        <span className="block text-xs font-medium text-ink-400">{label}</span>
      </span>
    </div>
  );
}

export default function HeroSection({ stats }) {
  const questionsLabel = stats ? `${formatCount(stats.totalQuestions)}+` : "—";
  const examsLabel = stats ? `${stats.totalExams}+` : "—";
  const studentsLabel = stats ? `${formatCount(stats.totalStudents)}+` : "—";

  return (
    <section className="relative overflow-hidden bg-gradient-to-b from-primary-50/60 via-white to-white">
      <div className="mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-4 pb-16 pt-10 sm:px-6 lg:grid-cols-2 lg:gap-8 lg:px-8 lg:pb-24 lg:pt-16">
        <ScrollReveal>
          <span className="inline-flex items-center gap-1.5 rounded-full bg-secondary-50 px-3.5 py-1.5 text-xs font-semibold text-secondary-600">
            <Compass className="h-3.5 w-3.5" strokeWidth={2.5} />
            India's Smart Learning Platform
          </span>

          <h1 className="mt-5 text-[2.5rem] font-extrabold leading-[1.08] tracking-tight sm:text-5xl lg:text-[3.4rem]">
            <span className="text-primary-700">Learn Better.</span>
            <br />
            <span className="text-accent-500">Discuss Freely.</span>
            <br />
            <span className="text-teal-500">Score Higher.</span>
          </h1>

          <p className="mt-5 max-w-md text-[15px] leading-relaxed text-ink-600 sm:text-base">
            Your all-in-one platform to practice, learn, discuss, and prepare smarter for
            competitive exams.
          </p>

          <div className="mt-7 flex flex-wrap items-center gap-3">
            <Link
              to="/login?mode=register"
              className="flex items-center gap-2 rounded-xl bg-primary-600 px-6 py-3.5 text-[15px] font-semibold text-white shadow-floating transition-transform hover:-translate-y-0.5 hover:bg-primary-700"
            >
              Start Learning Now
              <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
            </Link>
            <a
              href="#features"
              className="flex items-center gap-2 rounded-xl border border-primary-100 bg-white px-6 py-3.5 text-[15px] font-semibold text-primary-600 transition-colors hover:bg-primary-50"
            >
              Explore Features
            </a>
          </div>

          {stats && stats.totalStudents > 0 && (
            <p className="mt-6 text-sm text-ink-400">
              Join{" "}
              <span className="font-semibold text-ink-900">
                {formatCount(stats.totalStudents)}+ students
              </span>{" "}
              already learning and growing with SCORAM.
            </p>
          )}
        </ScrollReveal>

        <ScrollReveal delay={120} className="relative mx-auto w-full max-w-[340px] lg:max-w-[380px]">
          {/* Decorative backdrop shape */}
          <div className="absolute -inset-8 -z-10 rounded-[3rem] bg-gradient-to-br from-primary-600 to-primary-900 opacity-95 lg:-inset-10" />

          <div className="relative mx-auto aspect-[9/18.5] w-full max-w-[300px] overflow-hidden rounded-[2.5rem] border-[6px] border-primary-900 bg-white shadow-cardHover">
            <AssetImage
              src="/assets/landing/scoram-app-preview.png"
              alt="SCORAM mobile app home screen showing quick access to PYP practice, question bank, tests and mock tests"
              label="scoram-app-preview.png"
              aspect="aspect-[9/18.5]"
              className="h-full w-full object-cover"
            />
          </div>

          <FloatingStat
            icon={Layers3}
            value={questionsLabel}
            label="Questions"
            className="-left-4 top-6 hidden sm:flex lg:-left-10"
          />
          <FloatingStat
            icon={LayoutGrid}
            value={examsLabel}
            label="Exams Covered"
            className="-right-2 top-1/3 hidden sm:flex lg:-right-8"
          />
          <FloatingStat
            icon={Target}
            value="Multiple"
            label="Solving Methods"
            className="-left-2 bottom-8 hidden sm:flex lg:-left-6"
          />

          {/* Compact mobile-only stat row (floating cards above are hidden below sm) */}
          <div className="mt-6 grid grid-cols-3 gap-2 sm:hidden">
            <div className="rounded-xl bg-white px-2 py-3 text-center shadow-card">
              <p className="text-sm font-extrabold text-ink-900">{questionsLabel}</p>
              <p className="text-[11px] text-ink-400">Questions</p>
            </div>
            <div className="rounded-xl bg-white px-2 py-3 text-center shadow-card">
              <p className="text-sm font-extrabold text-ink-900">{examsLabel}</p>
              <p className="text-[11px] text-ink-400">Exams</p>
            </div>
            <div className="rounded-xl bg-white px-2 py-3 text-center shadow-card">
              <p className="text-sm font-extrabold text-ink-900">{studentsLabel}</p>
              <p className="text-[11px] text-ink-400">Students</p>
            </div>
          </div>
        </ScrollReveal>
      </div>
    </section>
  );
}
