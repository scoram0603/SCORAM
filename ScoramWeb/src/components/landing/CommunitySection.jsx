import { Link } from "react-router-dom";
import { ArrowRight, MessageSquare, Send, HelpCircle, Trophy } from "lucide-react";
import AssetImage from "./AssetImage";
import ScrollReveal from "./ScrollReveal";

const HIGHLIGHTS = [
  { icon: MessageSquare, label: "Group discussions" },
  { icon: Send, label: "Direct messages" },
  { icon: HelpCircle, label: "Question discussions" },
  { icon: Trophy, label: "Leaderboards" },
];

export default function CommunitySection() {
  return (
    <section className="py-20 lg:py-28">
      <div className="mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-4 sm:px-6 lg:grid-cols-2 lg:gap-16 lg:px-8">
        <ScrollReveal className="mx-auto w-full max-w-sm lg:order-1">
          <AssetImage
            src="/assets/landing/community-preview.png"
            alt="SCORAM student discussion and learning community interface"
            label="community-preview.png"
            aspect="aspect-[4/5]"
            className="w-full rounded-2xl border border-primary-100 object-cover shadow-cardHover"
          />
        </ScrollReveal>

        <ScrollReveal delay={120} className="lg:order-2">
          <p className="text-sm font-semibold uppercase tracking-wide text-teal-500">Community</p>
          <h2 className="mt-2 text-3xl font-extrabold tracking-tight text-ink-900 sm:text-4xl">
            Don't Just Study Alone.
          </h2>
          <p className="mt-4 max-w-lg text-[15px] leading-relaxed text-ink-600">
            Learn, discuss, and solve problems together with thousands of students preparing for
            the same exams as you.
          </p>
          <div className="mt-6 grid grid-cols-2 gap-3">
            {HIGHLIGHTS.map((item) => (
              <div key={item.label} className="flex items-center gap-2.5 rounded-xl bg-surface px-3.5 py-3 text-sm font-medium text-ink-600">
                <item.icon className="h-4.5 w-4.5 shrink-0 text-teal-500" strokeWidth={2.25} />
                {item.label}
              </div>
            ))}
          </div>
          <Link
            to="/chat"
            className="mt-7 inline-flex items-center gap-2 rounded-xl bg-teal-500 px-6 py-3.5 text-[15px] font-semibold text-white shadow-floating transition-transform hover:-translate-y-0.5 hover:bg-teal-500/90"
          >
            Join the Community
            <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
          </Link>
        </ScrollReveal>
      </div>
    </section>
  );
}
