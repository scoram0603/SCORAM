import { Fragment, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  UploadCloud, CheckCircle2, XCircle, Loader2, AlertTriangle, History, Undo2, Download, SquarePen,
  ChevronDown, ChevronRight,
} from "lucide-react";
import { previewBulkImport, commitBulkImport, getImportHistory, rollbackImport, updatePreviewRow, getImportJobQuestions } from "../api/bulkImport";
import { Card, Button, FormField, TextInput, TextArea, Select, Alert, friendlyError } from "./AdminUI";
import { QuestionCard, QuestionEditForm } from "./QuestionEditor";

const ACCEPTED = ".csv,.xlsx,.json";
const DIFFICULTIES = ["Easy", "Medium", "Hard"];

export default function BulkImportPanel({ paperId, token, onImported }) {
  const navigate = useNavigate();
  const fileInputRef = useRef(null);
  const [selectedFile, setSelectedFile] = useState(null);
  const [previewing, setPreviewing] = useState(false);
  const [preview, setPreview] = useState(null); // { jobId, fileName, format, totalRows, validCount, invalidCount, rows }
  const [checkedRows, setCheckedRows] = useState(new Set());
  const [expandedRows, setExpandedRows] = useState(new Set());
  const [error, setError] = useState(null);

  const [committing, setCommitting] = useState(false);
  const [commitResult, setCommitResult] = useState(null);

  const [history, setHistory] = useState(null);
  const [rollingBackId, setRollingBackId] = useState(null);

  // Expanding a Committed history entry lazily fetches that job's real questions (cached in
  // jobQuestions by jobId so re-expanding doesn't refetch) and lets the admin edit them right there,
  // grouped by upload batch instead of hunting through the paper's full Q.1..Q.N list.
  const [expandedJobId, setExpandedJobId] = useState(null);
  const [jobQuestions, setJobQuestions] = useState({}); // jobId -> Question[] | "loading" | "error"
  const [editingJobQuestionId, setEditingJobQuestionId] = useState(null);

  function refreshHistory() {
    getImportHistory(token, { paperId, page: 1, pageSize: 5 })
      .then((res) => setHistory(res.items))
      .catch(() => {});
  }

  useEffect(refreshHistory, [paperId, token]);

  function toggleJobExpanded(job) {
    if (expandedJobId === job.id) {
      setExpandedJobId(null);
      return;
    }
    setExpandedJobId(job.id);
    setEditingJobQuestionId(null);
    if (!jobQuestions[job.id]) {
      setJobQuestions((prev) => ({ ...prev, [job.id]: "loading" }));
      getImportJobQuestions(token, job.id)
        .then((questions) => setJobQuestions((prev) => ({ ...prev, [job.id]: questions })))
        .catch(() => setJobQuestions((prev) => ({ ...prev, [job.id]: "error" })));
    }
  }

  function handleFileChange(e) {
    const file = e.target.files?.[0];
    setSelectedFile(file || null);
    setPreview(null);
    setCommitResult(null);
    setError(null);
  }

  async function handlePreview() {
    if (!selectedFile) return;
    setPreviewing(true);
    setError(null);
    setCommitResult(null);
    try {
      const res = await previewBulkImport(token, paperId, selectedFile);
      setPreview(res);
      setCheckedRows(new Set(res.rows.filter((r) => r.isValid).map((r) => r.rowNumber)));
      setExpandedRows(new Set());
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setPreviewing(false);
    }
  }

  function toggleRow(rowNumber) {
    setCheckedRows((prev) => {
      const next = new Set(prev);
      if (next.has(rowNumber)) next.delete(rowNumber);
      else next.add(rowNumber);
      return next;
    });
  }

  function toggleExpanded(rowNumber) {
    setExpandedRows((prev) => {
      const next = new Set(prev);
      if (next.has(rowNumber)) next.delete(rowNumber);
      else next.add(rowNumber);
      return next;
    });
  }

  // Called after a row's edit form saves successfully -- swaps the corrected (and freshly
  // re-validated) row into the preview, and keeps checkedRows in sync: a row the admin just fixed
  // gets auto-checked (they clearly want it imported), one that somehow became invalid gets
  // unchecked (can't be committed anyway).
  function handleRowSaved(updatedRow) {
    setPreview((prev) => {
      if (!prev) return prev;
      const rows = prev.rows.map((r) => (r.rowNumber === updatedRow.rowNumber ? updatedRow : r));
      return { ...prev, rows, validCount: rows.filter((r) => r.isValid).length, invalidCount: rows.filter((r) => !r.isValid).length };
    });
    setCheckedRows((prev) => {
      const next = new Set(prev);
      if (updatedRow.isValid) next.add(updatedRow.rowNumber);
      else next.delete(updatedRow.rowNumber);
      return next;
    });
    setExpandedRows((prev) => {
      const next = new Set(prev);
      next.delete(updatedRow.rowNumber);
      return next;
    });
  }

  async function handleCommit() {
    if (!preview || checkedRows.size === 0) return;
    setCommitting(true);
    setError(null);
    try {
      const result = await commitBulkImport(token, preview.jobId, Array.from(checkedRows));
      setCommitResult(result);
      onImported?.(result.importedCount);
      refreshHistory();
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setCommitting(false);
    }
  }

  function handleStartOver() {
    setSelectedFile(null);
    setPreview(null);
    setCommitResult(null);
    setError(null);
    setExpandedRows(new Set());
    if (fileInputRef.current) fileInputRef.current.value = "";
  }

  async function handleRollback(jobId) {
    if (!window.confirm("Remove every question this import added? This can't be undone.")) return;
    setRollingBackId(jobId);
    try {
      await rollbackImport(token, jobId);
      refreshHistory();
    } catch (err) {
      window.alert(friendlyError(err));
    } finally {
      setRollingBackId(null);
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <p className="text-xs text-ink-400">
          Bulk-add text-only questions from a spreadsheet instead of one at a time. Expected columns:
          QuestionNumber, Subject, Topic, DifficultyLevel, QuestionText, OptionA–D, CorrectOption,
          Explanation (optional), SourceReference (optional). Images aren't supported here yet — add
          them afterward from the paper's question list.
        </p>

        <div className="mt-2 flex flex-wrap items-center gap-3 text-xs">
          <span className="text-ink-400">Not sure of the format? Download a sample:</span>
          <a href="/samples/sample-bulk-import.csv" download className="flex items-center gap-1 font-semibold text-secondary-500 hover:underline">
            <Download className="h-3.5 w-3.5" strokeWidth={2.25} />
            CSV
          </a>
          <a href="/samples/sample-bulk-import.xlsx" download className="flex items-center gap-1 font-semibold text-secondary-500 hover:underline">
            <Download className="h-3.5 w-3.5" strokeWidth={2.25} />
            Excel
          </a>
          <a href="/samples/sample-bulk-import.json" download className="flex items-center gap-1 font-semibold text-secondary-500 hover:underline">
            <Download className="h-3.5 w-3.5" strokeWidth={2.25} />
            JSON
          </a>
        </div>

        {!commitResult && (
          <div className="mt-3 flex flex-wrap items-center gap-3">
            <input ref={fileInputRef} type="file" accept={ACCEPTED} onChange={handleFileChange} className="text-sm" />
            <Button onClick={handlePreview} disabled={!selectedFile} isLoading={previewing}>
              <UploadCloud className="h-4 w-4" strokeWidth={2.25} />
              Preview
            </Button>
            {preview && (
              <Button variant="secondary" onClick={handleStartOver}>
                Choose a different file
              </Button>
            )}
          </div>
        )}

        {error && <div className="mt-3"><Alert>{error}</Alert></div>}

        {commitResult && (
          <div className="mt-3">
            <Alert type="success">
              Imported {commitResult.importedCount} question{commitResult.importedCount === 1 ? "" : "s"}
              {commitResult.skippedCount > 0 ? ` (${commitResult.skippedCount} row(s) skipped)` : ""}.
            </Alert>
            <p className="mt-2 text-xs text-ink-400">
              Spotted a wrong option, or need to add an explanation or image to one of these? Expand
              this batch in "Recent imports" below to fix it right here, or open the paper's full
              question list -- either works while the paper's still a Draft.
            </p>
            <div className="mt-3 flex flex-wrap gap-2">
              <Button variant="secondary" onClick={() => navigate(`/admin/papers/${paperId}`)}>
                <SquarePen className="h-4 w-4" strokeWidth={2.25} />
                Review &amp; edit these questions
              </Button>
              <Button variant="secondary" onClick={handleStartOver}>
                Import another file
              </Button>
            </div>
          </div>
        )}

        {preview && !commitResult && (
          <div className="mt-4">
            <div className="flex flex-wrap items-center gap-3 text-xs">
              <span className="flex items-center gap-1 font-semibold text-mint-500">
                <CheckCircle2 className="h-3.5 w-3.5" strokeWidth={2.5} />
                {preview.validCount} valid
              </span>
              {preview.invalidCount > 0 && (
                <span className="flex items-center gap-1 font-semibold text-red-600">
                  <XCircle className="h-3.5 w-3.5" strokeWidth={2.5} />
                  {preview.invalidCount} invalid (won't be imported)
                </span>
              )}
              <span className="text-ink-400">{checkedRows.size} selected to import</span>
              <span className="text-ink-300">· click a row to review &amp; edit it before importing</span>
            </div>

            <div className="mt-3 max-h-96 overflow-y-auto rounded-lg border border-primary-100">
              <table className="w-full text-left text-xs">
                <thead className="sticky top-0 bg-primary-50 text-ink-600">
                  <tr>
                    <th className="w-8 px-2 py-2"></th>
                    <th className="w-6 px-2 py-2"></th>
                    <th className="px-2 py-2">Q#</th>
                    <th className="px-2 py-2">Subject</th>
                    <th className="px-2 py-2">Question</th>
                    <th className="px-2 py-2">Issues</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.rows.map((row) => {
                    const isExpanded = expandedRows.has(row.rowNumber);
                    return (
                      <Fragment key={row.rowNumber}>
                        <tr
                          onClick={() => toggleExpanded(row.rowNumber)}
                          className={`cursor-pointer border-t border-primary-50 hover:bg-primary-50/60 ${row.isValid ? "" : "bg-red-50/40"}`}
                        >
                          <td className="px-2 py-2 align-top" onClick={(e) => e.stopPropagation()}>
                            <input
                              type="checkbox"
                              disabled={!row.isValid}
                              checked={checkedRows.has(row.rowNumber)}
                              onChange={() => toggleRow(row.rowNumber)}
                            />
                          </td>
                          <td className="px-2 py-2 align-top text-ink-300">
                            {isExpanded ? <ChevronDown className="h-3.5 w-3.5" strokeWidth={2.5} /> : <ChevronRight className="h-3.5 w-3.5" strokeWidth={2.5} />}
                          </td>
                          <td className="px-2 py-2 align-top font-semibold text-ink-900">{row.questionNumber || "—"}</td>
                          <td className="px-2 py-2 align-top text-ink-600">{row.subject || "—"}</td>
                          <td className="max-w-xs px-2 py-2 align-top text-ink-600">
                            <span className="line-clamp-2">{row.questionText || "—"}</span>
                          </td>
                          <td className="px-2 py-2 align-top">
                            {row.errors.length > 0 && (
                              <ul className="flex flex-col gap-0.5 text-red-600">
                                {row.errors.map((e, i) => (
                                  <li key={i} className="flex items-start gap-1">
                                    <AlertTriangle className="mt-0.5 h-3 w-3 shrink-0" strokeWidth={2.5} />
                                    {e}
                                  </li>
                                ))}
                              </ul>
                            )}
                          </td>
                        </tr>
                        {isExpanded && (
                          <tr className="border-t border-primary-50 bg-primary-50/20">
                            <td colSpan={6} className="px-2 py-2">
                              <PreviewRowEditor row={row} jobId={preview.jobId} token={token} onSaved={handleRowSaved} />
                            </td>
                          </tr>
                        )}
                      </Fragment>
                    );
                  })}
                </tbody>
              </table>
            </div>

            <Button className="mt-3" onClick={handleCommit} disabled={checkedRows.size === 0} isLoading={committing}>
              Import {checkedRows.size} question{checkedRows.size === 1 ? "" : "s"}
            </Button>
          </div>
        )}
      </Card>

      {history?.length > 0 && (
        <Card>
          <div className="mb-2 flex items-center gap-2">
            <History className="h-4 w-4 text-ink-400" strokeWidth={2.25} />
            <h3 className="text-sm font-bold text-ink-900">Recent imports for this paper</h3>
          </div>
          <div className="flex flex-col divide-y divide-primary-50">
            {history.map((job) => {
              const isJobExpandable = job.status === "Committed";
              const isJobExpanded = expandedJobId === job.id;
              const jobQ = jobQuestions[job.id];
              return (
                <div key={job.id} className="py-2">
                  <div
                    className={`flex items-center justify-between gap-3 text-xs ${isJobExpandable ? "cursor-pointer" : ""}`}
                    onClick={() => isJobExpandable && toggleJobExpanded(job)}
                  >
                    <span className="flex items-center gap-1.5 text-ink-600">
                      {isJobExpandable && (
                        isJobExpanded
                          ? <ChevronDown className="h-3.5 w-3.5 shrink-0 text-ink-300" strokeWidth={2.5} />
                          : <ChevronRight className="h-3.5 w-3.5 shrink-0 text-ink-300" strokeWidth={2.5} />
                      )}
                      <span>
                        <span className="font-semibold text-ink-900">{job.fileName}</span> · {job.status} ·{" "}
                        {job.importedCount}/{job.totalRows} imported · by {job.createdByAdminName}
                      </span>
                    </span>
                    {job.status === "Committed" && (
                      <Button
                        variant="secondary"
                        isLoading={rollingBackId === job.id}
                        onClick={(e) => { e.stopPropagation(); handleRollback(job.id); }}
                      >
                        <Undo2 className="h-3.5 w-3.5" strokeWidth={2.25} />
                        Roll back
                      </Button>
                    )}
                  </div>

                  {isJobExpanded && (
                    <div className="mt-2 flex flex-col gap-2 border-l-2 border-primary-100 pl-3">
                      {jobQ === "loading" && (
                        <span className="flex items-center gap-1.5 text-xs text-ink-400">
                          <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.5} />
                          Loading this batch's questions…
                        </span>
                      )}
                      {jobQ === "error" && <Alert>Couldn't load this batch's questions. Try again.</Alert>}
                      {Array.isArray(jobQ) && jobQ.length === 0 && (
                        <p className="text-xs text-ink-400">No questions found for this import (it may have since been rolled back).</p>
                      )}
                      {Array.isArray(jobQ) && jobQ.map((q) =>
                        editingJobQuestionId === q.id ? (
                          <QuestionEditForm
                            key={q.id}
                            question={q}
                            token={token}
                            onSaved={(updated) => {
                              setJobQuestions((prev) => ({
                                ...prev,
                                [job.id]: prev[job.id].map((x) => (x.id === updated.id ? updated : x)),
                              }));
                              setEditingJobQuestionId(null);
                            }}
                            onCancel={() => setEditingJobQuestionId(null)}
                          />
                        ) : (
                          <QuestionCard
                            key={q.id}
                            question={q}
                            canEdit
                            canDelete={false}
                            onEdit={() => setEditingJobQuestionId(q.id)}
                          />
                        )
                      )}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </Card>
      )}
    </div>
  );
}

// Editable form for one not-yet-committed preview row -- lets the admin fix a wrong option,
// correct answer, or explanation right here during review instead of having to correct the source
// file and re-upload (spec section 10). Saves via PATCH .../rows/{rowNumber}, which re-validates
// server-side, so the row that comes back reflects whether the edit actually fixed the problem.
function PreviewRowEditor({ row, jobId, token, onSaved }) {
  const [fields, setFields] = useState({
    questionNumber: row.questionNumber,
    subject: row.subject,
    topic: row.topic,
    difficultyLevel: row.difficultyLevel,
    questionText: row.questionText,
    optionA: row.optionA,
    optionB: row.optionB,
    optionC: row.optionC,
    optionD: row.optionD,
    correctOption: row.correctOption,
    explanation: row.explanation || "",
    sourceReference: row.sourceReference || "",
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  function updateField(key, value) {
    setFields((f) => ({ ...f, [key]: value }));
  }

  async function handleSave(e) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      const updated = await updatePreviewRow(token, jobId, row.rowNumber, fields);
      onSaved(updated);
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <form onSubmit={handleSave} className="flex flex-col gap-3 rounded-lg border border-primary-100 bg-white p-3">
      {row.errors?.length > 0 && (
        <ul className="flex flex-col gap-0.5 text-xs text-red-600">
          {row.errors.map((e, i) => (
            <li key={i} className="flex items-start gap-1">
              <AlertTriangle className="mt-0.5 h-3 w-3 shrink-0" strokeWidth={2.5} />
              {e}
            </li>
          ))}
        </ul>
      )}

      <div className="grid grid-cols-2 gap-3">
        <FormField label="Question No."><TextInput required type="number" value={fields.questionNumber} onChange={(e) => updateField("questionNumber", e.target.value)} /></FormField>
        <FormField label="Subject"><TextInput required value={fields.subject} onChange={(e) => updateField("subject", e.target.value)} /></FormField>
        <FormField label="Topic"><TextInput required value={fields.topic} onChange={(e) => updateField("topic", e.target.value)} /></FormField>
        <FormField label="Difficulty">
          <Select value={fields.difficultyLevel} onChange={(e) => updateField("difficultyLevel", e.target.value)}>
            {DIFFICULTIES.map((d) => <option key={d} value={d}>{d}</option>)}
          </Select>
        </FormField>
      </div>

      <FormField label="Question text">
        <TextArea required rows={2} value={fields.questionText} onChange={(e) => updateField("questionText", e.target.value)} />
      </FormField>

      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        {["A", "B", "C", "D"].map((letter) => (
          <FormField key={letter} label={`Option ${letter}`}>
            <div className="flex items-center gap-2">
              <input
                type="radio"
                name={`correct-${jobId}-${row.rowNumber}`}
                checked={fields.correctOption === letter}
                onChange={() => updateField("correctOption", letter)}
                className="h-4 w-4 accent-mint-500"
                title={`Mark ${letter} as the correct option`}
              />
              <TextInput required value={fields[`option${letter}`]} onChange={(e) => updateField(`option${letter}`, e.target.value)} />
            </div>
          </FormField>
        ))}
      </div>
      <p className="text-xs text-ink-400">Select the radio button next to the correct option.</p>

      <FormField label="Explanation (optional)">
        <TextArea rows={2} value={fields.explanation} onChange={(e) => updateField("explanation", e.target.value)} />
      </FormField>
      <FormField label="Source reference (optional)">
        <TextInput value={fields.sourceReference} onChange={(e) => updateField("sourceReference", e.target.value)} />
      </FormField>

      {error && <Alert>{error}</Alert>}

      <div>
        <Button type="submit" isLoading={saving}>Save changes</Button>
      </div>
    </form>
  );
}
