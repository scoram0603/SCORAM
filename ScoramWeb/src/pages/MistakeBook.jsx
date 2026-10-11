import { useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { ArrowLeft, ChevronDown, ChevronRight, Eye, EyeOff, Play } from "lucide-react";
import { getMistakeDetail, getMistakes, setMistakeDismissed } from "../api/smartProgress";
import { useApiQuery } from "../hooks/useApiQuery";
import QuestionView from "../components/progress/QuestionView";
import { MathText } from "../components/questions/MathText";
import {
  EmptyState, ErrorState, InlineNote, PrimaryLinkClass, SecondaryLinkClass, SelectField, SkeletonBlock,
} from "../components/progress/ui";
import { fmtDate, kindLabel, KIND_OPTIONS, plural } from "../components/progress/format";

const STATUS_OPTIONS = [
  ["Open", "Open (to correct)"], ["New", "New"], ["Learning", "Learning"], ["Corrected", "Corrected"], ["Dismissed", "Dismissed"], ["All", "All"],
];
const TYPE_OPTIONS = [
  ["Incorrect", "Incorrect"], ["Skipped", "Skipped"], ["Repeated", "Repeatedly wrong"], ["Marked", "Marked for review"], ["All", "All"],
];
const SORT_OPTIONS = [["recent", "Most recent"], ["oldest", "Oldest first"], ["mostWrong", "Most wrong"]];
const PAGE_SIZE = 20;

const STATUS_TEXT = { New: "New", Learning: "Learning", Corrected: "Corrected", Dismissed: "Dismissed" };

function MistakeRow({ item, onChanged }) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState(null);
  const detail = useApiQuery((signal) => getMistakeDetail(item.questionRef, { signal }), [item.questionRef], { enabled: open });

  async function toggleDismiss() {
    setBusy(true);
    setActionError(null);
    try {
      await setMistakeDismissed(item.questionRef, item.status !== "Dismissed");
      onChanged();
    } catch (e) {
      setActionError(e);
    } finally {
      setBusy(false);
    }
  }

  const d = detail.data;
  return (
    <li className="rounded-xl2 border border-primary-100 bg-white shadow-card">
      <button type="button" aria-expanded={open} onClick={() => setOpen(!open)} className="flex w-full items-start gap-3 p-4 text-left">
        <span className="mt-0.5 text-ink-400" aria-hidden="true">
          {open ? <ChevronDown className="h-4 w-4" strokeWidth={2.5} /> : <ChevronRight className="h-4 w-4" strokeWidth={2.5} />}
        </span>
        <span className="min-w-0 flex-1">
          <span className="mb-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-[11px] text-ink-400">
            <span className="rounded-full border border-primary-100 bg-primary-50/60 px-2 py-0.5 font-semibold text-ink-600">{STATUS_TEXT[item.status] || item.status}</span>
            <span>{item.isUnclassified ? "Unclassified" : `${item.subject} · ${item.topic}`}</span>
            <span>wrong {item.timesWrong}×{item.timesSkipped > 0 ? ` · skipped ${item.timesSkipped}×` : ""}</span>
            {item.isRepeated && <span className="font-semibold text-red-700">Repeatedly wrong</span>}
            {item.isMarkedForReview && <span>Marked for review</span>}
          </span>
          <span className="line-clamp-2 block text-sm text-ink-900"><MathText text={item.questionPreview} /></span>
          <span className="mt-1 block text-[11px] text-ink-400">
            Last missed {fmtDate(item.lastMistakeAt)}
            {item.revision.attempts > 0 && ` · revised ${item.revision.correct}/${item.revision.attempts} correct`}
            {item.status === "Learning" && item.revision.isDue && " · due now"}
          </span>
        </span>
      </button>

      {open && (
        <div className="border-t border-primary-100 p-4">
          {detail.initialLoading ? <SkeletonBlock rows={4} label="Loading question" />
            : detail.error && !d ? <ErrorState error={detail.error} onRetry={detail.reload} />
            : d ? (
              <>
                <QuestionView q={d} picked={item.lastSelectedOption} correctOption={d.correctOption} />
                {(d.explanation || d.explanationImageUrl) && (
                  <div className="mt-3 rounded-xl2 bg-primary-50/60 p-3">
                    <p className="text-xs font-bold text-ink-900">Explanation</p>
                    <MathText text={d.explanation} className="mt-1 text-sm text-ink-600" />
                  </div>
                )}
                <h3 className="mt-4 text-xs font-bold text-ink-900">History</h3>
                <ul className="mt-1 space-y-1 text-xs text-ink-600">
                  {d.history.map((h) => (
                    <li key={`${h.attemptId}`}>
                      <Link to={`/tests/result/${h.attemptId}`} className="text-secondary-500 hover:underline">{h.title}</Link>
                      {" · "}{kindLabel(h.testKind)} · {fmtDate(h.attemptedAt)} · {h.wasSkipped ? "skipped" : h.isCorrect ? "correct" : `wrong (chose ${h.selectedOption})`}
                    </li>
                  ))}
                  {d.revisionHistory.map((h, i) => (
                    <li key={`r${i}`} className="text-ink-400">Revision · {fmtDate(h.answeredAt, true)} · {h.isCorrect ? "correct" : `wrong (chose ${h.selectedOption})`}</li>
                  ))}
                </ul>
              </>
            ) : null}
          <div className="mt-4 flex flex-wrap gap-2">
            <button type="button" disabled={busy} onClick={toggleDismiss} className={SecondaryLinkClass()}>
              {item.status === "Dismissed" ? <Eye className="h-3.5 w-3.5" aria-hidden="true" /> : <EyeOff className="h-3.5 w-3.5" aria-hidden="true" />}
              {item.status === "Dismissed" ? "Restore to Mistake Book" : "Hide from Mistake Book"}
            </button>
          </div>
          {actionError && <p role="alert" className="mt-2 text-xs text-red-700">{actionError.message || "Couldn't update that."}</p>}
        </div>
      )}
    </li>
  );
}

export default function MistakeBook() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const [searchText, setSearchText] = useState(params.get("search") || "");
  const f = {
    subject: params.get("subject") || "",
    topic: params.get("topic") || "",
    kind: params.get("kind") || "",
    status: params.get("status") || "Open",
    type: params.get("type") || "Incorrect",
    sort: params.get("sort") || "recent",
    search: params.get("search") || "",
    dueOnly: params.get("due") === "1",
    page: Number(params.get("page") || 1) || 1,
  };

  function setFilter(key, value) {
    const next = new URLSearchParams(params);
    if (value === "" || value === false || value == null) next.delete(key);
    else next.set(key, value === true ? "1" : value);
    if (key !== "page") next.delete("page");
    setParams(next, { replace: true });
  }

  // debounce the free-text search into the URL
  useEffect(() => {
    if (searchText === f.search) return undefined;
    const t = setTimeout(() => setFilter("search", searchText.trim()), 400);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchText]);

  const key = JSON.stringify(f);
  const q = useApiQuery(
    (signal) => getMistakes({
      subject: f.subject || undefined, topic: f.topic || undefined, kind: f.kind || undefined, status: f.status, type: f.type,
      sort: f.sort, search: f.search || undefined, dueOnly: f.dueOnly, page: f.page, pageSize: PAGE_SIZE,
    }, { signal }),
    [key],
  );
  const d = q.data;
  const totalPages = d ? Math.max(1, Math.ceil(d.totalCount / d.pageSize)) : 1;
  const filtered = Boolean(f.subject || f.topic || f.kind || f.search || f.dueOnly || f.status !== "Open" || f.type !== "Incorrect");
  const reviseQuery = new URLSearchParams();
  if (f.subject) reviseQuery.set("subject", f.subject);
  if (f.topic) reviseQuery.set("topic", f.topic);

  return (
    <div className="px-4 pb-10 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <button type="button" onClick={() => navigate("/progress")} className="flex items-center gap-1.5 text-sm font-semibold text-secondary-500">
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} aria-hidden="true" /> My Progress
      </button>
      <div className="mt-3 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-extrabold text-ink-900 sm:text-2xl">Mistake Book</h1>
          <p className="text-sm text-ink-400">Questions you got wrong or skipped, with your revision progress. Revising never changes your test scores.</p>
        </div>
        {d && d.summary.open > 0 && (
          <Link to={`/mistake-book/revise${reviseQuery.toString() ? `?${reviseQuery}` : ""}`} className={PrimaryLinkClass()}>
            <Play className="h-3.5 w-3.5" aria-hidden="true" /> Revise now
          </Link>
        )}
      </div>

      {d && (
        <p className="mt-3 text-xs text-ink-600">
          <strong>{d.summary.open}</strong> open ({d.summary.new} new, {d.summary.learning} learning) · <strong>{d.summary.dueNow}</strong> due now · <strong>{d.summary.corrected}</strong> corrected · <strong>{d.summary.repeated}</strong> wrong more than once
        </p>
      )}

      <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
        <SelectField label="Status" value={f.status} onChange={(v) => setFilter("status", v === "Open" ? "" : v)}>
          {STATUS_OPTIONS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
        </SelectField>
        <SelectField label="Type" value={f.type} onChange={(v) => setFilter("type", v === "Incorrect" ? "" : v)}>
          {TYPE_OPTIONS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
        </SelectField>
        <SelectField label="Assessment" value={f.kind} onChange={(v) => setFilter("kind", v)}>
          {KIND_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
        </SelectField>
        <SelectField label="Sort" value={f.sort} onChange={(v) => setFilter("sort", v === "recent" ? "" : v)}>
          {SORT_OPTIONS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
        </SelectField>
        <label className="col-span-2 block sm:col-span-1 lg:col-span-2">
          <span className="mb-1 block text-[11px] font-semibold text-ink-600">Search question text</span>
          <input
            type="search" value={searchText} maxLength={100} onChange={(e) => setSearchText(e.target.value)}
            className="h-10 w-full rounded-xl2 border border-primary-100 bg-white px-3 text-sm text-ink-900 focus:border-secondary-500"
          />
        </label>
      </div>
      <div className="mt-2 flex flex-wrap items-center gap-3 text-xs text-ink-600">
        <label className="inline-flex items-center gap-1.5">
          <input type="checkbox" checked={f.dueOnly} onChange={(e) => setFilter("due", e.target.checked)} /> Due for revision only
        </label>
        {(f.subject || f.topic) && (
          <span className="inline-flex items-center gap-1.5 rounded-full border border-primary-100 bg-primary-50/60 px-2 py-0.5">
            {f.subject}{f.topic ? ` · ${f.topic}` : ""}
            <button type="button" aria-label="Clear subject and topic filter" onClick={() => { const n = new URLSearchParams(params); n.delete("subject"); n.delete("topic"); n.delete("page"); setParams(n, { replace: true }); }} className="font-bold">×</button>
          </span>
        )}
        {filtered && <button type="button" onClick={() => { setSearchText(""); setParams(new URLSearchParams(), { replace: true }); }} className="font-semibold text-secondary-500 hover:underline">Reset filters</button>}
      </div>

      <div className="mt-4" aria-live="polite">
        {q.initialLoading ? <SkeletonBlock rows={6} label="Loading your Mistake Book" />
          : q.error && !d ? <ErrorState error={q.error} onRetry={q.reload} />
          : d && d.items.length === 0 ? (
            filtered
              ? <EmptyState title="No questions match these filters" action={<button type="button" className={PrimaryLinkClass()} onClick={() => { setSearchText(""); setParams(new URLSearchParams(), { replace: true }); }}>Reset filters</button>}>Try a different status or type.</EmptyState>
              : <EmptyState title="Your Mistake Book is empty" action={<Link to="/tests" className={PrimaryLinkClass()}>Take a test</Link>}>Questions you get wrong or skip in a submitted test, mock or previous year paper show up here for revision.</EmptyState>
          ) : d ? (
            <>
              {q.error && <div className="mb-3"><InlineNote tone="warn">Couldn't refresh just now — showing the last loaded list. <button type="button" className="font-semibold underline" onClick={q.reload}>Retry</button></InlineNote></div>}
              <ul className="space-y-2">{d.items.map((it) => <MistakeRow key={it.questionRef} item={it} onChanged={q.reload} />)}</ul>
              {totalPages > 1 && (
                <nav className="mt-4 flex items-center justify-between text-xs text-ink-600" aria-label="Pagination">
                  <button type="button" disabled={f.page <= 1} onClick={() => setFilter("page", f.page - 1 > 1 ? String(f.page - 1) : "")} className={SecondaryLinkClass("disabled:opacity-50")}>Previous</button>
                  <span>Page {f.page} of {totalPages} · {plural(d.totalCount, "question")}</span>
                  <button type="button" disabled={f.page >= totalPages} onClick={() => setFilter("page", String(f.page + 1))} className={SecondaryLinkClass("disabled:opacity-50")}>Next</button>
                </nav>
              )}
            </>
          ) : null}
      </div>
    </div>
  );
}
