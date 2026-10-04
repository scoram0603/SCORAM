import { useCallback, useEffect, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { Loader2, Search, UserPlus, Users, Flag, Trophy, Lock, MessageCircle, GitCompare, X } from "lucide-react";
import * as api from "../api/studyPartners";
import { API_BASE_URL } from "../api/client";
import { timeAgo } from "../utils/format";

// STUDY PARTNER hub: My Study Partners / Requests / Challenges / Leaderboard / Privacy, plus Search.
// One page with tabs (not one long scroll). Every number and visibility rule comes from the server --
// this page only renders what it is given.

const TABS = [
  { id: "partners", label: "My Study Partners", icon: Users },
  { id: "requests", label: "Requests", icon: UserPlus },
  { id: "challenges", label: "Challenges", icon: Flag },
  { id: "leaderboard", label: "Leaderboard", icon: Trophy },
  { id: "privacy", label: "Privacy", icon: Lock },
];

const errText = (e) => (e && e.message) || "Something went wrong. Please try again.";
const photoSrc = (u) => (!u ? null : u.startsWith("http") ? u : `${API_BASE_URL}${u}`);

function Avatar({ person, size = 44 }) {
  const src = photoSrc(person.photoUrl);
  return (
    <div
      className="flex shrink-0 items-center justify-center overflow-hidden rounded-full bg-primary-50 font-bold text-primary-500"
      style={{ width: size, height: size }}
    >
      {src ? <img src={src} alt="" className="h-full w-full object-cover" /> : (person.fullName || "?")[0].toUpperCase()}
    </div>
  );
}

function Empty({ icon: Icon, title, text, action }) {
  return (
    <div className="flex flex-col items-center px-4 py-14 text-center">
      <div className="flex h-14 w-14 items-center justify-center rounded-full bg-primary-50 text-primary-500">
        <Icon className="h-6 w-6" strokeWidth={2} />
      </div>
      <p className="mt-4 font-bold text-ink-900">{title}</p>
      {text && <p className="mt-1 max-w-xs text-sm text-ink-400">{text}</p>}
      {action}
    </div>
  );
}

function Spinner() {
  return (
    <div className="flex justify-center py-14 text-ink-400" role="status" aria-label="Loading">
      <Loader2 className="h-6 w-6 animate-spin" />
    </div>
  );
}

function ErrorBox({ message, onRetry }) {
  return (
    <div className="rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-600" role="alert">
      {message}
      {onRetry && (
        <button type="button" onClick={onRetry} className="ml-2 font-semibold underline">
          Try again
        </button>
      )}
    </div>
  );
}

const btn = "rounded-lg px-3 py-2 text-sm font-semibold transition-colors disabled:opacity-50";
const btnPrimary = `${btn} bg-primary-500 text-white hover:bg-primary-600`;
const btnGhost = `${btn} border border-primary-100 text-ink-700 hover:bg-primary-50`;

/** Loads data with a refetch handle -- the small amount of state every tab needs. */
function useLoad(fn, deps) {
  const [state, setState] = useState({ status: "loading", data: null, error: "" });
  const load = useCallback(() => {
    setState((s) => ({ ...s, status: "loading" }));
    fn()
      .then((data) => setState({ status: "ok", data, error: "" }))
      .catch((e) => setState({ status: "error", data: null, error: errText(e) }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);
  useEffect(load, [load]);
  return { ...state, reload: load };
}

// ------------------------------------------------------------------ search

function SearchPanel({ onChanged }) {
  const [q, setQ] = useState("");
  const [results, setResults] = useState([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [note, setNote] = useState("");

  async function run(term) {
    if (term.trim().length < 2) { setResults([]); return; }
    setBusy(true);
    setError("");
    try { setResults(await api.searchStudyPartners(term.trim())); }
    catch (e) { setError(errText(e)); }
    finally { setBusy(false); }
  }

  useEffect(() => {
    const t = setTimeout(() => run(q), 350);
    return () => clearTimeout(t);
  }, [q]);

  async function add(r) {
    setNote("");
    try {
      await api.sendRequest(r.userId);
      setNote(`Request sent to ${r.fullName}.`);
      onChanged();
    } catch (e) { setNote(errText(e)); }
    run(q);
  }

  const action = (r) => {
    switch (r.connectionState) {
      case "StudyPartner": return <span className="text-sm font-semibold text-teal-500">Study Partner</span>;
      case "RequestSent": return <span className="text-sm text-ink-400">Request Sent</span>;
      case "RequestReceived": return <span className="text-sm text-ink-400">Sent you a request</span>;
      case "RequestRejected": return <span className="text-sm text-ink-400">Request declined</span>;
      case "Blocked": return <span className="text-sm text-ink-400">Blocked</span>;
      case "Restricted": return <span className="text-sm text-ink-400">Not accepting requests</span>;
      default: return <button type="button" className={btnPrimary} onClick={() => add(r)}>Add Study Partner</button>;
    }
  };

  return (
    <section aria-label="Search Study Partners" className="rounded-xl2 border border-primary-100 bg-white p-4">
      <label className="relative block">
        <span className="sr-only">Search by username or name</span>
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" />
        <input
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder="Search by username or name"
          className="w-full rounded-lg border border-primary-100 py-2.5 pl-9 pr-3 text-sm outline-none focus:border-primary-500"
        />
      </label>
      {busy && <p className="mt-3 text-sm text-ink-400">Searching…</p>}
      {error && <div className="mt-3"><ErrorBox message={error} /></div>}
      {note && <p className="mt-3 text-sm text-ink-600" role="status">{note}</p>}
      {q.trim().length >= 2 && !busy && !error && results.length === 0 && (
        <p className="mt-3 text-sm text-ink-400">No students found. Check the spelling, or try their username.</p>
      )}
      <ul className="mt-3 divide-y divide-primary-50">
        {results.map((r) => (
          <li key={r.userId} className="flex flex-wrap items-center gap-3 py-3">
            <Avatar person={r} />
            <div className="min-w-0 flex-1">
              <p className="truncate font-semibold text-ink-900">{r.fullName}</p>
              <p className="truncate text-sm text-ink-400">@{r.username}{r.primaryExam ? ` · ${r.primaryExam}` : ""}</p>
            </div>
            {action(r)}
          </li>
        ))}
      </ul>
    </section>
  );
}

// ------------------------------------------------------------------ partners (+ profile / compare)

function ProgressTiles({ p }) {
  const tiles = [
    ["Questions attempted", p.questionsAttempted],
    ["Accuracy", `${Number(p.accuracyPercent).toFixed(1)}%`],
    ["Tests completed", p.testsCompleted],
    ["Mock tests", p.mockTests],
    ["PYP attempts", p.pypAttempts],
    ["Current streak", `${p.currentStreak} days`],
  ];
  return (
    <dl className="grid grid-cols-2 gap-2 sm:grid-cols-3">
      {tiles.map(([k, v]) => (
        <div key={k} className="rounded-lg bg-primary-50/60 p-3">
          <dd className="text-lg font-extrabold text-ink-900">{v}</dd>
          <dt className="text-xs text-ink-400">{k}</dt>
        </div>
      ))}
    </dl>
  );
}

function CompareBars({ label, me, partner, max, suffix = "" }) {
  const top = max ?? Math.max(me, partner, 1);
  const bar = (v, cls) => (
    <div className="h-2 flex-1 overflow-hidden rounded-full bg-primary-50">
      <div className={`h-full rounded-full ${cls}`} style={{ width: `${Math.min(100, (v / top) * 100)}%` }} />
    </div>
  );
  return (
    <div role="group" aria-label={`${label}: you ${me}${suffix}, partner ${partner}${suffix}`}>
      <p className="text-xs text-ink-400">{label}</p>
      <div className="mt-1 flex items-center gap-2">{bar(me, "bg-primary-500")}<span className="w-16 text-right text-sm font-bold">{me}{suffix}</span></div>
      <div className="mt-1 flex items-center gap-2">{bar(partner, "bg-teal-500")}<span className="w-16 text-right text-sm font-bold">{partner}{suffix}</span></div>
    </div>
  );
}

function ComparePanel({ partner, onClose }) {
  const [examId, setExamId] = useState("");
  const { status, data, error, reload } = useLoad(() => api.comparePartner(partner.userId, examId), [partner.userId, examId]);

  return (
    <Modal title={`Compare with ${partner.fullName}`} onClose={onClose}>
      {status === "loading" && <Spinner />}
      {status === "error" && <ErrorBox message={error} onRetry={reload} />}
      {status === "ok" && (
        <div className="space-y-4">
          {data.commonExams.length > 0 && (
            <div className="flex flex-wrap gap-2">
              {[{ id: "", name: "Overall" }, ...data.commonExams].map((e) => (
                <button key={e.id || "all"} type="button" onClick={() => setExamId(e.id)}
                  className={`rounded-full px-3 py-1 text-sm font-semibold ${examId === e.id ? "bg-primary-500 text-white" : "bg-primary-50 text-ink-700"}`}>
                  {e.name}
                </button>
              ))}
            </div>
          )}
          <p className="flex gap-4 text-xs text-ink-600">
            <span><span className="mr-1 inline-block h-2 w-2 rounded-full bg-primary-500" />You</span>
            <span><span className="mr-1 inline-block h-2 w-2 rounded-full bg-teal-500" />{partner.fullName}</span>
          </p>
          {[
            ["Questions attempted", "questionsAttempted"], ["Correct answers", "questionsCorrect"],
            ["Tests completed", "testsCompleted"], ["Mock tests", "mockTests"], ["PYP attempts", "pypAttempts"],
            ["Current streak (days)", "currentStreak"], ["Questions this week", "questionsLast7Days"],
          ].map(([label, key]) => <CompareBars key={key} label={label} me={data.me[key]} partner={data.partnerProgress[key]} />)}
          <CompareBars label="Accuracy" me={Number(data.me.accuracyPercent)} partner={Number(data.partnerProgress.accuracyPercent)} max={100} suffix="%" />
          <p className="text-xs text-ink-400">Streak is for the whole account. Numbers match your Progress page.</p>
        </div>
      )}
    </Modal>
  );
}

function ChallengeForm({ partner, onClose, onCreated }) {
  const [type, setType] = useState("Practice");
  const [questions, setQuestions] = useState(50);
  const [targetDays, setTargetDays] = useState(5);
  const [duration, setDuration] = useState(7);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit() {
    setBusy(true); setError("");
    try {
      await api.createChallenge({
        partnerUserId: partner.userId, type, durationDays: duration,
        questionCount: type === "Streak" ? null : questions, targetDays: type === "Streak" ? targetDays : null,
      });
      onCreated(); onClose();
    } catch (e) { setError(errText(e)); setBusy(false); }
  }
  const chip = (on, label, set) => (
    <button type="button" key={label} onClick={set} aria-pressed={on}
      className={`rounded-full px-3 py-1 text-sm font-semibold ${on ? "bg-primary-500 text-white" : "bg-primary-50 text-ink-700"}`}>{label}</button>
  );
  return (
    <Modal title={`Challenge ${partner.fullName}`} onClose={onClose}>
      <div className="space-y-4">
        <div className="flex flex-wrap gap-2">{["Practice", "Accuracy", "Speed", "Streak"].map((t) => chip(type === t, t === "Streak" ? "Study streak" : t, () => setType(t)))}</div>
        {type === "Streak"
          ? <div><p className="mb-1 text-xs text-ink-400">Study days to reach</p><div className="flex gap-2">{[3, 5, 7].map((d) => chip(targetDays === d, `${d} days`, () => { setTargetDays(d); if (duration < d) setDuration(d <= 7 ? 7 : 14); }))}</div></div>
          : <div><p className="mb-1 text-xs text-ink-400">Number of questions</p><div className="flex gap-2">{[20, 50, 100].map((n) => chip(questions === n, `${n}`, () => setQuestions(n)))}</div></div>}
        <div><p className="mb-1 text-xs text-ink-400">Runs for</p><div className="flex gap-2">{[3, 7, 14].filter((d) => type !== "Streak" || d >= targetDays).map((d) => chip(duration === d, `${d} days`, () => setDuration(d)))}</div></div>
        <p className="text-xs text-ink-400">Scored from the practice, mock and PYP attempts you both make during the challenge — nothing separate to play.</p>
        {error && <ErrorBox message={error} />}
        <button type="button" className={`${btnPrimary} w-full`} disabled={busy} onClick={submit}>{busy ? "Sending…" : "Send challenge"}</button>
      </div>
    </Modal>
  );
}

function Modal({ title, onClose, children }) {
  useEffect(() => {
    const onKey = (e) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/40 p-0 sm:items-center sm:p-4" onClick={onClose}>
      <div role="dialog" aria-modal="true" aria-label={title} onClick={(e) => e.stopPropagation()}
        className="max-h-[90vh] w-full overflow-y-auto rounded-t-2xl bg-white p-5 sm:max-w-lg sm:rounded-2xl">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-extrabold text-ink-900">{title}</h2>
          <button type="button" onClick={onClose} aria-label="Close" className="rounded-lg p-1 text-ink-400 hover:bg-primary-50"><X className="h-5 w-5" /></button>
        </div>
        {children}
      </div>
    </div>
  );
}

function PartnerProfile({ partner, onClose, onChanged, onCompare }) {
  const { status, data, error, reload } = useLoad(() => api.getPartnerProfile(partner.userId), [partner.userId]);
  const [msg, setMsg] = useState("");
  const navigate = useNavigate();

  async function confirmAct(kind) {
    const text = kind === "remove" ? "Remove this Study Partner?" : "Block this student? They won't be able to find, message or send you requests.";
    if (!window.confirm(text)) return;
    try {
      await (kind === "remove" ? api.removePartner(partner.userId) : api.blockStudent(partner.userId));
      onChanged(); onClose();
    } catch (e) { setMsg(errText(e)); }
  }

  return (
    <Modal title="Study Partner Profile" onClose={onClose}>
      {status === "loading" && <Spinner />}
      {status === "error" && <ErrorBox message={error} onRetry={reload} />}
      {status === "ok" && (
        <div className="space-y-4">
          <div className="flex items-center gap-3">
            <Avatar person={data} size={56} />
            <div><p className="font-extrabold text-ink-900">{data.fullName}</p><p className="text-sm text-ink-400">@{data.username}</p></div>
          </div>
          {data.exams?.length > 0 && <div className="flex flex-wrap gap-2">{data.exams.map((e) => <span key={e.id} className="rounded-full bg-primary-50 px-3 py-1 text-xs font-semibold text-primary-500">{e.name}</span>)}</div>}
          {data.progress ? <ProgressTiles p={data.progress} /> : <p className="flex items-center gap-2 rounded-lg bg-primary-50/60 p-3 text-sm text-ink-600"><Lock className="h-4 w-4" />This student keeps their study progress private.</p>}
          {msg && <ErrorBox message={msg} />}
          {data.isPartner && (
            <div className="flex flex-wrap gap-2 border-t border-primary-50 pt-4">
              <button type="button" className={btnPrimary} onClick={() => navigate("/chat?tab=messages")}><MessageCircle className="mr-1 inline h-4 w-4" />Message</button>
              <button type="button" className={btnGhost} onClick={() => { onClose(); onCompare(); }}><GitCompare className="mr-1 inline h-4 w-4" />Compare</button>
              <button type="button" className={btnGhost} onClick={() => confirmAct("remove")}>Remove</button>
              <button type="button" className={btnGhost} onClick={() => confirmAct("block")}>Block</button>
            </div>
          )}
        </div>
      )}
    </Modal>
  );
}

function PartnersTab({ onSearchFocus }) {
  const { status, data, error, reload } = useLoad(() => api.getPartners(), []);
  const [profile, setProfile] = useState(null);
  const [compare, setCompare] = useState(null);
  const [challenge, setChallenge] = useState(null);

  if (status === "loading") return <Spinner />;
  if (status === "error") return <ErrorBox message={error} onRetry={reload} />;
  if (data.length === 0)
    return <Empty icon={Users} title="Build your study circle" text="Connect with fellow aspirants and prepare together."
      action={<button type="button" className={`${btnPrimary} mt-4`} onClick={onSearchFocus}>Find Study Partners</button>} />;

  return (
    <>
      <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        {data.map((p) => {
          return (
            <li key={p.userId} className="flex flex-col rounded-xl2 border border-primary-100 bg-white p-4">
              <button type="button" className="flex items-center gap-3 text-left" onClick={() => setProfile(p)}>
                <Avatar person={p} />
                <span className="min-w-0">
                  <span className="block truncate font-semibold text-ink-900">{p.fullName}</span>
                  <span className="block truncate text-sm text-ink-400">@{p.username}{p.primaryExam ? ` · ${p.primaryExam}` : ""}</span>
                </span>
              </button>
              {(p.currentStreak != null || p.questionsLast7Days != null) && (
                <p className="mt-2 text-xs text-ink-400">
                  {p.currentStreak != null && `${p.currentStreak}-day streak`}{p.currentStreak != null && p.questionsLast7Days != null && " · "}
                  {p.questionsLast7Days != null && `${p.questionsLast7Days} questions this week`}
                </p>
              )}
              <div className="mt-3 flex flex-wrap gap-2">
                <button type="button" className={btnGhost} onClick={() => setCompare(p)}>Compare</button>
                <button type="button" className={btnGhost} onClick={() => setChallenge(p)}>Challenge</button>
              </div>
            </li>
          );
        })}
      </ul>
      {profile && <PartnerProfile partner={profile} onClose={() => setProfile(null)} onChanged={reload} onCompare={() => setCompare(profile)} />}
      {compare && <ComparePanel partner={compare} onClose={() => setCompare(null)} />}
      {challenge && <ChallengeForm partner={challenge} onClose={() => setChallenge(null)} onCreated={() => {}} />}
    </>
  );
}

// ------------------------------------------------------------------ requests

function RequestsTab({ onChanged }) {
  const { status, data, error, reload } = useLoad(() => api.getRequests(), []);
  const [msg, setMsg] = useState("");

  async function act(fn, ok) {
    setMsg("");
    try { await fn(); setMsg(ok); onChanged(); }
    catch (e) { setMsg(errText(e)); }
    reload();
  }

  if (status === "loading") return <Spinner />;
  if (status === "error") return <ErrorBox message={error} onRetry={reload} />;
  if (!data.incoming.length && !data.outgoing.length) return <Empty icon={UserPlus} title="No pending Study Partner requests." />;

  const row = (r, incoming) => (
    <li key={r.id} className="flex flex-wrap items-center gap-3 rounded-xl2 border border-primary-100 bg-white p-3">
      <Avatar person={r.person} />
      <div className="min-w-0 flex-1">
        <p className="truncate font-semibold text-ink-900">{r.person.fullName}</p>
        <p className="truncate text-sm text-ink-400">@{r.person.username}{r.person.primaryExam ? ` · ${r.person.primaryExam}` : ""} · {timeAgo(r.createdAt)}</p>
      </div>
      {incoming ? (
        <div className="flex gap-2">
          <button type="button" className={btnPrimary} onClick={() => act(() => api.acceptRequest(r.id), "You're now Study Partners.")}>Accept</button>
          <button type="button" className={btnGhost} onClick={() => act(() => api.rejectRequest(r.id), "Request declined.")}>Reject</button>
          <button type="button" className={btnGhost} onClick={() => window.confirm("Block this student?") && act(() => api.blockStudent(r.person.userId), "Student blocked.")}>Block</button>
        </div>
      ) : (
        <div className="flex items-center gap-3"><span className="text-sm text-ink-400">Request Sent</span>
          <button type="button" className={btnGhost} onClick={() => act(() => api.cancelRequest(r.id), "Request cancelled.")}>Cancel Request</button></div>
      )}
    </li>
  );

  return (
    <div className="space-y-5">
      {msg && <p className="text-sm text-ink-600" role="status">{msg}</p>}
      {data.incoming.length > 0 && <div><h3 className="mb-2 font-bold text-ink-900">Received</h3><ul className="space-y-2">{data.incoming.map((r) => row(r, true))}</ul></div>}
      {data.outgoing.length > 0 && <div><h3 className="mb-2 font-bold text-ink-900">Sent</h3><ul className="space-y-2">{data.outgoing.map((r) => row(r, false))}</ul></div>}
    </div>
  );
}

// ------------------------------------------------------------------ challenges

const TYPE_TITLE = { Practice: "Practice challenge", Accuracy: "Accuracy challenge", Speed: "Speed challenge", Streak: "Study streak challenge" };
const STATUS_LABEL = { Pending: "Pending", Accepted: "In progress", InProgress: "In progress", Completed: "Finished", Rejected: "Declined", Expired: "Expired", Cancelled: "Cancelled" };
const targetText = (c) => ({
  Practice: `Answer ${c.questionCount} questions`, Accuracy: `Best accuracy over ${c.questionCount}+ questions`,
  Speed: `Fastest attempt of ${c.questionCount}+ questions`, Streak: `Study on ${c.targetDays} days`,
}[c.type]);
function fmtResult(c, v) {
  if (v == null) return "—";
  if (c.type === "Accuracy") return `${Number(v).toFixed(1)}%`;
  if (c.type === "Speed") return v === 0 ? "—" : `${Math.floor(v / 60)}m ${Math.round(v % 60)}s`;
  return c.type === "Streak" ? `${Math.round(v)} days` : `${Math.round(v)} questions`;
}

function ChallengesTab() {
  const [history, setHistory] = useState(false);
  const { status, data, error, reload } = useLoad(() => api.getChallenges(history), [history]);
  const [msg, setMsg] = useState("");

  async function act(fn, ok) {
    setMsg("");
    try { await fn(); if (ok) setMsg(ok); } catch (e) { setMsg(errText(e)); }
    reload();
  }

  return (
    <div>
      <div className="mb-3 inline-flex rounded-lg border border-primary-100 p-0.5" role="tablist">
        {[[false, "Active"], [true, "Finished"]].map(([h, label]) => (
          <button key={label} type="button" role="tab" aria-selected={history === h} onClick={() => setHistory(h)}
            className={`rounded-md px-4 py-1.5 text-sm font-semibold ${history === h ? "bg-primary-500 text-white" : "text-ink-600"}`}>{label}</button>
        ))}
      </div>
      {msg && <p className="mb-3 text-sm text-ink-600" role="status">{msg}</p>}
      {status === "loading" && <Spinner />}
      {status === "error" && <ErrorBox message={error} onRetry={reload} />}
      {status === "ok" && data.length === 0 && <Empty icon={Flag} title={history ? "No finished challenges yet." : "No active challenges."} text={history ? undefined : "Challenge a Study Partner from My Study Partners."} />}
      {status === "ok" && (
        <ul className="grid gap-3 lg:grid-cols-2">
          {data.map((c) => {
            const other = c.iAmCreator ? c.partner : c.creator;
            const mine = c.iAmCreator ? c.creatorResult : c.partnerResult;
            const theirs = c.iAmCreator ? c.partnerResult : c.creatorResult;
            const ended = c.endDate && new Date(c.endDate) < new Date();
            return (
              <li key={c.id} className="rounded-xl2 border border-primary-100 bg-white p-4">
                <div className="flex items-center gap-3">
                  <Avatar person={other} size={36} />
                  <div className="min-w-0 flex-1"><p className="font-bold text-ink-900">{TYPE_TITLE[c.type]}</p>
                    <p className="truncate text-sm text-ink-400">{c.iAmCreator ? `You → ${other.fullName}` : `${other.fullName} → You`}</p></div>
                  <span className="rounded-full bg-primary-50 px-2.5 py-1 text-xs font-semibold text-primary-500">{STATUS_LABEL[c.status]}</span>
                </div>
                <p className="mt-2 text-sm text-ink-700">{[targetText(c), c.exam?.name, c.status === "Pending" ? `${c.durationDays} days once accepted` : c.endDate && c.status === "InProgress" ? `Ends ${new Date(c.endDate).toLocaleDateString()}` : null].filter(Boolean).join(" · ")}</p>
                {c.status === "Completed" && (
                  <div className="mt-3 grid grid-cols-2 gap-2 text-sm">
                    <div className={`rounded-lg p-3 ${c.winnerUserId && c.winnerUserId === (c.iAmCreator ? c.creator.userId : c.partner.userId) ? "bg-teal-50" : "bg-primary-50/60"}`}><p className="text-xs text-ink-400">You</p><p className="font-bold">{fmtResult(c, mine)}</p></div>
                    <div className="rounded-lg bg-primary-50/60 p-3"><p className="truncate text-xs text-ink-400">{other.fullName.split(" ")[0]}</p><p className="font-bold">{fmtResult(c, theirs)}</p></div>
                    {c.resultSummary && <p className="col-span-2 text-xs text-ink-400">{c.resultSummary}</p>}
                  </div>
                )}
                <div className="mt-3 flex flex-wrap gap-2">
                  {c.status === "Pending" && !c.iAmCreator && (<>
                    <button type="button" className={btnPrimary} onClick={() => act(() => api.acceptChallenge(c.id), "Challenge accepted.")}>Accept</button>
                    <button type="button" className={btnGhost} onClick={() => act(() => api.rejectChallenge(c.id))}>Decline</button></>)}
                  {c.status === "Pending" && c.iAmCreator && <button type="button" className={btnGhost} onClick={() => act(() => api.cancelChallenge(c.id), "Challenge withdrawn.")}>Withdraw</button>}
                  {c.status === "InProgress" && ended && <button type="button" className={btnPrimary} onClick={() => act(() => api.completeChallenge(c.id))}>See result</button>}
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

// ------------------------------------------------------------------ leaderboard

const PERIODS = [["today", "Today"], ["week", "This week"], ["month", "This month"], ["all", "All time"]];
const METRICS = [["questions", "Questions"], ["accuracy", "Accuracy"], ["tests", "Tests"], ["streak", "Streak"], ["xp", "XP"]];

function LeaderboardTab() {
  const [period, setPeriod] = useState("week");
  const [metric, setMetric] = useState("questions");
  const { status, data, error, reload } = useLoad(() => api.getLeaderboard(period, metric), [period, metric]);
  const val = (e) => ({ accuracy: `${Number(e.accuracyPercent).toFixed(1)}%`, tests: e.testsCompleted, streak: `${e.currentStreak} days`, xp: `${e.totalXp} XP` }[metric] ?? e.questionsAttempted);

  const chips = (list, cur, set) => (
    <div className="flex flex-wrap gap-2">{list.map(([k, l]) => (
      <button key={k} type="button" aria-pressed={cur === k} onClick={() => set(k)}
        className={`rounded-full px-3 py-1 text-sm font-semibold ${cur === k ? "bg-primary-500 text-white" : "bg-primary-50 text-ink-700"}`}>{l}</button>))}</div>
  );

  return (
    <div className="space-y-3">
      {chips(PERIODS, period, setPeriod)}
      {chips(METRICS, metric, setMetric)}
      {status === "loading" && <Spinner />}
      {status === "error" && <ErrorBox message={error} onRetry={reload} />}
      {status === "ok" && data.entries.length === 0 && <Empty icon={Trophy} title="Complete some practice to appear in your Study Partner leaderboard." />}
      {status === "ok" && data.entries.length > 0 && (
        <div className="overflow-x-auto rounded-xl2 border border-primary-100 bg-white">
          <table className="w-full min-w-[480px] text-left text-sm">
            <thead className="bg-primary-50/60 text-xs text-ink-400">
              <tr><th className="px-4 py-2">Rank</th><th className="px-4 py-2">Partner</th><th className="px-4 py-2">Questions</th><th className="px-4 py-2">Accuracy</th><th className="px-4 py-2">Streak</th></tr>
            </thead>
            <tbody>
              {data.entries.map((e) => (
                <tr key={e.userId} className={`border-t border-primary-50 ${e.isMe ? "bg-primary-50/40" : ""}`}>
                  <td className="px-4 py-2 font-bold">{e.rank}</td>
                  <td className="px-4 py-2"><span className="flex items-center gap-2"><Avatar person={e} size={28} />{e.fullName}{e.isMe && " (You)"}</span></td>
                  <td className="px-4 py-2">{e.questionsAttempted}</td><td className="px-4 py-2">{Number(e.accuracyPercent).toFixed(1)}%</td><td className="px-4 py-2">{e.currentStreak} days</td>
                </tr>
              ))}
            </tbody>
          </table>
          <p className="border-t border-primary-50 px-4 py-2 text-xs text-ink-400">Ranked by {METRICS.find(([k]) => k === metric)[1].toLowerCase()} ({period === "all" ? "all time" : PERIODS.find(([k]) => k === period)[1].toLowerCase()}). Showing {val(data.entries[0])} at the top.</p>
        </div>
      )}
    </div>
  );
}

// ------------------------------------------------------------------ privacy

const LEVELS = [["Everyone", "Everyone"], ["StudyPartnersOnly", "Study Partners only"], ["OnlyMe", "Only me"]];

function PrivacyTab() {
  const { status, data, error, reload } = useLoad(() => api.getPrivacy(), []);
  const [draft, setDraft] = useState(null);
  const [msg, setMsg] = useState("");
  const p = draft || data;

  async function save(next) {
    const prev = p; setDraft(next); setMsg("");
    try { setDraft(await api.savePrivacy(next)); setMsg("Saved."); }
    catch (e) { setDraft(prev); setMsg(errText(e)); }
  }

  if (status === "loading") return <Spinner />;
  if (status === "error") return <ErrorBox message={error} onRetry={reload} />;

  const select = (label, hint, key) => (
    <div className="flex flex-wrap items-center justify-between gap-2 py-3">
      <div><p className="font-semibold text-ink-900">{label}</p><p className="text-sm text-ink-400">{hint}</p></div>
      <select aria-label={label} value={p[key]} onChange={(e) => save({ ...p, [key]: e.target.value })} className="rounded-lg border border-primary-100 px-3 py-2 text-sm">
        {LEVELS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
      </select>
    </div>
  );
  const toggle = (label, hint, key) => (
    <label className="flex cursor-pointer items-center justify-between gap-3 py-3">
      <span><span className="block font-semibold text-ink-900">{label}</span><span className="block text-sm text-ink-400">{hint}</span></span>
      <input type="checkbox" checked={p[key]} onChange={(e) => save({ ...p, [key]: e.target.checked })} className="h-5 w-5 accent-primary-500" />
    </label>
  );

  return (
    <div className="max-w-xl divide-y divide-primary-50 rounded-xl2 border border-primary-100 bg-white px-4">
      {select("Profile", "Your selected exams", "profileVisibility")}
      {select("Progress", "Questions, accuracy, tests, streak", "progressVisibility")}
      {select("Activity", "Recently completed tests", "activityVisibility")}
      {toggle("Show me on leaderboards", "Only your Study Partners see it, and only if Progress is shared with them.", "showOnLeaderboard")}
      {toggle("Allow Study Partner requests", "Turn off to stop new requests. Existing Study Partners aren't affected.", "allowStudyPartnerRequests")}
      {msg && <p className="py-3 text-sm text-ink-600" role="status">{msg}</p>}
    </div>
  );
}

// ------------------------------------------------------------------ page

export default function StudyPartners() {
  const [params, setParams] = useSearchParams();
  const tab = TABS.some((t) => t.id === params.get("tab")) ? params.get("tab") : "partners";
  const [refreshKey, setRefreshKey] = useState(0);
  const bump = () => setRefreshKey((k) => k + 1);

  return (
    <div className="px-4 pb-10 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <h1 className="text-xl font-extrabold text-ink-900 sm:text-2xl">Study Partner</h1>
      <p className="mt-1 text-sm text-ink-400">Prepare together. Track progress. Challenge each other. Improve together.</p>

      <div className="mt-4"><SearchPanel onChanged={bump} /></div>

      <div className="mt-5 overflow-x-auto" role="tablist" aria-label="Study Partner sections">
        <div className="flex min-w-max gap-1 border-b border-primary-100">
          {TABS.map(({ id, label, icon: Icon }) => (
            <button key={id} type="button" role="tab" aria-selected={tab === id} onClick={() => setParams({ tab: id })}
              className={`flex items-center gap-1.5 border-b-2 px-4 py-2.5 text-sm font-semibold ${tab === id ? "border-primary-500 text-primary-500" : "border-transparent text-ink-400 hover:text-ink-700"}`}>
              <Icon className="h-4 w-4" />{label}
            </button>
          ))}
        </div>
      </div>

      <div className="mt-5" key={`${tab}-${refreshKey}`}>
        {tab === "partners" && <PartnersTab onSearchFocus={() => window.scrollTo({ top: 0, behavior: "smooth" })} />}
        {tab === "requests" && <RequestsTab onChanged={bump} />}
        {tab === "challenges" && <ChallengesTab />}
        {tab === "leaderboard" && <LeaderboardTab />}
        {tab === "privacy" && <PrivacyTab />}
      </div>
    </div>
  );
}
