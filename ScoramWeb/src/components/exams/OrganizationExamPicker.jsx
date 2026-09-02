import { useEffect, useMemo, useState } from "react";
import { ChevronDown, Search, Check, Loader2, AlertCircle } from "lucide-react";
import { listOrganizations } from "../../api/organizations";
import { listExams } from "../../api/exams";

// ORGANIZATION HIERARCHY -- "pick an Organization, then pick from its exams" instead of one flat
// list of every exam at once. Used by the My Exams onboarding/management screens, and the exam
// filter on PYP/Question Bank/Mock Tests. Fetches the full flat exam list once (not per-organization
// on each expand) and groups it client-side -- this catalog is small enough (a few dozen to a few
// hundred exams total) that one upfront fetch plus instant client-side search beats the complexity
// of a separate network round-trip every time an organization is expanded, and it's what makes
// typing directly into the search box below work across every organization at once.
//
// Exams with no Organization assigned yet (OrganizationId is nullable -- see Exam.OrganizationId's
// own comment on why: every exam that existed before this feature started out unassigned, with no
// forced migration) show under "Other Exams" at the bottom rather than silently disappearing --
// an admin who hasn't gotten around to mapping an exam yet must never make it unreachable to
// students in the meantime.
//
// excludeIds (optional): exams to hide entirely rather than show as selectable -- used by the My
// Exams management screen's "Add Exam" panel, where an already-added exam shouldn't be offered
// again. pendingId (optional): shows a spinner on that one row instead of its checkbox, for a
// tap-to-add flow that's mid-request (same screen). onExamsLoaded (optional): fires once with the
// full flat exam list right after it loads -- lets a wrapper like OrganizationExamFilterDropdown
// resolve id->name for its own summary text without a second, duplicate fetch.
export default function OrganizationExamPicker({ selectedIds, onToggle, excludeIds, pendingId, onExamsLoaded }) {
  const [organizations, setOrganizations] = useState([]);
  const [exams, setExams] = useState([]);
  const [status, setStatus] = useState("loading");
  const [search, setSearch] = useState("");
  const [expandedIds, setExpandedIds] = useState(() => new Set());

  useEffect(() => {
    let cancelled = false;
    Promise.all([listOrganizations(), listExams()])
      .then(([orgs, allExams]) => {
        if (cancelled) return;
        setOrganizations(orgs);
        setExams(allExams);
        setStatus("ready");
        onExamsLoaded?.(allExams);
      })
      .catch(() => !cancelled && setStatus("error"));
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const selectedSet = useMemo(() => new Set(selectedIds), [selectedIds]);

  const examsByOrgId = useMemo(() => {
    const map = new Map();
    const unassigned = [];
    for (const exam of exams) {
      if (!exam.organizationId) {
        unassigned.push(exam);
        continue;
      }
      if (!map.has(exam.organizationId)) map.set(exam.organizationId, []);
      map.get(exam.organizationId).push(exam);
    }
    return { byOrg: map, unassigned };
  }, [exams]);

  const term = search.trim().toLowerCase();
  const isSearching = term.length > 0;

  function examMatches(exam) {
    if (excludeIds && excludeIds.has(exam.id)) return false;
    return !isSearching || exam.name.toLowerCase().includes(term);
  }

  function toggleExpanded(orgId) {
    setExpandedIds((prev) => {
      const next = new Set(prev);
      if (next.has(orgId)) next.delete(orgId); else next.add(orgId);
      return next;
    });
  }

  if (status === "loading") {
    return (
      <div className="flex justify-center py-8">
        <Loader2 className="h-5 w-5 animate-spin text-primary-400" />
      </div>
    );
  }

  if (status === "error") {
    return (
      <div className="flex items-center gap-2 rounded-xl2 border border-accent-100 bg-accent-50 p-3 text-sm text-accent-600">
        <AlertCircle className="h-4 w-4 shrink-0" strokeWidth={2.25} />
        Couldn't load exams. Please refresh and try again.
      </div>
    );
  }

  const orgSections = organizations
    .map((org) => ({ org, exams: (examsByOrgId.byOrg.get(org.id) || []).filter(examMatches) }))
    .filter(({ exams: orgExams }) => !isSearching || orgExams.length > 0);
  const unassignedExams = examsByOrgId.unassigned.filter(examMatches);

  return (
    <div>
      <div className="flex items-center gap-2 rounded-xl border border-primary-100 px-3 py-2">
        <Search className="h-4 w-4 shrink-0 text-ink-300" strokeWidth={2.25} />
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search exams..."
          className="w-full bg-transparent text-sm text-ink-900 outline-none placeholder:text-ink-300"
        />
      </div>

      <div className="mt-2 space-y-1.5">
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
                  <span className="rounded-full bg-primary-50 px-2 py-0.5 text-[11px] font-bold text-primary-600">
                    {selectedCount}
                  </span>
                )}
                <span className="text-xs text-ink-400">{org.examCount}</span>
                <ChevronDown
                  className={`h-4 w-4 shrink-0 text-ink-400 transition-transform ${expanded ? "rotate-180" : ""}`}
                  strokeWidth={2.25}
                />
              </button>
              {expanded && (
                <div className="border-t border-primary-100 bg-surface/60 p-1.5">
                  {orgExams.length === 0 ? (
                    <p className="px-2 py-2 text-xs text-ink-400">No exams found.</p>
                  ) : (
                    orgExams.map((exam) => (
                      <ExamRow
                        key={exam.id}
                        exam={exam}
                        selected={selectedSet.has(exam.id)}
                        pending={pendingId === exam.id}
                        onTap={() => onToggle(exam.id, exam.name)}
                      />
                    ))
                  )}
                </div>
              )}
            </div>
          );
        })}

        {unassignedExams.length > 0 && (
          <div className="overflow-hidden rounded-xl2 border border-primary-100">
            <button
              type="button"
              onClick={() => toggleExpanded("__unassigned")}
              className="flex w-full items-center gap-2 bg-white px-3 py-2.5 text-left hover:bg-primary-50"
            >
              <span className="min-w-0 flex-1 truncate text-sm font-bold text-ink-900">Other Exams</span>
              <ChevronDown
                className={`h-4 w-4 shrink-0 text-ink-400 transition-transform ${
                  isSearching || expandedIds.has("__unassigned") ? "rotate-180" : ""
                }`}
                strokeWidth={2.25}
              />
            </button>
            {(isSearching || expandedIds.has("__unassigned")) && (
              <div className="border-t border-primary-100 bg-surface/60 p-1.5">
                {unassignedExams.map((exam) => (
                  <ExamRow
                    key={exam.id}
                    exam={exam}
                    selected={selectedSet.has(exam.id)}
                    pending={pendingId === exam.id}
                    onTap={() => onToggle(exam.id, exam.name)}
                  />
                ))}
              </div>
            )}
          </div>
        )}

        {orgSections.length === 0 && unassignedExams.length === 0 && (
          <p className="px-1 py-4 text-center text-sm text-ink-400">No matching exams.</p>
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
      className={`flex w-full items-center gap-2.5 rounded-xl px-2.5 py-2 text-left transition-colors ${
        selected ? "bg-primary-50" : "hover:bg-white"
      }`}
    >
      {pending ? (
        <Loader2 className="h-5 w-5 shrink-0 animate-spin text-primary-400" />
      ) : (
        <span
          className={`flex h-5 w-5 shrink-0 items-center justify-center rounded-md border-2 ${
            selected ? "border-primary-600 bg-primary-600 text-white" : "border-ink-200"
          }`}
        >
          {selected && <Check className="h-3.5 w-3.5" strokeWidth={3} />}
        </span>
      )}
      <span className="min-w-0 flex-1 truncate text-sm font-semibold text-ink-900">{exam.name}</span>
    </button>
  );
}
