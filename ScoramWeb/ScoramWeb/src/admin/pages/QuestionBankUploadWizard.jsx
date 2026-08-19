import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  ArrowLeft, UploadCloud, Download, CheckCircle2, XCircle, Copy, AlertTriangle, History,
} from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  previewQuestionBankImport, commitQuestionBankImport, getQuestionBankImportHistory,
  downloadExcelTemplate, downloadJsonTemplate,
} from "../api/questionBankImport";
import { PageHeader, Card, Button, Alert, friendlyError } from "../components/AdminUI";

// Spec sections 9-13: Excel or JSON bulk upload, with a mandatory preview → validate → confirm step
// before anything touches the database (section 10: "DO NOT directly insert an unvalidated Excel
// file"). Rows flagged IsDuplicate stay selectable -- committing them merges their exam/year pairs
// into the existing question instead of creating a second copy (see QuestionBankAdminController.Commit).
export default function QuestionBankUploadWizard() {
  const { token } = useAdminAuth();
  const navigate = useNavigate();
  const fileInputRef = useRef(null);

  const [format, setFormat] = useState("excel"); // "excel" | "json"
  const [selectedFile, setSelectedFile] = useState(null);
  const [previewing, setPreviewing] = useState(false);
  const [preview, setPreview] = useState(null);
  const [checkedRows, setCheckedRows] = useState(new Set());
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
      const res = await previewQuestionBankImport(token, selectedFile, format);
      setPreview(res);
      // Valid rows are pre-checked (including duplicates -- they'll be merged, not duplicated);
      // invalid rows are never selectable in the first place.
      setCheckedRows(new Set(res.rows.filter((r) => r.isValid).map((r) => r.rowNumber)));
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
        subtitle="Excel (.xlsx) or JSON — validated and previewed before anything is saved"
        action={
          <Button variant="ghost" onClick={() => navigate("/admin/question-bank")}>
            <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
            Back to Question Bank
          </Button>
        }
      />

      <div className="flex flex-col gap-4 p-6">
        <Card>
          <p className="text-xs text-ink-400">
            Expected columns/fields: QuestionText, OptionA–D, CorrectOption, Explanation (optional),
            Subject, Topic, SourceReference (optional), ExamYears — e.g.{" "}
            <code className="rounded bg-primary-50 px-1 py-0.5">"SSC CGL:2018; UP TGT:2022"</code>.
            A question that already exists (by normalized text) won't be duplicated — its new
            exam/year pairs are merged into the existing question instead.
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
                {["excel", "json"].map((f) => (
                  <button
                    key={f}
                    type="button"
                    onClick={() => { setFormat(f); handleStartOver(); }}
                    className={`rounded-xl2 px-3.5 py-2 text-xs font-semibold transition-colors ${format === f ? "bg-primary-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"}`}
                  >
                    {f === "excel" ? ".xlsx" : ".json"}
                  </button>
                ))}
              </div>

              <div className="mt-3 flex flex-wrap items-center gap-3">
                <input
                  ref={fileInputRef}
                  type="file"
                  accept={format === "excel" ? ".xlsx" : ".json"}
                  onChange={handleFileChange}
                  className="text-sm"
                />
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
                <Button variant="secondary" onClick={() => navigate("/admin/question-bank")}>Go to Question Bank</Button>
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
              </div>

              <div className="mt-3 max-h-[28rem] overflow-y-auto rounded-lg border border-primary-100">
                <table className="w-full text-left text-xs">
                  <thead className="sticky top-0 bg-primary-50 text-ink-600">
                    <tr>
                      <th className="w-8 px-2 py-2"></th>
                      <th className="px-2 py-2">Row</th>
                      <th className="px-2 py-2">Question</th>
                      <th className="px-2 py-2">Subject / Topic</th>
                      <th className="px-2 py-2">Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.rows.map((row) => (
                      <tr key={row.rowNumber} className={`border-t border-primary-50 ${!row.isValid ? "bg-red-50/40" : row.isDuplicate ? "bg-accent-50/40" : ""}`}>
                        <td className="px-2 py-2 align-top">
                          <input
                            type="checkbox"
                            disabled={!row.isValid}
                            checked={checkedRows.has(row.rowNumber)}
                            onChange={() => toggleRow(row.rowNumber)}
                          />
                        </td>
                        <td className="px-2 py-2 align-top font-semibold text-ink-900">{row.rowNumber}</td>
                        <td className="max-w-xs px-2 py-2 align-top text-ink-600">
                          <span className="line-clamp-2">{row.questionText || "—"}</span>
                        </td>
                        <td className="px-2 py-2 align-top text-ink-600">{row.subject} / {row.topic}</td>
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
                    ))}
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
