import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { ExternalLink, Search } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { listAllExamSubjects } from "../api/examTaxonomy";
import { Card, TextInput, Alert, friendlyError } from "./AdminUI";

// Read-only view, shown inside Admin > Subjects, of every exam-specific subject across all exams: the names
// admins have set per exam (renamed, merged or created for one exam only) next to the global subject each came
// from. The global list above is never changed by exam-wise edits -- this is where they become visible.
export default function ExamWiseSubjectsCard({ refreshKey }) {
  const { token } = useAdminAuth();
  const [rows, setRows] = useState(null);
  const [error, setError] = useState(null);
  const [input, setInput] = useState("");
  const [search, setSearch] = useState("");

  useEffect(() => {
    const t = setTimeout(() => setSearch(input), 300);
    return () => clearTimeout(t);
  }, [input]);

  useEffect(() => {
    let alive = true;
    listAllExamSubjects(token, search)
      .then((r) => { if (alive) { setRows(r); setError(null); } })
      .catch((e) => { if (alive) setError(friendlyError(e)); });
    return () => { alive = false; };
  }, [token, search, refreshKey]);

  // group by exam
  const groups = [];
  (rows ?? []).forEach((r) => {
    let g = groups[groups.length - 1];
    if (!g || g.examId !== r.examId) { g = { examId: r.examId, examName: r.examName, examBusinessId: r.examBusinessId, items: [] }; groups.push(g); }
    g.items.push(r);
  });

  return (
    <Card className="!p-0 overflow-hidden">
      <div className="flex flex-wrap items-center gap-3 border-b border-primary-100 p-4">
        <div className="mr-auto">
          <h2 className="text-sm font-extrabold text-ink-900">Exam-wise subject names</h2>
          <p className="text-xs text-ink-400">Subjects renamed, merged or created for a single exam. They don't change the global list above.</p>
        </div>
        <div className="relative w-full sm:w-64">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" />
          <TextInput value={input} onChange={(e) => setInput(e.target.value)} placeholder="Search exam or subject…" className="!pl-9 !py-2" aria-label="Search exam-wise subjects" />
        </div>
      </div>
      {error && <div className="p-4"><Alert>{error}</Alert></div>}
      {rows === null && !error && <p className="p-4 text-sm text-ink-400">Loading…</p>}
      {rows && groups.length === 0 && <p className="p-4 text-sm text-ink-400">{search ? "No exam-wise subjects match." : "No exam has its own subject list yet. Open Exam Subjects & Topics and initialise an exam to start."}</p>}
      <div className="divide-y divide-primary-50">
        {groups.map((g) => (
          <div key={g.examId} className="p-4">
            <div className="mb-2 flex items-center justify-between gap-2">
              <h3 className="text-sm font-bold text-ink-900">{g.examName}{g.examBusinessId ? <span className="ml-2 text-xs font-semibold text-ink-400">{g.examBusinessId}</span> : null}</h3>
              <Link to={`/admin/exam-taxonomy?exam=${g.examId}`} className="inline-flex items-center gap-1 text-xs font-semibold text-secondary-500 hover:underline">
                Manage <ExternalLink className="h-3.5 w-3.5" />
              </Link>
            </div>
            <ul className="flex flex-wrap gap-2">
              {g.items.map((r) => {
                const renamed = r.baseSubjectName && r.baseSubjectName !== r.name;
                return (
                  <li key={r.examSubjectId} className={`rounded-xl2 border px-3 py-1.5 text-xs ${r.isActive ? "border-primary-100 bg-white text-ink-900" : "border-primary-50 bg-surface text-ink-400"}`}
                    title={renamed ? `Global subject: ${r.baseSubjectName}` : undefined}>
                    <b>{r.name}</b>
                    {renamed && <span className="text-ink-400"> · global: {r.baseSubjectName}</span>}
                    {!r.baseSubjectName && <span className="text-ink-400"> · exam-only</span>}
                    <span className="text-ink-400"> · {r.topicCount} topics{r.isActive ? "" : " · archived"}</span>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </div>
    </Card>
  );
}
