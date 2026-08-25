import { useState } from "react";
import { ChevronDown } from "lucide-react";
import ScrollReveal from "./ScrollReveal";
import { faqs } from "../../data/landingContent";

function FaqItem({ item, isOpen, onToggle }) {
  return (
    <div className="rounded-2xl border border-primary-100 bg-white shadow-card">
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={isOpen}
        className="flex w-full items-center justify-between gap-4 px-5 py-4 text-left sm:px-6 sm:py-5"
      >
        <span className="text-[15px] font-semibold text-ink-900">{item.question}</span>
        <ChevronDown
          className={`h-5 w-5 shrink-0 text-primary-500 transition-transform ${isOpen ? "rotate-180" : ""}`}
          strokeWidth={2.25}
        />
      </button>
      <div
        className={`grid overflow-hidden transition-all duration-300 ${
          isOpen ? "grid-rows-[1fr] opacity-100" : "grid-rows-[0fr] opacity-0"
        }`}
      >
        <div className="overflow-hidden px-5 pb-5 text-sm leading-relaxed text-ink-600 sm:px-6">
          {item.answer}
        </div>
      </div>
    </div>
  );
}

export default function FAQSection() {
  const [openIndex, setOpenIndex] = useState(0);

  return (
    <section id="faq" className="mx-auto max-w-4xl px-4 py-20 sm:px-6 lg:px-8 lg:py-28">
      <ScrollReveal className="mx-auto max-w-2xl text-center">
        <p className="text-sm font-semibold uppercase tracking-wide text-primary-600">FAQ</p>
        <h2 className="mt-2 text-3xl font-extrabold tracking-tight text-ink-900 sm:text-4xl">
          Frequently Asked Questions
        </h2>
      </ScrollReveal>

      <div className="mt-10 space-y-3">
        {faqs.map((item, i) => (
          <ScrollReveal key={item.question} delay={i * 40}>
            <FaqItem
              item={item}
              isOpen={openIndex === i}
              onToggle={() => setOpenIndex(openIndex === i ? -1 : i)}
            />
          </ScrollReveal>
        ))}
      </div>
    </section>
  );
}
