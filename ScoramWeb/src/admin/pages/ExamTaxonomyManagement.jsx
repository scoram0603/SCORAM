import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Archive, CheckCircle2, Eye, Layers, ListTree, Pencil, Plus, RotateCcw, Search, Trash2, Wand2 } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  listTaxonomyExams, getExamTaxonomy, listExamTopics, listExamQuestions, unmapExamQuestions,
} from "../api/examTaxonomy";
import { PageHeader, Card, Button, TextInput, Select, Alert, friendlyError } from "../components/AdminUI";
import Pager from "../components/Pager";
import {
  NameDialog, UsageDialog, DeleteDialog, MergeDialog, InitDialog, MapDialog, ClassificationsDialog, useTaxonomyActions,
} from "../components/ExamTaxonomyDialogs";

const num = (v) => (v ?? 0).toLocaleString();
const STATUS = [["all", "All"], ["active", "Active"], ["archived", "Archived"]];
const QUESTION_PAGE = 20;

function StatusPill({ active }) {
  return (
    <span className={`rounded-full px-2.5 py-1 text-[11px] font-bold ${active ? "bg-mint-50 text-mint-500" : "bg-surface text-ink-400"}`}>
      {active ? "Active" : "Archived"}
    </span>
  );
}

function Tabs({ value, onChange, items }) {
  return (
    <div className="flex gap-1 rounded-xl2 bg-surface p-1" role="tablist">
      {items.map(([key, label]) => (
        <button key={key} type="button" role="tab" aria-selected={value === key} onClick={() => onChange(key)}
          className={`rounded-xl2 px-3.5 py-1.5 text-xs font-bold transition-colors ${value === key ? "bg-white text-primary-600 shadow-card" : "text-ink-400 hover:text-ink-600"}`}>
          {label}
        </button>
      ))}
    </div>
  );
}

// Admin > Exam-wise Subject & Topic Management.
//
// Pick an exam, then manage THAT exam's subjects and topics and how its questions are classified. Everything
// here is per exam: a rename/delete never touches another exam, the global subject list, or any question's
// content, and the same question can be classified differently in each exam it belongs to.
export default function ExamTaxonomyManagement() {
  const { token } = useAdminAuth();
  const actions = useTaxonomyActions();
  const [params, setParams] = useSearchParams();
  const examId = params.get("exam") || "";

  const [exams, setExams] = useState([]);
  const [overview, setOverview] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [notice, setNotice] = useState(null);

  // subjects pane filters
  const [subjectSearch, setSubjectSearch] = useState("");
  const [subjectStatus, setSubjectStatus] = useState("all");
  const [subjectId, setSubjectId] = useState("");

  // right pane
  const [tab, setTab] = useState("topics");
  const [topics, setTopics] = useState(null);
  const [topicSearch, setTopicSearch] = useState("");
  const [topicStatus, setTopicStatus] = useState("all");

  // questions tab
  const [scope, setScope] = useState("subject"); // subject | topic | unassigned | all
  const [topicFilterId, setTopicFilterId] = useState("");
  const [qSearch, setQSearch] = useState("");
  const [qPage, setQPage] = useState(1);
  const [questions, setQuestions] = useState(null);
  const [selected, setSelected] = useState(() => new Set());

  const [dialog, setDialog] = useState(null);
  const [busy, setBusy] = useState(false);
  const seq = useRef(0);

  useEffect(() => { listTaxonomyExams(token).then(setExams).catch((e) => setError(friendlyError(e))); }, [token]);

  useEffect(() => {
    if (!notice) return undefined;
    const t = setTimeout(() => setNotice(null), 7000);
    return () => clearTimeout(t);
  }, [notice]);

  const loadOverview = useCallback(async () => {
    if (!examId) { setOverview(null); return; }
    const my = ++seq.current;
    setLoading(true);
    setError(null);
    try {
      const res = await getExamTaxonomy(token, examId, { search: subjectSearch, status: subjectStatus });
      if (my !== seq.current) return;
      setOverview(res);
    } catch (err) {
      if (my === seq.current) setError(friendlyError(err));
    } finally {
      if (my === seq.current) setLoading(false);
    }
  }, [token, examId, subjectSearch, subjectStatus]);

  useEffect(() => { const t = setTimeout(loadOverview, 200); return () => clearTimeout(t); }, [loadOverview]);

  // Changing exam resets everything that belonged to the previous one.
  useEffect(() => {
    setSubjectId(""); setTopics(null); setSelected(new Set()); setQuestions(null); setQPage(1); setScope("subject"); setTopicFilterId("");
    setSubjectSearch(""); setSubjectStatus("all");
  }, [examId]);

  const subjects = overview?.subjects ?? [];
  const selectedSubject = subjects.find((s) => s.id === subjectId) || null;
  const exam = overview?.exam;
  const examName = exam?.name ?? exams.find((e) => e.id === examId)?.name ?? "";

  // A subject that disappears (deleted/filtered out) can't stay selected.
  useEffect(() => {
    if (subjectId && overview && !subjects.some((s) => s.id === subjectId) && subjectStatus === "all" && !subjectSearch) setSubjectId("");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [overview]);

  const loadTopics = useCallback(async () => {
    if (!examId || !subjectId) { setTopics(null); return; }
    try {
      const res = await listExamTopics(token, examId, subjectId, { search: topicSearch, status: topicStatus });
      setTopics(res.topics);
    } catch (err) { setError(friendlyError(err)); }
  }, [token, examId, subjectId, topicSearch, topicStatus]);
  useEffect(() => { const t = setTimeout(loadTopics, 200); return () => clearTimeout(t); }, [loadTopics]);

  const loadQuestions = useCallback(async () => {
    if (!examId) return;
    const q = { search: qSearch, page: qPage, pageSize: QUESTION_PAGE };
    if (scope === "unassigned") q.unassigned = true;
    else if (scope === "topic" && topicFilterId) q.examTopicId = topicFilterId;
    else if (scope === "subject" && subjectId) q.examSubjectId = subjectId;
    else if (scope === "subject" || scope === "topic") { setQuestions({ items: [], totalCount: 0 }); return; }
    try { setQuestions(await listExamQuestions(token, examId, q)); } catch (err) { setError(friendlyError(err)); }
  }, [token, examId, scope, subjectId, topicFilterId, qSearch, qPage]);
  useEffect(() => { if (tab !== "questions") return undefined; const t = setTimeout(loadQuestions, 200); return () => clearTimeout(t); }, [tab, loadQuestions]);

  const refreshAll = async (message) => {
    setDialog(null);
    if (message) setNotice(message);
    setSelected(new Set());
    await Promise.all([loadOverview(), loadTopics(), tab === "questions" ? loadQuestions() : null]);
    listTaxonomyExams(token).then(setExams).catch(() => {});
  };

  const run = async (fn, okMessage) => {
    if (busy) return;
    setBusy(true); setError(null);
    try { const res = await fn(); await refreshAll(okMessage ?? res?.message); }
    catch (err) { setError(friendlyError(err)); await loadOverview(); }
    finally { setBusy(false); }
  };

  const pickExam = (id) => { const next = new URLSearchParams(params); if (id) next.set("exam", id); else next.delete("exam"); setParams(next, { replace: true }); };

  const toggle = (id) => setSelected((prev) => { const next = new Set(prev); if (next.has(id)) next.delete(id); else next.add(id); return next; });
  const items = questions?.items ?? [];
  const allOnPage = items.length > 0 && items.every((i) => selected.has(i.id));

  const topicOptions = useMemo(() => topics ?? [], [topics]);

  return (
    <div>
      <PageHeader
        title="Exam-wise Subject & Topic Management"
        subtitle="Rename, add and remove subjects and topics for one exam at a time, and decide how its questions are classified — other exams are never affected."
      />

      <div className="space-y-4 p-4 sm:p-6">
        {notice && (
          <div className="flex items-start gap-2 rounded-xl2 bg-mint-50 p-3 text-sm font-medium text-mint-500" role="status">
            <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" /> {notice}
          </div>
        )}
        {error && <Alert>{error}</Alert>}

        <Card className="!p-4">
          <div className="grid gap-3 sm:grid-cols-[minmax(0,360px)_1fr] sm:items-end">
            <label className="block">
              <span className="mb-1 block text-xs font-semibold text-ink-600">1. Select an exam</span>
              <Select value={examId} onChange={(e) => pickExam(e.target.value)} aria-label="Exam">
                <option value="">Choose an exam…</option>
                {exams.map((e) => (
                  <option key={e.id} value={e.id}>{e.name}{e.businessId ? ` (${e.businessId})` : ""} — {num(e.questionCount)} questions{e.initialized ? "" : e.questionCount > 0 ? " · not initialised" : ""}</option>
                ))}
              </Select>
            </label>
            {exam && (
              <div className="flex flex-wrap gap-x-6 gap-y-1 text-sm text-ink-600">
                <span><b className="text-ink-900">{num(overview.totalQuestions)}</b> questions</span>
                <span><b className="text-ink-900">{num(subjects.length)}</b> subjects</span>
                <span className={overview.unassignedQuestions ? "text-accent-600" : ""}><b>{num(overview.unassignedQuestions)}</b> unassigned</span>
              </div>
            )}
          </div>
        </Card>

        {!examId && (
          <Card><p className="text-sm text-ink-400">Choose an exam to see its subjects and topics.</p></Card>
        )}

        {examId && overview && !overview.initialized && overview.totalQuestions > 0 && (
          <Card className="border border-accent-200 bg-accent-50 !p-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="text-sm text-accent-600">
                <p className="font-bold">{examName} doesn't have its own subject &amp; topic list yet.</p>
                <p className="text-xs">Its {num(overview.totalQuestions)} questions currently use the global classification. Initialise the list from it to start editing it for this exam only.</p>
              </div>
              <Button onClick={() => setDialog({ type: "init" })}><Wand2 className="h-4 w-4" /> Initialise from current classification</Button>
            </div>
          </Card>
        )}

        {examId && overview && (overview.initialized || overview.totalQuestions === 0) && (
          <div className="grid gap-4 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]">
            {/* ---------------- subjects ---------------- */}
            <Card className="!p-0 overflow-hidden">
              <div className="flex flex-wrap items-center gap-2 border-b border-primary-100 p-3">
                <h2 className="mr-auto text-sm font-extrabold text-ink-900">2. Subjects in {examName}</h2>
                <Button className="!px-3 !py-1.5 text-xs" onClick={() => setDialog({ type: "subject-create" })}><Plus className="h-3.5 w-3.5" /> Add subject</Button>
              </div>
              <div className="flex flex-wrap items-center gap-2 border-b border-primary-100 p-3">
                <div className="relative min-w-[150px] flex-1">
                  <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" />
                  <TextInput value={subjectSearch} onChange={(e) => setSubjectSearch(e.target.value)} placeholder="Search subjects…" className="!pl-9 !py-2" aria-label="Search subjects" />
                </div>
                <Tabs value={subjectStatus} onChange={setSubjectStatus} items={STATUS} />
              </div>
              <ul className={`divide-y divide-primary-50 ${loading ? "opacity-60" : ""}`}>
                {subjects.length === 0 && <li className="p-4 text-sm text-ink-400">{subjectSearch || subjectStatus !== "all" ? "No subjects match." : "No subjects yet — add the first one."}</li>}
                {subjects.map((s) => (
                  <li key={s.id} className={`p-3 ${s.id === subjectId ? "bg-primary-50/60" : ""}`}>
                    <div className="flex items-start gap-2">
                      <button type="button" onClick={() => { setSubjectId(s.id); setTopicFilterId(""); setQPage(1); }} className="min-w-0 flex-1 text-left">
                        <span className="block truncate font-bold text-ink-900 hover:text-secondary-500">{s.name}</span>
                        <span className="text-xs text-ink-400">
                          {num(s.questionCount)} questions · {num(s.topicCount)} topics
                          {s.baseSubjectName && s.baseSubjectName !== s.name && <> · from “{s.baseSubjectName}”</>}
                        </span>
                      </button>
                      <StatusPill active={s.isActive} />
                    </div>
                    <div className="mt-2 flex flex-wrap gap-1">
                      <IconBtn label="Rename" icon={Pencil} onClick={() => setDialog({ type: "subject-rename", item: s })} />
                      <IconBtn label="Usage" icon={Eye} onClick={() => setDialog({ type: "subject-usage", item: s })} />
                      {s.isActive
                        ? <IconBtn label="Archive" icon={Archive} onClick={() => run(() => actions.setSubjectActive(examId, s, false))} />
                        : <IconBtn label="Restore" icon={RotateCcw} onClick={() => run(() => actions.setSubjectActive(examId, s, true))} />}
                      <IconBtn label="Delete" icon={Trash2} danger onClick={() => setDialog({ type: "subject-delete", item: s })} />
                    </div>
                  </li>
                ))}
              </ul>
            </Card>

            {/* ---------------- topics / questions ---------------- */}
            <Card className="!p-0 overflow-hidden">
              {!selectedSubject ? (
                <div className="flex h-full min-h-[220px] flex-col items-center justify-center gap-2 p-6 text-center text-sm text-ink-400">
                  <ListTree className="h-8 w-8 text-primary-200" />
                  <p>3. Select a subject to manage its topics, or open the questions tab to map questions.</p>
                  <Button variant="secondary" className="!py-1.5 text-xs" onClick={() => { setTab("questions"); setScope("unassigned"); setQPage(1); }}>Show unassigned questions</Button>
                </div>
              ) : (
                <>
                  <div className="flex flex-wrap items-center gap-2 border-b border-primary-100 p-3">
                    <h2 className="mr-auto min-w-0 truncate text-sm font-extrabold text-ink-900">{selectedSubject.name}</h2>
                    <Tabs value={tab} onChange={(t) => { setTab(t); if (t === "questions" && scope === "unassigned") setScope("subject"); setQPage(1); }} items={[["topics", "Topics"], ["questions", "Questions"]]} />
                  </div>

                  {tab === "topics" && (
                    <>
                      <div className="flex flex-wrap items-center gap-2 border-b border-primary-100 p-3">
                        <div className="relative min-w-[150px] flex-1">
                          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" />
                          <TextInput value={topicSearch} onChange={(e) => setTopicSearch(e.target.value)} placeholder="Search topics…" className="!pl-9 !py-2" aria-label="Search topics" />
                        </div>
                        <Tabs value={topicStatus} onChange={setTopicStatus} items={STATUS} />
                        <Button className="!px-3 !py-1.5 text-xs" disabled={!selectedSubject.isActive} onClick={() => setDialog({ type: "topic-create" })}><Plus className="h-3.5 w-3.5" /> Add topic</Button>
                      </div>
                      <ul className="divide-y divide-primary-50">
                        {topics === null && <li className="p-4 text-sm text-ink-400">Loading…</li>}
                        {topics?.length === 0 && <li className="p-4 text-sm text-ink-400">{topicSearch || topicStatus !== "all" ? "No topics match." : "No topics yet."}</li>}
                        {topics?.map((t) => (
                          <li key={t.id} className="flex flex-wrap items-center gap-2 p-3">
                            <div className="min-w-0 flex-1">
                              <span className="block truncate font-semibold text-ink-900">{t.name}</span>
                              <span className="text-xs text-ink-400">{num(t.questionCount)} questions{t.baseTopicName && t.baseTopicName !== t.name && <> · from “{t.baseTopicName}”</>}</span>
                            </div>
                            <StatusPill active={t.isActive} />
                            <div className="flex gap-1">
                              <IconBtn label="Questions" icon={Layers} onClick={() => { setTab("questions"); setScope("topic"); setTopicFilterId(t.id); setQPage(1); }} />
                              <IconBtn label="Rename" icon={Pencil} onClick={() => setDialog({ type: "topic-rename", item: t })} />
                              <IconBtn label="Usage" icon={Eye} onClick={() => setDialog({ type: "topic-usage", item: t })} />
                              {t.isActive
                                ? <IconBtn label="Archive" icon={Archive} onClick={() => run(() => actions.setTopicActive(examId, t, false))} />
                                : <IconBtn label="Restore" icon={RotateCcw} onClick={() => run(() => actions.setTopicActive(examId, t, true))} />}
                              <IconBtn label="Delete" icon={Trash2} danger onClick={() => setDialog({ type: "topic-delete", item: t })} />
                            </div>
                          </li>
                        ))}
                      </ul>
                    </>
                  )}

                  {tab === "questions" && (
                    <QuestionsPane
                      scope={scope} setScope={(v) => { setScope(v); setQPage(1); setSelected(new Set()); }}
                      topicOptions={topicOptions} topicFilterId={topicFilterId} setTopicFilterId={(v) => { setTopicFilterId(v); setQPage(1); }}
                      qSearch={qSearch} setQSearch={(v) => { setQSearch(v); setQPage(1); }}
                      questions={questions} items={items} selected={selected} toggle={toggle}
                      allOnPage={allOnPage}
                      toggleAll={() => setSelected((prev) => { const next = new Set(prev); items.forEach((i) => (allOnPage ? next.delete(i.id) : next.add(i.id))); return next; })}
                      page={qPage} onPage={setQPage}
                      onMap={() => setDialog({ type: "map" })}
                      onUnmap={() => run(() => unmapExamQuestions(token, examId, { questionIds: [...selected] }))}
                      onInspect={(q) => setDialog({ type: "classifications", item: q })}
                      busy={busy}
                    />
                  )}
                </>
              )}
            </Card>
          </div>
        )}
      </div>

      {/* ---------------- dialogs ---------------- */}
      {dialog?.type === "init" && <InitDialog examId={examId} examName={examName} onClose={() => setDialog(null)} onDone={(m) => refreshAll(m)} />}
      {dialog?.type === "subject-create" && (
        <NameDialog kind="subject" mode="create" examName={examName} onClose={() => setDialog(null)}
          onSubmit={async (name) => { const res = await actions.createSubject(examId, name); await refreshAll(res.message); if (res.subject) setSubjectId(res.subject.id); }} />
      )}
      {dialog?.type === "subject-rename" && (
        <NameDialog kind="subject" mode="rename" examName={examName} initial={dialog.item.name} onClose={() => setDialog(null)}
          onMerge={(clash) => setDialog({ type: "merge", kind: "subject", item: dialog.item, targetId: clash.existingId })}
          onSubmit={async (name) => { const res = await actions.renameSubject(examId, dialog.item, name); await refreshAll(res.message); }} />
      )}
      {dialog?.type === "subject-usage" && <UsageDialog kind="subject" examId={examId} examName={examName} item={dialog.item} onClose={() => setDialog(null)} />}
      {dialog?.type === "subject-delete" && (
        <DeleteDialog kind="subject" examId={examId} examName={examName} item={dialog.item} onClose={() => setDialog(null)} onDone={(m) => refreshAll(m)} />
      )}
      {dialog?.type === "topic-create" && selectedSubject && (
        <NameDialog kind="topic" mode="create" examName={examName} parentName={selectedSubject.name} onClose={() => setDialog(null)}
          onSubmit={async (name) => { const res = await actions.createTopic(examId, selectedSubject.id, name); await refreshAll(res.message); }} />
      )}
      {dialog?.type === "topic-rename" && (
        <NameDialog kind="topic" mode="rename" examName={examName} parentName={selectedSubject?.name} initial={dialog.item.name} onClose={() => setDialog(null)}
          onMerge={(clash) => setDialog({ type: "merge", kind: "topic", item: dialog.item, targetId: clash.existingId })}
          onSubmit={async (name) => { const res = await actions.renameTopic(examId, dialog.item, name); await refreshAll(res.message); }} />
      )}
      {dialog?.type === "topic-usage" && <UsageDialog kind="topic" examId={examId} examName={examName} item={dialog.item} onClose={() => setDialog(null)} />}
      {dialog?.type === "topic-delete" && (
        <DeleteDialog kind="topic" examId={examId} examName={examName} item={dialog.item} onClose={() => setDialog(null)} onDone={(m) => refreshAll(m)} />
      )}
      {dialog?.type === "merge" && (
        <MergeDialog kind={dialog.kind} examId={examId} examName={examName} source={dialog.item} targetId={dialog.targetId}
          onClose={() => setDialog(null)}
          onDone={async (m) => { if (dialog.kind === "subject" && subjectId === dialog.item.id) setSubjectId(dialog.targetId); await refreshAll(m); }} />
      )}
      {dialog?.type === "map" && (
        <MapDialog examId={examId} examName={examName} subjects={subjects} defaultSubjectId={subjectId} defaultTopicId={scope === "topic" ? topicFilterId : ""}
          questionIds={[...selected]} onClose={() => setDialog(null)} onDone={(m) => refreshAll(m)} />
      )}
      {dialog?.type === "classifications" && <ClassificationsDialog question={dialog.item} onClose={() => setDialog(null)} />}
    </div>
  );
}

function IconBtn({ label, icon: Icon, onClick, danger = false }) {
  return (
    <button type="button" onClick={onClick} title={label} aria-label={label}
      className={`inline-flex items-center gap-1 rounded-lg px-2 py-1 text-[11px] font-semibold ${danger ? "bg-red-50 text-red-600 hover:bg-red-100" : "bg-primary-50 text-primary-600 hover:bg-primary-100"}`}>
      <Icon className="h-3.5 w-3.5" /> {label}
    </button>
  );
}

function QuestionsPane({
  scope, setScope, topicOptions, topicFilterId, setTopicFilterId, qSearch, setQSearch, questions, items, selected, toggle,
  allOnPage, toggleAll, page, onPage, onMap, onUnmap, onInspect, busy,
}) {
  return (
    <div>
      <div className="flex flex-wrap items-center gap-2 border-b border-primary-100 p-3">
        <Select value={scope} onChange={(e) => setScope(e.target.value)} className="!w-auto !py-2 text-xs" aria-label="Which questions">
          <option value="subject">In this subject</option>
          <option value="topic">In a topic of this subject</option>
          <option value="unassigned">Unassigned in this exam</option>
        </Select>
        {scope === "topic" && (
          <Select value={topicFilterId} onChange={(e) => setTopicFilterId(e.target.value)} className="!w-auto !py-2 text-xs" aria-label="Topic">
            <option value="">Choose a topic…</option>
            {topicOptions.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
          </Select>
        )}
        <div className="relative min-w-[140px] flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" />
          <TextInput value={qSearch} onChange={(e) => setQSearch(e.target.value)} placeholder="Search question text…" className="!pl-9 !py-2" aria-label="Search questions" />
        </div>
      </div>
      <div className="flex flex-wrap items-center gap-2 border-b border-primary-100 bg-surface px-3 py-2 text-xs">
        <label className="flex items-center gap-2 font-semibold text-ink-600">
          <input type="checkbox" checked={allOnPage} onChange={toggleAll} disabled={items.length === 0} /> Select page
        </label>
        <span className="text-ink-400">{selected.size ? `${selected.size} selected` : `${num(questions?.totalCount)} questions`}</span>
        <span className="ml-auto flex gap-2">
          <Button className="!px-3 !py-1.5 text-xs" disabled={selected.size === 0 || busy} onClick={onMap}>Map to subject / topic…</Button>
          <Button variant="secondary" className="!px-3 !py-1.5 text-xs" disabled={selected.size === 0 || busy} onClick={onUnmap}>Unassign</Button>
        </span>
      </div>
      <ul className="divide-y divide-primary-50">
        {questions === null && <li className="p-4 text-sm text-ink-400">Loading…</li>}
        {questions && items.length === 0 && <li className="p-4 text-sm text-ink-400">No questions here.</li>}
        {items.map((q) => (
          <li key={q.id} className="flex gap-3 p-3">
            <input type="checkbox" checked={selected.has(q.id)} onChange={() => toggle(q.id)} className="mt-1" aria-label="Select question" />
            <div className="min-w-0 flex-1">
              <p className="line-clamp-2 text-sm text-ink-900">{q.questionText}</p>
              <p className="mt-1 text-[11px] text-ink-400">
                In this exam: <b className="text-ink-600">{q.examSubjectName || "Unassigned"}</b>{q.examTopicName ? <> › <b className="text-ink-600">{q.examTopicName}</b></> : null}
                {!q.explicit && q.examSubjectName ? " (from global classification)" : ""}
                {" · "}Global: {q.globalSubject} › {q.globalTopic}
                {q.years.length > 0 && <> · {q.years.join(", ")}</>}
              </p>
            </div>
            <button type="button" onClick={() => onInspect(q)} className="self-start rounded-lg bg-primary-50 px-2 py-1 text-[11px] font-semibold text-primary-600 hover:bg-primary-100" title="See this question's classification in every exam">All exams</button>
          </li>
        ))}
      </ul>
      <div className="px-3 pb-3">
        <Pager page={page} pageSize={QUESTION_PAGE} totalCount={questions?.totalCount ?? 0} onPage={onPage} />
      </div>
    </div>
  );
}
