import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { GraduationCap, ArrowRight } from "lucide-react";
import { listExams } from "../../api/exams";
import { API_BASE_URL } from "../../api/client";
import ScrollReveal from "./ScrollReveal";

const TINTS = [
  "bg-accent-50 text-accent-600",
  "bg-secondary-50 text-secondary-500",
  "bg-mint-50 text-mint-500",
  "bg-teal-50 text-teal-500",
  "bg-violet-50 text-violet-500",
];

function tintFor(name) {
  let hash = 0;
  for (let i = 0; i < name.length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash);
  return TINTS[Math.abs(hash) % TINTS.length];
}

function logoSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

// Same data source and honesty rule as the in-app PopularExams widget: only exams that actually
// have questions, sorted by question count, real exam names and logos — never a hardcoded list of
// exams that may not actually be in the database yet.
export default function PopularExamsSection() {
  const [exams, setExams] = useState([]);
  const [status, setStatus] = useState("loading");

  useEffect(() => {
    listExams()
      .then((data) => {
        const withQuestions = data.filter((e) => e.questionCount > 0).sort((a, b) => b.questionCount - a.questionCount);
        setExams(withQuestions.slice(0, 7));
        setStatus("ready");
      })
      .catch(() => setStatus("error"));
  }, []);

  if (status !== "ready" || exams.length === 0) return null;

  return (
    <section className="mx-auto max-w-7xl px-4 py-20 sm:px-6 lg:px-8 lg:py-28">
      <ScrollReveal className="mx-auto max-w-2xl text-center">
        <p className="text-sm font-semibold uppercase tracking-wide text-secondary-500">Popular Exams</p>
        <h2 className="mt-2 text-3xl font-extrabold tracking-tight text-ink-900 sm:text-4xl">
          Prepare for Top Government Exams
        </h2>
      </ScrollReveal>

      <div className="mt-10 flex flex-wrap justify-center gap-3 sm:gap-4">
        {exams.map((exam, i) => (
          <ScrollReveal key={exam.id} delay={i * 40}>
            <Link
              to={`/search?examId=${exam.id}`}
              className="flex items-center gap-2.5 rounded-full border border-primary-100 bg-white py-2.5 pl-2.5 pr-5 shadow-card transition-all hover:-translate-y-0.5 hover:shadow-cardHover"
            >
              {logoSrc(exam.logoUrl) ? (
                <img src={logoSrc(exam.logoUrl)} alt="" className="h-9 w-9 shrink-0 rounded-full object-cover" />
              ) : (
                <span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full ${tintFor(exam.name)}`}>
                  <GraduationCap className="h-4.5 w-4.5" strokeWidth={2.25} />
                </span>
              )}
              <span className="text-sm font-semibold text-ink-900">{exam.name}</span>
            </Link>
          </ScrollReveal>
        ))}

        <Link
          to="/search"
          className="flex items-center gap-1.5 rounded-full bg-primary-50 py-2.5 pl-5 pr-5 text-sm font-semibold text-primary-600 transition-colors hover:bg-primary-100"
        >
          More Exams
          <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
        </Link>
      </div>
    </section>
  );
}
