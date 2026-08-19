import { useEffect, useState } from "react";
import { ChevronRight } from "lucide-react";
import { Link } from "react-router-dom";
import { listExams } from "../../api/exams";
import { API_BASE_URL } from "../../api/client";

const BADGE_TINTS = [
  "bg-accent-50 text-accent-600",
  "bg-secondary-50 text-secondary-500",
  "bg-mint-50 text-mint-500",
  "bg-violet-50 text-violet-500",
];

function badgeTintFor(name) {
  let hash = 0;
  for (let i = 0; i < name.length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash);
  return BADGE_TINTS[Math.abs(hash) % BADGE_TINTS.length];
}

function logoSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

export default function PopularExams() {
  const [exams, setExams] = useState([]);
  const [status, setStatus] = useState("loading");

  useEffect(() => {
    listExams()
      .then((data) => {
        const withQuestions = data.filter((e) => e.questionCount > 0).sort((a, b) => b.questionCount - a.questionCount);
        setExams(withQuestions.slice(0, 6));
        setStatus("ready");
      })
      .catch(() => setStatus("error"));
  }, []);

  if (status !== "ready" || exams.length === 0) return null; // nothing honest to show yet

  return (
    <section className="pb-6 sm:px-6 lg:px-0">
      <div className="mb-3 flex items-center justify-between px-4 sm:px-0 sm:mb-4">
        <h3 className="text-[17px] font-bold text-ink-900 sm:text-lg">Popular Exams</h3>
        <Link to="/search" className="flex items-center gap-0.5 text-sm font-semibold text-secondary-500">
          View All
          <ChevronRight className="h-4 w-4" strokeWidth={2.5} />
        </Link>
      </div>

      <div className="no-scrollbar flex gap-3 overflow-x-auto px-4 pb-1 sm:grid sm:grid-cols-3 sm:overflow-visible sm:px-0 sm:pb-0 lg:gap-4">
        {exams.map((exam) => (
          <Link
            key={exam.id}
            to={`/search?examId=${exam.id}`}
            className="flex w-[180px] shrink-0 items-center gap-3 rounded-xl2 border border-primary-100 bg-white p-3 text-left shadow-card transition-all hover:-translate-y-0.5 hover:shadow-cardHover sm:w-auto sm:shrink sm:p-4"
          >
            {logoSrc(exam.logoUrl) ? (
              <img src={logoSrc(exam.logoUrl)} alt="" className="h-10 w-10 shrink-0 rounded-full object-cover" />
            ) : (
              <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-sm font-bold ${badgeTintFor(exam.name)}`}>
                {exam.name.slice(0, 2).toUpperCase()}
              </span>
            )}
            <span className="min-w-0 flex-1">
              <span className="block text-sm font-bold leading-tight text-ink-900 sm:text-base">{exam.name}</span>
              <span className="mt-0.5 block text-xs text-ink-400 sm:text-sm">
                {exam.questionCount.toLocaleString("en-IN")} Questions
              </span>
            </span>
            <ChevronRight className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
          </Link>
        ))}
      </div>
    </section>
  );
}
