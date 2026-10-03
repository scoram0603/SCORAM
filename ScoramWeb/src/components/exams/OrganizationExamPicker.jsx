import { useEffect, useMemo, useState } from "react";
import { ChevronDown, Search, Check, Loader2, AlertCircle, X } from "lucide-react";
import { listOrganizations } from "../../api/organizations";
import { listExams } from "../../api/exams";

// ORGANIZATION HIERARCHY -- "pick an Organization, then pick from its exams" instead of one flat
// list of every exam at once. Used by the My Exams first-time selection screen, the My Exams
// management screen, and the public (signed-out) exam filter.
//
// MY EXAMS SEARCH -- typing in the search box is a SERVER-SIDE, case-insensitive search
// (GET /api/exams?search=), debounced, matching the exam name or its Organization's name ("RRB"
// finds every RRB exam). The previous list stays on screen (dimmed, with a small spinner) while a
// new search is in flight, and a stale response can never overwrite a newer one (AbortController).
// Organizations (a small, fixed list) are loaded once.
//
// Exams with no Organization assigned yet (OrganizationId is nullable -- every exam that existed
// before Organizations started out unassigned) show under "More exams" at the bottom rather than
// silently disappearing -- an admin who hasn't mapped an exam yet must never make it unreachable.
//
// excludeIds (optional): exams to hide entirely -- used by "+ Add Exam" so an already-added exam
// isn't offered again. pendingId (optional): shows a spinner on that one row instead of its
// checkbox. onExamsLoaded (optional): fires with each loaded exam list so a wrapper can resolve
// id->name for its own summary text without a second fetch.
const SEARCH_DEBOUNCE_MS = 300;

export default function OrganizationExamPicker({ selectedIds, onToggle, excludeIds, pendingId, onExamsLoaded, autoFocusSearch = false }) {
  const [organizations, setOrganizations] = useState([]);
  const [exams, setExams] = useState([]);
  const [status, setStatus] = useState("loading"); // loading | ready | error
  const [searching, setSearching] = useState(false);
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState(""); // debounced
  const [reloadKey, setReloadKey] = useState(0);
  const [expandedIds, setExpandedIds] = useState(() => new Set());

  // Debounce the text box.
  useEffect(() => {
    const handle = setTimeout(() => setSearch(searchInput.trim()), SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(handle);
  }, [searchInput]);

  // Organizations: once (and again on Retry).
  useEffect(() => {
    const controller = new AbortController();
    listOrganizations({ signal: controller.signal })
      .then((orgs) => setOrganizations(orgs))
      .catch((err) => { if (err.name !== "AbortError") setStatus("error"); });
    return () => controller.abort();
  }, [reloadKey]);

  // Exams: on first load, on every (debounced) search change, and on Retry.
  useEffect(() => {
    const controller = new AbortController();
    setSearching(true);
    listExams({ search, signal: controller.signal })
      .then((list) => {
        setExams(list);
        setStatus("ready");
        onExamsLoaded?.(list);
      })
      .catch((err) => { if (err.name !== "AbortError") setStatus("error"); })
      .finally(() => { if (!controller.signal.aborted) setSearching(false); });
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search, reloadKey]);

  const selectedSet = useMemo(() => new Set(selectedIds), [selectedIds]);
  const isSearching = search.length > 0;

  const grouped = useMemo(() => {
    const byOrg = new Map();
    const unassigned = [];
    for (const exam of exams) {
      if (excludeIds && excludeIds.has(exam.id)) continue;
      if (!exam.organizationId) { unassigned.push(exam); continue; }
      if (!byOrg.has(exam.organizationId)) byOrg.set(exam.organizationId, []);
      byOrg.get(exam.organizationId).push(exam);
    }
    return { byOrg, unassigned };
  }, [exams, excludeIds]);

  function toggleExpanded(orgId) {
    setExpandedIds((prev) => {
      const next = new Set(prev);
      if (next.has(orgId)) next.delete(orgId); else next.add(orgId);
      return next;
    });
  }

  if (status === "loading") {
    return (
      <div className="space-y-2 py-2" aria-busy="true" aria-label="Loading exams">
        {[0, 1, 2, 3].map((i) => <div key={i} className="h-11 animate-pulse rounded-xl2 bg-primary-50" />)}
      </div>
    );
  }

  if (status === "error") {
    return (
      <div className="flex flex-wrap items-center gap-2 rounded-xl2 border border-accent-100 bg-accent-50 p-3 text-sm text-accent-600">
        <AlertCircle className="h-4 w-4 shrink-0" strokeWidth={2.25} />
        <span className="min-w-0 flex-1">Unable to load exams.</span>
        <button
          type="button"
          onClick={() => { setStatus("loading"); setReloadKey((k) => k + 1); }}
          className="rounded-lg bg-white px-3 py-1 text-xs font-bold text-accent-600 hover:bg-accent-100"
        >
          Retry
        </button>
      </div>
    );
  }

  const orgSections = organizations
    .map((org) => ({ org, exams: grouped.byOrg.get(org.id) || [] }))
    .filter(({ exams: orgExams }) => orgExams.length > 0);
  const hasResults = orgSections.length > 0 || grouped.unassigned.length > 0;

  return (
    <div>
      <div className="flex items-center gap-2 rounded-xl border border-primary-100 bg-white px-3 py-2 focus-within:border-secondary-500">
        <Search className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2.25} />
        <input
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          placeholder="Search exams (e.g. RRB, SSC CGL)"
          aria-label="Search exams"
          autoFocus={autoFocusSearch}
          className="w-full bg-transparent text-sm text-ink-900 outline-none placeholder:text-ink-400"
        />
        {searching && <Loader2 className="h-4 w-4 shrink-0 animate-spin text-primary-400" />}
        {searchInput && !searching && (
          <button type="button" onClick={() => setSearchInput("")} aria-label="Clear search" className="text-ink-400 hover:text-ink-600">
            <X className="h-4 w-4" strokeWidth={2.25} />
          </button>
        )}
      </div>

      <div className={`mt-2 space-y-1.5 transition-opacity ${searching ? "opacity-60" : ""}`}>
        {orgSections.map(({ org, exams: orgExams }) => {
          const expanded = isSearching || expandedIds.has(org.id);
          const selectedCount = orgExams.filter((e) => selectedSet.has(e.id)).length;
          return (
            <div key={org.id} className="overflow-hidden rounded-xl2 border border-primary-100">
              <button
                type="button"
                onClick={() => toggleExpanded(org.id)}
                className="flex w-full items-center gap-2 bg-white px-3 py-2.5 text-left hover:bg-primary-50"
              >
                <span className="min-w-0 flex-1 truncate text-sm font-bold text-ink-900">{org.name}</span>
                {selectedCount > 0 && (
                  <span className="rounded-full bg-primary-50 px-2 py-0.5 text-[11px] font-bold text-primary-600">{selectedCount}</span>
                )}
                <span className="text-xs text-ink-400">{isSearching ? orgExams.length : org.examCount}</span>
                <ChevronDown className={`h-4 w-4 shrink-0 text-ink-400 transition-transform ${expanded ? "rotate-180" : ""}`} strokeWidth={2.25} />
              </button>
              {expanded && (
                <div className="border-t border-primary-100 bg-surface/60 p-1.5">
                  {orgExams.map((exam) => (
                    <ExamRow key={exam.id} exam={exam} selected={selectedSet.has(exam.id)} pending={pendingId === exam.id} onTap={() => onToggle(exam.id, exam.name)} />
                  ))}
                </div>
              )}
            </div>
          );
        })}

        {grouped.unassigned.length > 0 && (
          <div className="overflow-hidden rounded-xl2 border border-primary-100">
            <button
              type="button"
              onClick={() => toggleExpanded("__unassigned")}
              className="flex w-full items-center gap-2 bg-white px-3 py-2.5 text-left hover:bg-primary-50"
            >
              <span className="min-w-0 flex-1 truncate text-sm font-bold text-ink-900">More exams</span>
              <ChevronDown
                className={`h-4 w-4 shrink-0 text-ink-400 transition-transform ${isSearching || expandedIds.has("__unassigned") ? "rotate-180" : ""}`}
                strokeWidth={2.25}
              />
            </button>
            {(isSearching || expandedIds.has("__unassigned")) && (
              <div className="border-t border-primary-100 bg-surface/60 p-1.5">
                {grouped.unassigned.map((exam) => (
                  <ExamRow key={exam.id} exam={exam} selected={selectedSet.has(exam.id)} pending={pendingId === exam.id} onTap={() => onToggle(exam.id, exam.name)} />
                ))}
              </div>
            )}
          </div>
        )}

        {!hasResults && (
          <p className="px-1 py-6 text-center text-sm text-ink-400">
            {isSearching ? "No exams found." : "No exams available right now."}
          </p>
        )}
      </div>
    </div>
  );
}

function ExamRow({ exam, selected, pending, onTap }) {
  return (
    <button
      type="button"
      onClick={onTap}
      disabled={pending}
      aria-pressed={selected}
      className={`flex w-full items-center gap-2.5 rounded-xl px-2.5 py-2 text-left transition-colors ${selected ? "bg-primary-50" : "hover:bg-white"}`}
    >
      {pending ? (
        <Loader2 className="h-5 w-5 shrink-0 animate-spin text-primary-400" />
      ) : (
        <span className={`flex h-5 w-5 shrink-0 items-center justify-center rounded-md border-2 ${selected ? "border-primary-600 bg-primary-600 text-white" : "border-ink-400"}`}>
          {selected && <Check className="h-3.5 w-3.5" strokeWidth={3} />}
        </span>
      )}
      <span className="min-w-0 flex-1 truncate text-sm font-semibold text-ink-900">{exam.name}</span>
    </button>
  );
}
