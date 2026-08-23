import { useEffect, useState } from "react";
import { useNavigate, useParams, useLocation } from "react-router-dom";
import {
  ArrowLeft, Loader2, FileQuestion, Clock, MinusCircle, CheckCircle2, ListChecks,
  ClipboardList, Play, AlertTriangle, Square, CheckSquare,
} from "lucide-react";
import { loadBriefing, confirmStart, MISSING_STATE_ERROR } from "../utils/examBriefing";

// Universal "Exam Briefing" screen (spec: "Pre-Exam Instructions UI"). One reusable page for all 6
// start-kinds (paper/mock/practice-template/practice-adhoc/quiz-daily/quiz-weak) -- see
// utils/examBriefing.js for what actually differs between them. This component only ever calls
// loadBriefing (read-only metadata fetch) until the student explicitly confirms, at which point it
// calls confirmStart -- the SAME start function each page already called directly -- and hands off
// to the existing, untouched /tests/attempt/:attemptId flow. No timer, scoring, or question-loading
// logic is duplicated here.
export default function PreExamInstructions() {
  const { kind, id } = useParams();
  const location = useLocation();
  const navigate = useNavigate();

  const [briefing, setBriefing] = useState(null);
  const [status, setStatus] = useState("loading");
  const [confirmed, setConfirmed] = useState(false);
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState("");

  useEffect(() => {
    setStatus("loading");
    loadBriefing(kind, id, location.state)
      .then((b) => {
        setBriefing(b);
        setStatus("success");
      })
      .catch((err) => {
        setStatus(err?.code === MISSING_STATE_ERROR ? "missing-state" : "error");
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [kind, id]);

  async function handleStart() {
    setStartError("");
    setStarting(true);
    try {
      const attempt = await confirmStart(kind, id, { ...location.state, ...briefing.state });
      navigate(`/tests/attempt/${attempt.attemptId}`);
    } catch (err) {
      setStartError(err.message || "Couldn't start this attempt right now.");
      setStarting(false);
    }
  }

  if (status === "loading") {
    return (
      <div className="flex min-h-[60vh] items-center justify-center text-ink-400">
        <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
      </div>
    );
  }

  if (status === "missing-state") {
    return (
      <EdgeCaseScreen
        title="Let's set that up again"
        message="This test was generated from choices made on the previous page, and we lost track of them (maybe you refreshed, or opened this link directly). Head back and choose again -- it only takes a second."
        backLabel="Choose again"
        backTo={-1}
      />
    );
  }

  if (status === "error" || !briefing) {
    return (
      <EdgeCaseScreen
        title="Test unavailable"
        message="We couldn't find this test, or it's no longer available."
        backLabel="Back"
        backTo={-1}
      />
    );
  }

  if (briefing.unavailable) {
    return (
      <EdgeCaseScreen
        title="Not available right now"
        message={briefing.reason}
        backLabel="Back"
        backTo={briefing.backTo}
      />
    );
  }

  const hasNegativeMarking = Boolean(briefing.negativeMarkingRatio);
  const instructionsList = briefing.instructions
    ? briefing.instructions.split("\n").map((s) => s.trim()).filter(Boolean)
    : buildGenericInstructions(briefing, hasNegativeMarking);

  return (
    <div className="mx-auto max-w-2xl px-4 pb-28 pt-4 sm:px-6 lg:pt-6">
      <button
        type="button"
        onClick={() => navigate(briefing.backTo)}
        className="flex items-center gap-1.5 text-sm font-semibold text-secondary-500 hover:text-secondary-600"
      >
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
        Back
      </button>

      {/* ---------- Header ---------- */}
      <div className="mt-4 animate-fade-in rounded-xl2 border border-primary-100 bg-white p-5 shadow-card sm:p-6">
        <span className="inline-flex items-center rounded-full bg-primary-600 px-2.5 py-1 text-[11px] font-bold uppercase tracking-wide text-white">
          {briefing.badge}
        </span>
        <p className="mt-2 text-xs font-bold uppercase tracking-wide text-secondary-500">{briefing.typeLabel}</p>
        <h1 className="mt-1 text-xl font-extrabold text-ink-900 sm:text-2xl">{briefing.title}</h1>
        {briefing.subtitle && <p className="mt-0.5 text-sm text-ink-400">{briefing.subtitle}</p>}

        {/* ---------- Stat cards ---------- */}
        <div className="mt-5 grid grid-cols-3 gap-2.5 sm:gap-3">
          <StatCard icon={FileQuestion} value={briefing.questionCount} label="Questions" />
          <StatCard icon={Clock} value={`${briefing.durationMinutes} min`} label="Time Limit" />
          {hasNegativeMarking ? (
            <StatCard icon={MinusCircle} value={`-${briefing.negativeMarkingRatio}`} label="Negative Marking" />
          ) : (
            <StatCard icon={MinusCircle} value="None" label="Negative Marking" muted />
          )}
        </div>
      </div>

      {/* ---------- What to expect ---------- */}
      {briefing.whatToExpect?.length > 0 && (
        <div className="mt-4 rounded-xl2 border border-primary-100 bg-white p-5 shadow-card">
          <p className="flex items-center gap-1.5 text-sm font-bold text-ink-900">
            <ListChecks className="h-4 w-4 text-secondary-500" strokeWidth={2.25} />
            What you'll experience
          </p>
          <ul className="mt-2.5 flex flex-col gap-1.5">
            {briefing.whatToExpect.map((item) => (
              <li key={item} className="flex items-start gap-2 text-sm text-ink-600">
                <CheckCircle2 className="mt-0.5 h-3.5 w-3.5 shrink-0 text-mint-500" strokeWidth={2.25} />
                {item}
              </li>
            ))}
          </ul>
        </div>
      )}

      {/* ---------- Test Details ---------- */}
      {briefing.details?.length > 0 && (
        <div className="mt-4 rounded-xl2 border border-primary-100 bg-white p-5 shadow-card">
          <p className="flex items-center gap-1.5 text-sm font-bold text-ink-900">
            <ClipboardList className="h-4 w-4 text-secondary-500" strokeWidth={2.25} />
            Test Details
          </p>
          <div className="mt-2.5 flex flex-col divide-y divide-primary-50">
            {briefing.details.map((d) => (
              <div key={d.label} className="flex items-center justify-between gap-3 py-2 text-sm">
                <span className="text-ink-400">{d.label}</span>
                <span className="text-right font-semibold text-ink-900">{d.value}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ---------- Instructions ---------- */}
      <div className="mt-4 rounded-xl2 border border-primary-100 bg-white p-5 shadow-card">
        <p className="text-sm font-bold text-ink-900">Important Instructions</p>
        <ol className="mt-2.5 flex flex-col gap-2">
          {instructionsList.map((item, i) => (
            <li key={i} className="flex gap-2 text-sm text-ink-600">
              <span className="font-bold text-ink-300">{i + 1}.</span>
              {item}
            </li>
          ))}
        </ol>
      </div>

      {startError && (
        <p className="mt-4 flex items-center gap-2 rounded-xl2 border border-red-200 bg-red-50 px-4 py-3 text-sm font-medium text-red-600">
          <AlertTriangle className="h-4 w-4 shrink-0" strokeWidth={2.25} />
          {startError}
        </p>
      )}

      {/* ---------- Confirm + CTA (sticky on mobile) ---------- */}
      <div className="fixed inset-x-0 bottom-0 z-10 border-t border-primary-100 bg-white/95 px-4 py-3 backdrop-blur sm:static sm:mt-4 sm:rounded-xl2 sm:border sm:bg-white sm:p-5 sm:shadow-card">
        <div className="mx-auto max-w-2xl">
          <button
            type="button"
            onClick={() => setConfirmed((c) => !c)}
            className="flex w-full items-center gap-2 text-left text-sm text-ink-700"
          >
            {confirmed ? (
              <CheckSquare className="h-5 w-5 shrink-0 text-primary-600" strokeWidth={2.25} />
            ) : (
              <Square className="h-5 w-5 shrink-0 text-ink-300" strokeWidth={2.25} />
            )}
            I have read and understood the instructions above.
          </button>

          <button
            type="button"
            onClick={handleStart}
            disabled={!confirmed || starting}
            className="mt-3 flex h-12 w-full items-center justify-center gap-2 rounded-xl2 bg-primary-600 text-sm font-bold text-white transition-colors hover:bg-primary-700 disabled:cursor-not-allowed disabled:opacity-40"
          >
            {starting ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.25} /> : <Play className="h-4 w-4" strokeWidth={2.25} />}
            {briefing.startLabel}
          </button>
          <p className="mt-1.5 text-center text-xs text-ink-400">
            {briefing.questionCount} Questions • {briefing.durationMinutes} Minutes • Exam Mode
          </p>
        </div>
      </div>
    </div>
  );
}

function buildGenericInstructions(briefing, hasNegativeMarking) {
  const items = [
    "The timer starts as soon as you click Start below.",
    "You can move between questions using Next and Previous.",
    "You can mark questions for review and come back to them later.",
    "Unanswered questions receive zero marks.",
  ];
  items.push(
    hasNegativeMarking
      ? `Negative marking applies: -${briefing.negativeMarkingRatio} mark for each incorrect answer.`
      : "No negative marking -- an incorrect answer won't cost you marks."
  );
  items.push("The test will be automatically submitted when the timer runs out.");
  return items;
}

function StatCard({ icon: Icon, value, label, muted }) {
  return (
    <div className={`flex flex-col items-center rounded-xl2 border py-3 text-center ${muted ? "border-primary-100 bg-primary-50" : "border-primary-100 bg-white"}`}>
      <Icon className={`h-4 w-4 ${muted ? "text-ink-300" : "text-secondary-500"}`} strokeWidth={2.25} />
      <span className="mt-1 text-base font-extrabold text-ink-900 sm:text-lg">{value}</span>
      <span className="text-[10px] font-semibold uppercase tracking-wide text-ink-400 sm:text-[11px]">{label}</span>
    </div>
  );
}

function EdgeCaseScreen({ title, message, backLabel, backTo }) {
  const navigate = useNavigate();
  return (
    <div className="flex min-h-[60vh] flex-col items-center justify-center px-6 text-center">
      <AlertTriangle className="h-8 w-8 text-secondary-400" strokeWidth={2} />
      <p className="mt-3 text-base font-bold text-ink-900">{title}</p>
      <p className="mt-1 max-w-sm text-sm text-ink-400">{message}</p>
      <button
        type="button"
        onClick={() => navigate(backTo)}
        className="mt-4 rounded-xl2 bg-primary-600 px-4 py-2.5 text-sm font-semibold text-white hover:bg-primary-700"
      >
        {backLabel}
      </button>
    </div>
  );
}
