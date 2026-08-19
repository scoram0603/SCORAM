import { useEffect, useState } from "react";
import { Lightbulb, ThumbsUp, Loader2, Plus, ShieldCheck, Star, Clock } from "lucide-react";
import { getSolutions, submitSolution, upvoteSolution } from "../../api/solutions";
import { useAuth } from "../../context/AuthContext";
import { timeAgo } from "../../utils/format";

const SOLUTION_TYPES = [
  { value: "OfficialAdmin", label: "Official" },
  { value: "TeacherVerified", label: "Teacher" },
  { value: "Shortcut", label: "Shortcut" },
  { value: "Alternative", label: "Alternative" },
  { value: "Community", label: "Community" },
];

function typeLabel(value) {
  return SOLUTION_TYPES.find((t) => t.value === value)?.label || value;
}

// questionType: "paper" (default -- legacy PYQ question) or "bank" (Question Bank question).
// Routes to /api/questions/{id}/solutions vs /api/question-bank/{id}/solutions -- see api/solutions.js.
// Everything else about this component (rendering, upvoting, submit form) is identical either way,
// since both question types share the exact same QuestionSolution table/moderation queue on the backend.
export default function SolutionsPanel({ questionId, onRequireLogin, questionType = "paper" }) {
  const { isAuthenticated } = useAuth();
  const [solutions, setSolutions] = useState([]);
  const [status, setStatus] = useState("loading");
  const [activeType, setActiveType] = useState(null); // null = "All"
  const [showForm, setShowForm] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setStatus("loading");
    setActiveType(null);
    getSolutions(questionId, {}, questionType)
      .then((data) => {
        if (cancelled) return;
        setSolutions(data);
        setStatus("success");
      })
      .catch(() => {
        if (!cancelled) setStatus("error");
      });
    return () => {
      cancelled = true;
    };
  }, [questionId, questionType]);

  function refresh() {
    getSolutions(questionId, {}, questionType).then(setSolutions).catch(() => {});
  }

  async function handleUpvote(id) {
    if (!isAuthenticated) return onRequireLogin?.();
    setSolutions((prev) => prev.map((s) => (s.id === id ? { ...s, upvoteCount: s.upvoteCount + 1 } : s)));
    try {
      await upvoteSolution(id);
    } catch {
      setSolutions((prev) => prev.map((s) => (s.id === id ? { ...s, upvoteCount: s.upvoteCount - 1 } : s)));
    }
  }

  function handleAddClick() {
    if (!isAuthenticated) return onRequireLogin?.();
    setShowForm(true);
  }

  const typesPresent = [...new Set(solutions.map((s) => s.solutionType))];
  const visibleSolutions = activeType ? solutions.filter((s) => s.solutionType === activeType) : solutions;

  return (
    <section className="mt-6">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Lightbulb className="h-[18px] w-[18px] text-primary-600" strokeWidth={2.25} />
          <h2 className="text-[15px] font-bold text-ink-900">
            Solutions {status === "success" ? `(${solutions.length})` : ""}
          </h2>
        </div>
        {!showForm && (
          <button
            type="button"
            onClick={handleAddClick}
            className="flex items-center gap-1 text-xs font-semibold text-secondary-500 hover:text-secondary-600"
          >
            <Plus className="h-3.5 w-3.5" strokeWidth={2.5} />
            Add your method
          </button>
        )}
      </div>

      {/* Switch between solution types -- only shown once there's more than one type to switch between */}
      {typesPresent.length > 1 && (
        <div className="mt-3 flex flex-wrap gap-1.5">
          <TypeChip active={activeType === null} onClick={() => setActiveType(null)}>All</TypeChip>
          {typesPresent.map((t) => (
            <TypeChip key={t} active={activeType === t} onClick={() => setActiveType(t)}>{typeLabel(t)}</TypeChip>
          ))}
        </div>
      )}

      {showForm && (
        <SolutionForm
          questionId={questionId}
          questionType={questionType}
          onCancel={() => setShowForm(false)}
          onSubmitted={() => {
            setShowForm(false);
            refresh();
          }}
        />
      )}

      {status === "loading" && (
        <div className="flex justify-center py-10 text-ink-400">
          <Loader2 className="h-5 w-5 animate-spin" strokeWidth={2.25} />
        </div>
      )}

      {status === "error" && (
        <p className="mt-4 rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-600">
          Couldn't load solutions right now.
        </p>
      )}

      {status === "success" && visibleSolutions.length === 0 && (
        <p className="mt-4 rounded-xl2 border border-primary-100 bg-white p-4 text-sm text-ink-400">
          No solutions here yet — be the first to share how you'd solve it.
        </p>
      )}

      {status === "success" && visibleSolutions.length > 0 && (
        <ul className="mt-4 flex flex-col gap-3">
          {visibleSolutions.map((s) => (
            <li key={s.id}>
              <SolutionCard solution={s} onUpvote={handleUpvote} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function TypeChip({ active, onClick, children }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded-full px-3 py-1.5 text-xs font-semibold transition-colors ${
        active ? "bg-primary-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"
      }`}
    >
      {children}
    </button>
  );
}

function SolutionCard({ solution, onUpvote }) {
  return (
    <div className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card sm:p-5">
      <div className="flex flex-wrap items-center gap-1.5">
        <span className="rounded-md bg-secondary-50 px-2 py-1 text-[11px] font-semibold text-secondary-500">
          {typeLabel(solution.solutionType)}
        </span>
        {solution.isEasiestMethod && (
          <span className="flex items-center gap-1 rounded-md bg-accent-50 px-1.5 py-0.5 text-[10px] font-bold text-accent-600">
            <Star className="h-3 w-3" strokeWidth={2.5} />
            Easiest method
          </span>
        )}
        {solution.isVerified && (
          <span className="flex items-center gap-1 rounded-md bg-mint-50 px-1.5 py-0.5 text-[10px] font-bold text-mint-500">
            <ShieldCheck className="h-3 w-3" strokeWidth={2.5} />
            Verified
          </span>
        )}
        {!solution.isApproved && (
          <span className="flex items-center gap-1 rounded-md bg-primary-50 px-1.5 py-0.5 text-[10px] font-bold text-primary-600">
            <Clock className="h-3 w-3" strokeWidth={2.5} />
            Pending review — only visible to you
          </span>
        )}
      </div>

      <h3 className="mt-2 text-sm font-bold text-ink-900">{solution.title}</h3>
      <p className="mt-1 whitespace-pre-line text-sm leading-snug text-ink-600">{solution.solutionText}</p>
      {solution.imageUrl && (
        <img src={solution.imageUrl} alt="" className="mt-2 max-h-64 rounded-lg border border-primary-100" />
      )}

      <div className="mt-3 flex items-center justify-between text-xs text-ink-400">
        <span>
          {solution.submittedByAdmin ? "Scoram Team" : solution.submittedByName} · {timeAgo(solution.createdAt)}
        </span>
        <button
          type="button"
          onClick={() => onUpvote(solution.id)}
          className="flex items-center gap-1 transition-colors hover:text-secondary-500"
        >
          <ThumbsUp className="h-3.5 w-3.5" strokeWidth={2} />
          {solution.upvoteCount}
        </button>
      </div>
    </div>
  );
}

function SolutionForm({ questionId, questionType, onCancel, onSubmitted }) {
  const [title, setTitle] = useState("");
  const [solutionType, setSolutionType] = useState("Community");
  const [solutionText, setSolutionText] = useState("");
  const [imageUrl, setImageUrl] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    if (!title.trim() || !solutionText.trim()) return;
    setSubmitting(true);
    setError(null);
    try {
      await submitSolution(questionId, { title: title.trim(), solutionType, solutionText: solutionText.trim(), imageUrl: imageUrl.trim() }, questionType);
      onSubmitted();
    } catch (err) {
      setError(err.message || "Couldn't submit your solution. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="mt-3 flex flex-col gap-2.5 rounded-xl2 border border-primary-100 bg-primary-50/40 p-4">
      <p className="text-xs text-ink-400">
        Your solution goes live for everyone only after a moderator approves it — you'll see it marked "pending" until then.
      </p>

      <input
        type="text"
        required
        maxLength={150}
        value={title}
        onChange={(e) => setTitle(e.target.value)}
        placeholder="Short title, e.g. 'Using unitary method'"
        className="h-10 rounded-lg border border-primary-100 bg-white px-3 text-sm focus:border-secondary-500"
      />

      <select
        value={solutionType}
        onChange={(e) => setSolutionType(e.target.value)}
        className="h-10 rounded-lg border border-primary-100 bg-white px-3 text-sm focus:border-secondary-500"
      >
        <option value="Community">Community</option>
        <option value="Shortcut">Shortcut</option>
        <option value="Alternative">Alternative</option>
      </select>

      <textarea
        required
        rows={4}
        value={solutionText}
        onChange={(e) => setSolutionText(e.target.value)}
        placeholder="Walk through how you'd solve it..."
        className="rounded-lg border border-primary-100 bg-white px-3 py-2 text-sm focus:border-secondary-500"
      />

      <input
        type="url"
        value={imageUrl}
        onChange={(e) => setImageUrl(e.target.value)}
        placeholder="Image URL (optional)"
        className="h-10 rounded-lg border border-primary-100 bg-white px-3 text-sm focus:border-secondary-500"
      />

      {error && <p className="text-xs text-red-600">{error}</p>}

      <div className="mt-1 flex gap-2">
        <button
          type="button"
          onClick={onCancel}
          className="rounded-xl2 border border-primary-100 bg-white px-4 py-2 text-sm font-semibold text-ink-600"
        >
          Cancel
        </button>
        <button
          type="submit"
          disabled={submitting}
          className="flex items-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
        >
          {submitting && <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />}
          Submit for review
        </button>
      </div>
    </form>
  );
}
