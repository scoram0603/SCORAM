import { Link } from "react-router-dom";
import { ArrowRight, GraduationCap, Sparkles } from "lucide-react";

export default function FinalCTA() {
  return (
    <section className="relative overflow-hidden bg-gradient-to-br from-primary-700 to-primary-900 px-4 py-16 sm:px-6 lg:px-8">
      <GraduationCap className="pointer-events-none absolute -left-8 -top-8 h-40 w-40 text-white/5" strokeWidth={1} />
      <Sparkles className="pointer-events-none absolute -bottom-6 -right-6 h-36 w-36 text-accent-500/10" strokeWidth={1} />

      <div className="relative mx-auto flex max-w-6xl flex-col items-center justify-between gap-6 rounded-2xl bg-white/[0.06] px-6 py-10 text-center backdrop-blur-sm sm:px-10 lg:flex-row lg:text-left">
        <div>
          <h2 className="text-2xl font-extrabold text-white sm:text-3xl">Ready to Score Higher?</h2>
          <p className="mt-2 text-[15px] text-white/75">
            Start your preparation journey with SCORAM today.
          </p>
        </div>
        <div className="flex w-full flex-col gap-3 sm:w-auto sm:flex-row">
          <Link
            to="/login?mode=register"
            className="flex items-center justify-center gap-2 rounded-xl bg-accent-500 px-6 py-3.5 text-[15px] font-semibold text-white shadow-floating transition-transform hover:-translate-y-0.5 hover:bg-accent-600"
          >
            Create Free Account
            <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
          </Link>
          <a
            href="#top"
            className="flex items-center justify-center gap-2 rounded-xl border border-white/30 px-6 py-3.5 text-[15px] font-semibold text-white transition-colors hover:bg-white/10"
          >
            Explore SCORAM
          </a>
        </div>
      </div>
    </section>
  );
}
