import { Fragment, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  ArrowLeft, UploadCloud, Download, CheckCircle2, XCircle, Copy, AlertTriangle, History, ChevronDown, ChevronRight,
} from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  previewQuestionBankImport, commitQuestionBankImport, getQuestionBankImportHistory, updatePreviewRow,
  downloadExcelTemplate, downloadJsonTemplate,
} from "../api/questionBankImport";
import { PageHeader, Card, Button, FormField, TextInput, TextArea, Select, Alert, friendlyError } from "../components/AdminUI";
import { MathText } from "../../components/questions/MathText";
import { RowBadges, StagedContentPreview } from "../components/BulkImportRowPreview";

const FORMATS = [
  { key: "csv", label: ".csv", accept: ".csv" },
  { key: "excel", label: ".xlsx", accept: ".xlsx" },
  { key: "json", label: ".json", accept: ".json" },
  { key: "zip", label: ".zip", accept: ".zip" },
];

// Spec sections 9-13, 18-20, 44: CSV/Excel/JSON/ZIP bulk upload, with a mandatory preview →
// validate → confirm step before anything touches the database (section 10: "DO NOT directly
// insert an unvalidated Excel file"). Rows flagged IsDuplicate stay selectable -- committing them
// merges their exam/year pairs into the existing question instead of creating a second copy (see
// QuestionBankAdminController.Commit). A .zip additionally supports per-question images and a
// ContentBlocks sequence (math/text/image/table) -- see BulkUploadZipService on the backend.
export default function QuestionBankUploadWizard() {
  const { token } = useAdminAuth();
  const navigate = useNavigate();
  const fileInputRef = useRef(null);

  const [format, setFormat] = useState("excel"); // "csv" | "excel" | "json" | "zip"
  const [selectedFile, setSelectedFile] = useState(null);
  const [defaultLanguage, setDefaultLanguage] = useState(""); // "" | "Hindi" | "English"
  const [previewing, setPreviewing] = useState(false);
  const [preview, setPreview] = useState(null);
  const [checkedRows, setCheckedRows] = useState(new Set());
  // Rows the admin has expanded to review full options/explanation before committing (section 10 --
  // preview must let the admin actually verify content, not just row-level metadata).
  const [expandedRows, setExpandedRows] = useState(new Set());
  const [error, setError] = useState(null);

  const [committing, setCommitting] = useState(false);
  const [commitResult, setCommitResult] = useState(null);

  const [history, setHistory] = useState(null);

  useEffect(() => {
    refreshHistory();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  function refreshHistory() {
    getQuestionBankImportHistory(token, { page: 1, pageSize: 5 }).then((res) => setHistory(res.items)).catch(() => {});
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
      const res = await previewQuestionBankImport(token, selectedFile, format, defaultLanguage || undefined);
      setPreview(res);
      // Valid rows are pre-checked (including duplicates -- they'll be merged, not duplicated);
      // invalid rows are never selectable in the first place.
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
  // re-validated) row into the preview, keeps checkedRows in sync (a just-fixed row gets
  // auto-checked; one that somehow became invalid gets unchecked), and collapses it back down.
  function handleRowSaved(updatedRow) {
    setPreview((prev) => {
      if (!prev) return prev;
      const rows = prev.rows.map((r) => (r.rowNumber === updatedRow.rowNumber ? updatedRow : r));
      return {
        ...prev,
        rows,
        validCount: rows.filter((r) => r.isValid).length,
        invalidCount: rows.filter((r) => !r.isValid).length,
        duplicateCount: rows.filter((r) => r.isDuplicate).length,
      };
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
      const result = await commitQuestionBankImport(token, preview.jobId, Array.from(checkedRows));
      setCommitResult(result);
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

  async function handleDownloadTemplate(kind) {
    try {
      if (kind === "excel") await downloadExcelTemplate(token);
      else await downloadJsonTemplate(token);
    } catch (err) {
      window.alert(friendlyError(err));
    }
  }

  return (
    <div>
      <PageHeader
        title="Bulk Upload Questions"
        subtitle="CSV, Excel (.xlsx), JSON, or ZIP (with images) — validated and previewed before anything is saved"
        action={
          <Button variant="ghost" onClick={() => navigate("/admin/question-bank")}>
            <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
            Back to PYQs
          </Button>
        }
      />

      <div className="flex flex-col gap-4 p-6">
        <Card>
          <p className="text-xs text-ink-400">
            Expected columns/fields: QuestionText, OptionA–D, CorrectOption, Explanation (optional),
            Subject, Topic, SourceReference (optional), Language (optional), ExamYears — e.g.{" "}
            <code className="rounded bg-primary-50 px-1 py-0.5">"SSC CGL:2018; UP TGT:2022"</code>.
            A question that already exists (by normalized text) won't be duplicated — its new
            exam/year pairs are merged into the existing question instead. Math renders from
            $inline$ or $$display$$ LaTeX in any text field.
          </p>
          <p className="mt-1.5 text-xs text-ink-400">
            <span className="font-semibold text-ink-600">Medium (Hindi/English):</span> either fill
            the Language column per-row in your file, or pick a Default Language below and leave
            that column blank — it'll apply to every row that doesn't specify its own.
          </p>
          <p className="mt-1.5 text-xs text-ink-400">
            <span className="font-semibold text-ink-600">Images:</span> not supported in CSV/Excel/JSON
            (add them afterward from the question list) — upload a .zip instead, containing{" "}
            <code className="rounded bg-primary-50 px-1">questions.json</code> (same columns, plus
            optional <code className="rounded bg-primary-50 px-1">questionImage</code>,{" "}
            <code className="rounded bg-primary-50 px-1">optionAImage</code>…
            <code className="rounded bg-primary-50 px-1">explanationImage</code> filename fields) and
            an <code className="rounded bg-primary-50 px-1">images/</code> folder with the referenced
            files.
          </p>

          <div className="mt-2 flex flex-wrap items-center gap-3 text-xs">
            <span className="text-ink-400">Not sure of the format? Download the template:</span>
            <button type="button" onClick={() => handleDownloadTemplate("excel")} className="flex items-center gap-1 font-semibold text-secondary-500 hover:underline">
              <Download className="h-3.5 w-3.5" strokeWidth={2.25} />
              Excel
            </button>
            <button type="button" onClick={() => handleDownloadTemplate("json")} className="flex items-center gap-1 font-semibold text-secondary-500 hover:underline">
              <Download className="h-3.5 w-3.5" strokeWidth={2.25} />
              JSON
            </button>
          </div>

          {!commitResult && (
            <>
              <div className="mt-3 flex gap-2">
                {FORMATS.map((f) => (
                  <button
                    key={f.key}
                    type="button"
                    onClick={() => { setFormat(f.key); handleStartOver(); }}
                    className={`rounded-xl2 px-3.5 py-2 text-xs font-semibold transition-colors ${format === f.key ? "bg-primary-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"}`}
                  >
                    {f.label}
                  </button>
                ))}
              </div>

              <div className="mt-3 flex flex-wrap items-center gap-3">
                <input
                  ref={fileInputRef}
                  type="file"
                  accept={FORMATS.find((f) => f.key === format)?.accept}
                  onChange={handleFileChange}
                  className="text-sm"
                />
                <label className="flex items-center gap-1.5 text-xs">
                  <span className="font-semibold text-ink-600">Default Language:</span>
                  <select
                    value={defaultLanguage}
                    onChange={(e) => setDefaultLanguage(e.target.value)}
                    className="rounded-lg border border-primary-100 bg-white px-2 py-1.5 text-xs text-ink-900 focus:border-secondary-500 focus:outline-none"
                  >
                    <option value="">Not set (use file's own values)</option>
                    <option value="Hindi">Hindi</option>
                    <option value="English">English</option>
                  </select>
                </label>
                <Button onClick={handlePreview} disabled={!selectedFile} isLoading={previewing}>
                  <UploadCloud className="h-4 w-4" strokeWidth={2.25} />
                  Preview
                </Button>
                {preview && (
                  <Button variant="secondary" onClick={handleStartOver}>Choose a different file</Button>
                )}
              </div>
            </>
          )}

          {error && <div className="mt-3"><Alert>{error}</Alert></div>}

          {commitResult && (
            <div className="mt-3">
              <Alert type="success">
                Imported {commitResult.importedCount} new question{commitResult.importedCount === 1 ? "" : "s"}
                {commitResult.mergedIntoExistingCount > 0 ? `, merged ${commitResult.mergedIntoExistingCount} duplicate row(s) into existing questions` : ""}
                {commitResult.skippedCount > 0 ? ` (${commitResult.skippedCount} row(s) not selected/skipped)` : ""}.
              </Alert>
              <div className="mt-3 flex flex-wrap gap-2">
                <Button variant="secondary" onClick={() => navigate("/admin/question-bank")}>Go to PYQs</Button>
                <Button variant="secondary" onClick={handleStartOver}>Import another file</Button>
              </div>
            </div>
          )}

          {preview && !commitResult && (
            <div className="mt-4">
              <div className="flex flex-wrap items-center gap-3 text-xs">
                <span>Total: {preview.totalRows}</span>
                <span className="flex items-center gap-1 font-semibold text-mint-500">
                  <CheckCircle2 className="h-3.5 w-3.5" strokeWidth={2.5} />
                  {preview.validCount} valid
                </span>
                {preview.invalidCount > 0 && (
                  <span className="flex items-center gap-1 font-semibold text-red-600">
                    <XCircle className="h-3.5 w-3.5" strokeWidth={2.5} />
                    {preview.invalidCount} invalid
                  </span>
                )}
                {preview.duplicateCount > 0 && (
                  <span className="flex items-center gap-1 font-semibold text-accent-600">
                    <Copy className="h-3.5 w-3.5" strokeWidth={2.5} />
                    {preview.duplicateCount} duplicate (will merge exam/year only)
                  </span>
                )}
                <span className="text-ink-400">{checkedRows.size} selected to import</span>
                <span className="text-ink-300">· click a row to review &amp; edit it before importing</span>
              </div>

              <div className="mt-3 max-h-[28rem] overflow-y-auto rounded-lg border border-primary-100">
                <table className="w-full text-left text-xs">
                  <thead className="sticky top-0 bg-primary-50 text-ink-600">
                    <tr>
                      <th className="w-8 px-2 py-2"></th>
                      <th className="w-6 px-2 py-2"></th>
                      <th className="px-2 py-2">Row</th>
                      <th className="px-2 py-2">Question</th>
                      <th className="px-2 py-2">Subject / Topic</th>
                      <th className="px-2 py-2">Medium</th>
                      <th className="px-2 py-2">Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.rows.map((row) => {
                      const isExpanded = expandedRows.has(row.rowNumber);
                      return (
                        <Fragment key={row.rowNumber}>
                          <tr
                            onClick={() => toggleExpanded(row.rowNumber)}
                            className={`cursor-pointer border-t border-primary-50 hover:bg-primary-50/60 ${!row.isValid ? "bg-red-50/40" : row.isDuplicate ? "bg-accent-50/40" : ""}`}
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
                            <td className="px-2 py-2 align-top font-semibold text-ink-900">{row.rowNumber}</td>
                            <td className="max-w-xs px-2 py-2 align-top text-ink-600">
                              <span className="line-clamp-2">{row.questionText || "—"}</span>
                              <RowBadges row={row} />
                            </td>
                            <td className="px-2 py-2 align-top text-ink-600">{row.subject} / {row.topic}</td>
                            <td className="px-2 py-2 align-top text-ink-600">{row.language || <span className="text-ink-300">—</span>}</td>
                            <td className="px-2 py-2 align-top">
                              {!row.isValid && (
                                <ul className="flex flex-col gap-0.5 text-red-600">
                                  {row.errors.map((e, i) => (
                                    <li key={i} className="flex items-start gap-1">
                                      <AlertTriangle className="mt-0.5 h-3 w-3 shrink-0" strokeWidth={2.5} />
                                      {e}
                                    </li>
                                  ))}
                                </ul>
                              )}
                              {row.isValid && row.isDuplicate && (
                                <span className="text-accent-600">
                                  Duplicate of: {row.duplicateOfQuestionTextSnippet}
                                </span>
                              )}
                              {row.isValid && !row.isDuplicate && <span className="text-mint-500">New</span>}
                            </td>
                          </tr>
                          {isExpanded && (
                            <tr className="border-t border-primary-50 bg-primary-50/20">
                              <td colSpan={7} className="px-2 py-2">
                                <QuestionBankRowEditor row={row} jobId={preview.jobId} token={token} onSaved={handleRowSaved} />
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
                Import {checkedRows.size} row{checkedRows.size === 1 ? "" : "s"}
              </Button>
            </div>
          )}
        </Card>

        {history?.length > 0 && (
          <Card>
            <div className="mb-2 flex items-center gap-2">
              <History className="h-4 w-4 text-ink-400" strokeWidth={2.25} />
              <h3 className="text-sm font-bold text-ink-900">Recent imports</h3>
            </div>
            <div className="flex flex-col divide-y divide-primary-50">
              {history.map((job) => (
                <div key={job.id} className="flex items-center justify-between gap-3 py-2 text-xs">
                  <span className="text-ink-600">
                    <span className="font-semibold text-ink-900">{job.fileName}</span> · {job.status} ·{" "}
                    {job.importedCount} new, {job.mergedIntoExistingCount} merged / {job.totalRows} rows · by {job.createdByAdminName}
                  </span>
                </div>
              ))}
            </div>
          </Card>
        )}
      </div>
    </div>
  );
}

// Editable form for one not-yet-committed preview row -- lets the admin fix a wrong option,
// correct answer, explanation, subject/topic, or exam/year tags right here during review instead of
// correcting the source file and re-uploading. Saves via PATCH .../rows/{rowNumber}, which
// re-validates server-side (including duplicate detection), so the row that comes back reflects
// whether the edit actually fixed the problem.
function QuestionBankRowEditor({ row, jobId, token, onSaved }) {
  const [fields, setFields] = useState({
    questionText: row.questionText,
    optionA: row.optionA,
    optionB: row.optionB,
    optionC: row.optionC,
    optionD: row.optionD,
    correctOption: row.correctOption,
    explanation: row.explanation || "",
    subject: row.subject,
    topic: row.topic,
    sourceReference: row.sourceReference || "",
    language: row.language || "",
    rawExamYears: row.rawExamYears || "",
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
      {row.isDuplicate && (
        <p className="text-xs text-accent-600">Duplicate of: {row.duplicateOfQuestionTextSnippet}</p>
      )}

      {/* Staged images and any ContentBlocks came from the ZIP's images/ folder and questions.json
          at parse time -- shown here read-only since the preview-row edit API only covers the plain
          text fields below; to change an image, fix the ZIP and re-upload. */}
      <StagedContentPreview row={row} />

      <div className="grid grid-cols-2 gap-3">
        <FormField label="Subject"><TextInput required value={fields.subject} onChange={(e) => updateField("subject", e.target.value)} /></FormField>
        <FormField label="Topic"><TextInput required value={fields.topic} onChange={(e) => updateField("topic", e.target.value)} /></FormField>
      </div>

      <FormField label="Question text">
        <TextArea required rows={2} value={fields.questionText} onChange={(e) => updateField("questionText", e.target.value)} />
      </FormField>
      {fields.questionText?.includes("$") && (
        <p className="-mt-2 rounded-lg bg-primary-50/50 px-3 py-2 text-sm text-ink-700"><MathText text={fields.questionText} /></p>
      )}

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
      {fields.explanation?.includes("$") && (
        <p className="-mt-2 rounded-lg bg-primary-50/50 px-3 py-2 text-sm text-ink-700"><MathText text={fields.explanation} /></p>
      )}

      <div className="grid grid-cols-2 gap-3">
        <FormField label="Source reference (optional)">
          <TextInput value={fields.sourceReference} onChange={(e) => updateField("sourceReference", e.target.value)} />
        </FormField>
        <FormField label="Medium">
          <Select value={fields.language} onChange={(e) => updateField("language", e.target.value)}>
            <option value="">Not set</option>
            <option value="Hindi">Hindi</option>
            <option value="English">English</option>
          </Select>
        </FormField>
      </div>

      <FormField label={<>Exam:Year pairs <span className="font-normal text-ink-400">— e.g. "SSC CGL:2018; UP TGT:2022"</span></>}>
        <TextInput value={fields.rawExamYears} onChange={(e) => updateField("rawExamYears", e.target.value)} />
      </FormField>

      {error && <Alert>{error}</Alert>}

      <div>
        <Button type="submit" isLoading={saving}>Save changes</Button>
      </div>
    </form>
  );
}
