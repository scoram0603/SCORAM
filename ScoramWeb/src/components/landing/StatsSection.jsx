import { Users, Layers3, LayoutGrid, Sparkles } from "lucide-react";
import ScrollReveal from "./ScrollReveal";
import { formatCount } from "../../utils/format";

export default function StatsSection({ stats }) {
  const items = [
    { icon: Users, value: stats ? `${formatCount(stats.totalStudents)}+` : "—", label: "Active Students" },
    { icon: Layers3, value: stats ? `${formatCount(stats.totalQuestions)}+` : "—", label: "Questions" },
    { icon: LayoutGrid, value: stats ? `${stats.totalExams}+` : "—", label: "Exams Covered" },
    { icon: Sparkles, value: "Multiple", label: "Solving Methods" },
  ];

  return (
    <section className="px-4 sm:px-6 lg:px-8">
      <ScrollReveal className="mx-auto -mt-8 max-w-6xl rounded-2xl border border-primary-100 bg-white p-6 shadow-cardHover sm:p-8 lg:-mt-10">
        <div className="grid grid-cols-2 gap-6 sm:grid-cols-4 sm:gap-4">
          {items.map((item) => (
            <div key={item.label} className="flex flex-col items-center gap-2 text-center sm:flex-row sm:text-left">
              <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-teal-50 text-teal-500">
                <item.icon className="h-5 w-5" strokeWidth={2.25} />
              </span>
              <span>
                <span className="block text-xl font-extrabold text-ink-900 sm:text-2xl">{item.value}</span>
                <span className="block text-xs font-medium text-ink-400 sm:text-sm">{item.label}</span>
              </span>
            </div>
          ))}
        </div>
      </ScrollReveal>
    </section>
  );
}
