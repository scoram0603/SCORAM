import { Link } from "react-router-dom";
import { ArrowRight, BookOpenCheck } from "lucide-react";
import AssetImage from "./AssetImage";
import ScrollReveal from "./ScrollReveal";

const HIGHLIGHTS = [
  "Topic-wise preparation",
  "Exam-wise questions",
  "Previous year questions",
  "Multiple solving methods",
  "Discussions on every question",
];

const FILTER_CHIPS = ["Subject", "Topic", "Exam", "Year"];

export default function QuestionBankSection() {
  return (
    <section className="py-20 lg:py-28">
      <div className="mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-4 sm:px-6 lg:grid-cols-2 lg:gap-16 lg:px-8">
        <ScrollReveal className="order-2 lg:order-1 mx-auto w-full max-w-sm">
          <div className="rounded-2xl border border-primary-100 bg-white p-4 shadow-cardHover">
            <div className="mb-3 flex flex-wrap gap-2">
              {FILTER_CHIPS.map((chip) => (
                <span
                  key={chip}
                  className="rounded-full border border-primary-100 bg-primary-50 px-3 py-1 text-xs font-semibold text-primary-600"
                >
                  {chip}
                </span>
              ))}
            </div>
            <AssetImage
              src="/assets/landing/question-bank-preview.png"
              alt="SCORAM competitive exam question bank interface with subject and topic filters"
              label="question-bank-preview.png"
              aspect="aspect-[4/5]"
              className="w-full rounded-xl object-cover"
            />
          </div>
        </ScrollReveal>

        <ScrollReveal delay={120} className="order-1 lg:order-2">
          <p className="text-sm font-semibold uppercase tracking-wide text-accent-500">PYQs</p>
          <h2 className="mt-2 text-3xl font-extrabold tracking-tight text-ink-900 sm:text-4xl">
            Master Every Topic with PYQs
          </h2>
          <p className="mt-4 max-w-lg text-[15px] leading-relaxed text-ink-600">
            Filter thousands of questions by subject, topic, exam, or year — and see how each one
            connects to the exam you're actually preparing for.
          </p>
          <div className="mt-6 grid grid-cols-1 gap-2.5 sm:grid-cols-2">
            {HIGHLIGHTS.map((point) => (
              <div key={point} className="flex items-center gap-2.5 rounded-xl bg-surface px-3.5 py-2.5 text-sm font-medium text-ink-600">
                <BookOpenCheck className="h-4 w-4 shrink-0 text-primary-500" strokeWidth={2.25} />
                {point}
              </div>
            ))}
          </div>
          <Link
            to="/question-bank"
            className="mt-7 inline-flex items-center gap-2 rounded-xl bg-primary-600 px-6 py-3.5 text-[15px] font-semibold text-white shadow-floating transition-transform hover:-translate-y-0.5 hover:bg-primary-700"
          >
            Explore PYQs
            <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
          </Link>
        </ScrollReveal>
      </div>
    </section>
  );
}
