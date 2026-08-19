import { ArrowRight, Target, ClipboardCheck, BookOpen, MessageCircle, Trophy } from "lucide-react";
import { heroStats } from "../../data/mockData";

const STAT_ICONS = { BookOpen, MessageCircle, ClipboardCheck, Trophy };

export default function HeroBanner() {
  return (
    <div className="px-4 pb-5 sm:px-6 lg:px-8 lg:pb-6 lg:pt-2">
      {/* Mobile / tablet hero — compact */}
      <div className="relative overflow-hidden rounded-xl2 bg-gradient-to-br from-primary-600 to-primary-900 px-5 py-6 text-white shadow-card lg:hidden">
        <Target className="pointer-events-none absolute -right-6 -top-6 h-32 w-32 text-white/10" strokeWidth={1.25} />
        <div className="relative max-w-[260px]">
          <h2 className="text-xl font-extrabold leading-tight sm:text-2xl">
            Crack <span className="text-accent-500">SSC & Railway</span> with Confidence
          </h2>
          <p className="mt-2 text-[13px] leading-snug text-white/80 sm:text-sm">
            PYQs • Mock Tests • Smart Discussions
          </p>
          <button
            type="button"
            className="mt-4 inline-flex items-center gap-1.5 rounded-full bg-accent-500 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-accent-600"
          >
            Start Practicing
            <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
          </button>
        </div>
      </div>

      {/* Desktop hero — 220px tall, headline + illustration + stats rail */}
      <div className="relative hidden overflow-hidden rounded-xl2 bg-gradient-to-br from-primary-600 to-primary-900 text-white shadow-card lg:flex lg:h-[220px] lg:items-stretch">
        <div className="flex flex-1 items-center gap-8 px-10">
          <div className="max-w-md">
            <h2 className="text-3xl font-extrabold leading-tight">
              Master <span className="text-accent-500">Every Topic</span> with Multiple Solving Methods
            </h2>
            <p className="mt-3 text-[15px] leading-snug text-white/80">
              See the official answer, a teacher's method, and a 10-second community trick — side by side.
            </p>
            <button
              type="button"
              className="mt-5 inline-flex items-center gap-1.5 rounded-full bg-accent-500 px-5 py-3 text-sm font-semibold text-white transition-colors hover:bg-accent-600"
            >
              Explore Solutions
              <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
            </button>
          </div>

          {/* Decorative illustration: target + clipboard, echoing the "score/accuracy" theme */}
          <div className="relative hidden h-full flex-1 items-center justify-center xl:flex">
            <ClipboardCheck className="h-24 w-24 -rotate-6 text-white/15" strokeWidth={1.25} />
            <Target className="absolute h-20 w-20 translate-x-10 translate-y-4 text-accent-500/40" strokeWidth={1.25} />
          </div>
        </div>

        {/* 4 vertical stats, floating directly on the gradient */}
        <div className="flex w-64 shrink-0 flex-col justify-center gap-4 px-8">
          {heroStats.map((stat) => {
            const Icon = STAT_ICONS[stat.icon];
            return (
              <div key={stat.label} className="flex items-center gap-3">
                <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-accent-500/20 text-accent-500">
                  <Icon className="h-4 w-4" strokeWidth={2.25} />
                </span>
                <span>
                  <span className="block text-lg font-extrabold leading-none">{stat.value}</span>
                  <span className="block text-xs text-white/70">{stat.label}</span>
                </span>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
