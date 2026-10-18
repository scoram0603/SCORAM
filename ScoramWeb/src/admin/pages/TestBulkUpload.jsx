import { useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { ArrowLeft, UploadCloud, Download, CheckCircle2, XCircle, AlertTriangle, ChevronDown, ChevronRight, Undo2, History } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  previewTestImport, commitTestImport, getTestImportHistory, getTestImportJob, rollbackTestImport, downloadTestImportTemplate,
} from "../api/testImport";
import { PageHeader, Card, Button, Alert, friendlyError } from "../components/AdminUI";

const KINDS = {
  mock: {
    title: "Bulk Upload Mock Tests",
    subtitle: "Create many mock tests at once from Excel, CSV or JSON — with sections, section-wise timing and result release.",
    back: "/admin/mock-tests",
    noun: "mock test",
  },
  practice: {
    title: "Bulk Upload Practice Templates",
    subtitle: "Create many practice templates at once — filter-based (random from the Question Bank) or fixed question sets.",
    back: "/admin/practice-tests",
    noun: "practice template",
  },
};

const ACCEPT = ".xlsx,.csv,.json";

function fmtDate(value) {
  if (!value) return "—";
  return new Date(value).toLocaleString();
}

function StatusPill({ status }) {
  const styles = {
    Committed: "bg-mint-50 text-mint-500",
    RolledBack: "bg-amber-50 text-amber-600",
    Failed: "bg-red-50 text-red-600",
  };
  return <span className={`rounded-full px-2 py-0.5 text-[11px] font-semibold ${styles[status] || "bg-primary-50 text-primary-600"}`}>{status}</span>;
}

export default function TestBulkUpload() {
  const { kind: kindParam } = useParams();
  const kind = KINDS[kindParam] ? kindParam : "mock";
  const cfg = KINDS[kind];
  const { token } = useAdminAuth();
  const navigate = useNavigate();
  const fileInputRef = useRef(null);

  const [file, setFile] = useState(null);
  const [previewing, setPreviewing] = useState(false);
  const [preview, setPreview] = useState(null);
  const [checked, setChecked] = useState(new Set());
  const [expanded, setExpanded] = useState(new Set());
  const [error, setError] = useState(null);

  const [committing, setCommitting] = useState(false);
  const [result, setResult] = useState(null);

  const [history, setHistory] = useState(null);
  const [openJob, setOpenJob] = useState(null); // { id, tests }
  const [rollingBackId, setRollingBackId] = useState(null);
  const [historyMessage, setHistoryMessage] = useState(null);

  useEffect(() => {
    setFile(null); setPreview(null); setResult(null); setError(null); setOpenJob(null);
    refreshHistory();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [kind, token]);

  function refreshHistory() {
    getTestImportHistory(token, kind).then(setHistory).catch(() => setHistory([]));
  }

  async function handleDownload(format) {
    setError(null);
    try {
      await downloadTestImportTemplate(token, kind, format);
    } catch (err) {
      setError(friendlyError(err));
    }
  }

  async function handlePreview() {
    if (!file) return;
    setPreviewing(true);
    setError(null);
    setResult(null);
    try {
      const data = await previewTestImport(token, kind, file);
      setPreview(data);
      setChecked(new Set(data.tests.filter((t) => t.isValid).map((t) => t.index)));
      setExpanded(new Set(data.tests.filter((t) => !t.isValid).map((t) => t.index)));
    } catch (err) {
      setPreview(null);
      setError(friendlyError(err));
    } finally {
      setPreviewing(false);
    }
  }

  function toggle(setter, index) {
    setter((prev) => {
      const next = new Set(prev);
      if (next.has(index)) next.delete(index); else next.add(index);
      return next;
    });
  }

  async function handleCommit() {
    if (!preview || checked.size === 0) return;
    setCommitting(true);
    setError(null);
    try {
      const res = await commitTestImport(token, preview.token, [...checked]);
      setResult(res);
      setPreview(null);
      setFile(null);
      if (fileInputRef.current) fileInputRef.current.value = "";
      refreshHistory();
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setCommitting(false);
    }
  }

  async function handleRollback(job) {
    if (!window.confirm(`Roll back "${job.fileName}"? This deletes the ${job.importedTests} test(s) it created and the new questions made only for them.`)) return;
    setRollingBackId(job.id);
    setHistoryMessage(null);
    setError(null);
    try {
      const res = await rollbackTestImport(token, job.id);
      setHistoryMessage(
        `Rolled back: ${res.testsRemoved} test(s) and ${res.questionsRemoved} question(s) removed` +
          (res.questionsKept ? `; ${res.questionsKept} question(s) kept because other tests use them.` : "."),
      );
      refreshHistory();
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setRollingBackId(null);
    }
  }

  async function handleOpenJob(job) {
    if (openJob?.id === job.id) { setOpenJob(null); return; }
    try {
      const detail = await getTestImportJob(token, job.id);
      setOpenJob({ id: job.id, tests: detail.tests || [] });
    } catch (err) {
      setError(friendlyError(err));
    }
  }

  const validCount = preview?.tests.filter((t) => t.isValid).length ?? 0;

  return (
    <div>
      <PageHeader
        title={cfg.title}
        subtitle={cfg.subtitle}
        action={
          <Button variant="secondary" onClick={() => navigate(cfg.back)}>
            <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
            Back
          </Button>
        }
      />

      <div className="space-y-5 p-6">
        {error && <Alert>{error}</Alert>}
        {historyMessage && <Alert type="success">{historyMessage}</Alert>}

        {result && (
          <Alert type="success">
            Imported {result.importedTests} {cfg.noun}(s) — {result.newQuestionsCreated} new question(s) created (test-only, hidden from PYQ search),
            {" "}{result.existingQuestionsReused} existing Question Bank question(s) reused. Imported tests are in Draft unless your file said otherwise.
          </Alert>
        )}

        {/* ---------- step 1: templates + file ---------- */}
        <Card>
          <h2 className="mb-1 text-sm font-bold text-ink-900">1. Get a template</h2>
          <p className="mb-3 text-xs text-ink-400">
            One row = one question (Excel/CSV), or nested tests → sections → questions (JSON). The template's Instructions sheet explains every column.
            Each question can be a brand-new question, a question that already exists in the Question Bank (by its text or id), or both kinds mixed in one test.
          </p>
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => handleDownload("xlsx")}><Download className="h-4 w-4" />Excel (.xlsx)</Button>
            <Button variant="secondary" onClick={() => handleDownload("csv")}><Download className="h-4 w-4" />CSV</Button>
            <Button variant="secondary" onClick={() => handleDownload("json")}><Download className="h-4 w-4" />JSON</Button>
          </div>
        </Card>

        <Card>
          <h2 className="mb-3 text-sm font-bold text-ink-900">2. Upload and preview</h2>
          <div className="flex flex-wrap items-center gap-3">
            <input
              ref={fileInputRef}
              type="file"
              accept={ACCEPT}
              onChange={(e) => { setFile(e.target.files?.[0] || null); setPreview(null); setResult(null); }}
              className="text-xs"
            />
            <Button onClick={handlePreview} isLoading={previewing} disabled={!file}>
              <UploadCloud className="h-4 w-4" />
              Preview
            </Button>
          </div>
          <p className="mt-2 text-xs text-ink-400">.xlsx, .csv or .json, up to 10 MB. Nothing is saved until you confirm below.</p>
        </Card>

        {/* ---------- step 3: preview ---------- */}
        {preview && (
          <Card>
            <h2 className="mb-1 text-sm font-bold text-ink-900">3. Review</h2>
            <p className="mb-3 text-xs text-ink-600">
              {preview.totalTests} test(s) found — <span className="font-semibold text-mint-500">{preview.validTests} valid</span>
              {preview.invalidTests > 0 && <>, <span className="font-semibold text-red-600">{preview.invalidTests} with errors</span> (they will be skipped)</>}.
              {" "}Valid tests use {preview.totalNewQuestions} new and {preview.totalExistingQuestions} existing questions.
            </p>

            {preview.fileErrors.map((m, i) => <div key={i} className="mb-2"><Alert>{m}</Alert></div>)}

            <div className="divide-y divide-primary-50 rounded-xl2 border border-primary-100">
              {preview.tests.map((t) => (
                <div key={t.index} className="p-3">
                  <div className="flex flex-wrap items-center gap-3">
                    <input
                      type="checkbox"
                      checked={checked.has(t.index)}
                      disabled={!t.isValid}
                      onChange={() => toggle(setChecked, t.index)}
                      aria-label={`Import ${t.title}`}
                    />
                    {t.isValid ? <CheckCircle2 className="h-4 w-4 text-mint-500" /> : <XCircle className="h-4 w-4 text-red-600" />}
                    <button type="button" className="flex items-center gap-1 text-left text-sm font-semibold text-ink-900" onClick={() => toggle(setExpanded, t.index)}>
                      {expanded.has(t.index) ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                      {t.title || <span className="text-ink-400">(no title)</span>}
                    </button>
                    <span className="text-xs text-ink-400">
                      {t.exam && <>{t.exam} · </>}
                      {t.isCurated || kind === "mock" ? `${t.questionCount} questions` : "filter-based"}
                      {t.totalMinutes ? ` · ${t.totalMinutes} min` : ""}
                      {t.status ? ` · ${t.status}` : ""}
                      {kind === "mock" && t.resultRelease && t.resultRelease !== "Instant" ? ` · result: ${t.resultRelease}` : ""}
                    </span>
                    {t.isValid && (t.newQuestions > 0 || t.existingQuestions > 0) && (
                      <span className="text-xs text-ink-400">{t.newQuestions} new · {t.existingQuestions} existing</span>
                    )}
                    {t.warnings.length > 0 && <AlertTriangle className="h-4 w-4 text-amber-500" title={`${t.warnings.length} warning(s)`} />}
                  </div>

                  {expanded.has(t.index) && (
                    <div className="mt-2 space-y-2 pl-8 text-xs">
                      {t.errors.map((m, i) => <div key={`e${i}`} className="text-red-600">• {m}</div>)}
                      {t.warnings.map((m, i) => <div key={`w${i}`} className="text-amber-600">• {m}</div>)}
                      {t.sections.length > 0 && (
                        <table className="text-xs text-ink-600">
                          <thead><tr className="text-left text-ink-400"><th className="pr-6">Section</th><th className="pr-6">Time</th><th>Questions</th></tr></thead>
                          <tbody>
                            {t.sections.map((s) => (
                              <tr key={s.name}><td className="pr-6">{s.name}</td><td className="pr-6">{s.durationMinutes ? `${s.durationMinutes} min` : "—"}</td><td>{s.questionCount}</td></tr>
                            ))}
                          </tbody>
                        </table>
                      )}
                    </div>
                  )}
                </div>
              ))}
            </div>

            <div className="mt-4 flex flex-wrap items-center gap-3">
              <Button onClick={handleCommit} isLoading={committing} disabled={checked.size === 0}>
                Import {checked.size} of {validCount} valid test(s)
              </Button>
              <Button variant="ghost" onClick={() => setPreview(null)}>Cancel</Button>
            </div>
          </Card>
        )}

        {/* ---------- history ---------- */}
        <Card>
          <h2 className="mb-3 flex items-center gap-2 text-sm font-bold text-ink-900"><History className="h-4 w-4" />Import history</h2>
          {history === null && <p className="text-xs text-ink-400">Loading…</p>}
          {history && history.length === 0 && <p className="text-xs text-ink-400">No imports yet.</p>}
          {history && history.length > 0 && (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs">
                <thead className="bg-primary-50 text-ink-600">
                  <tr>
                    <th className="px-3 py-2">File</th><th className="px-3 py-2">When</th><th className="px-3 py-2">By</th>
                    <th className="px-3 py-2">Tests</th><th className="px-3 py-2">New / reused Qs</th><th className="px-3 py-2">Status</th><th className="px-3 py-2"></th>
                  </tr>
                </thead>
                <tbody>
                  {history.map((job) => (
                    <FragmentRow key={job.id} job={job} open={openJob?.id === job.id} tests={openJob?.id === job.id ? openJob.tests : null}
                      onOpen={() => handleOpenJob(job)} onRollback={() => handleRollback(job)} rolling={rollingBackId === job.id} />
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      </div>
    </div>
  );
}

function FragmentRow({ job, open, tests, onOpen, onRollback, rolling }) {
  return (
    <>
      <tr className="border-t border-primary-50">
        <td className="px-3 py-2.5 font-medium text-ink-900">{job.fileName}</td>
        <td className="px-3 py-2.5 text-ink-600">{fmtDate(job.committedAt || job.createdAt)}</td>
        <td className="px-3 py-2.5 text-ink-600">{job.createdByAdminName || "—"}</td>
        <td className="px-3 py-2.5 text-ink-600">{job.importedTests}</td>
        <td className="px-3 py-2.5 text-ink-600">{job.newQuestionsCreated} / {job.existingQuestionsReused}</td>
        <td className="px-3 py-2.5"><StatusPill status={job.status} /></td>
        <td className="px-3 py-2.5">
          <div className="flex gap-2">
            {job.status === "Committed" && <Button variant="ghost" onClick={onOpen}>{open ? "Hide" : "View"} tests</Button>}
            {job.status === "Committed" && (
              <Button variant="danger" isLoading={rolling} onClick={onRollback}><Undo2 className="h-4 w-4" />Rollback</Button>
            )}
          </div>
        </td>
      </tr>
      {open && tests && (
        <tr className="bg-primary-50/40">
          <td colSpan={7} className="px-6 py-3 text-xs text-ink-600">
            {tests.length === 0 ? "No tests found." : tests.map((t) => (
              <div key={t.id}>{t.businessId ? `${t.businessId} · ` : ""}{t.title} — {t.questionCount} questions · {t.status}</div>
            ))}
          </td>
        </tr>
      )}
    </>
  );
}
