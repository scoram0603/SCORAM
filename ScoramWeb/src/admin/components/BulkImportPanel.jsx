import { Fragment, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { UploadCloud, CheckCircle2, XCircle, Loader2, AlertTriangle, History, Undo2, Download, SquarePen, ChevronDown, ChevronRight } from "lucide-react";
import { previewBulkImport, commitBulkImport, getImportHistory, rollbackImport } from "../api/bulkImport";
import { Card, Button, Alert, friendlyError, ImportRowOptionsDetail } from "./AdminUI";

const ACCEPTED = ".csv,.xlsx,.json";

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

  function refreshHistory() {
    getImportHistory(token, { paperId, page: 1, pageSize: 5 })
      .then((res) => setHistory(res.items))
      .catch(() => {});
  }

  useEffect(refreshHistory, [paperId, token]);

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
              Spotted a wrong option, or need to add an explanation or image to one of these? You can
              fix any question here while the paper's still a Draft.
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
              <span className="text-ink-300">· click a row to review its full options &amp; explanation</span>
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
                              <ImportRowOptionsDetail row={row} />
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
            {history.map((job) => (
              <div key={job.id} className="flex items-center justify-between gap-3 py-2 text-xs">
                <span className="text-ink-600">
                  <span className="font-semibold text-ink-900">{job.fileName}</span> · {job.status} ·{" "}
                  {job.importedCount}/{job.totalRows} imported · by {job.createdByAdminName}
                </span>
                {job.status === "Committed" && (
                  <Button
                    variant="secondary"
                    isLoading={rollingBackId === job.id}
                    onClick={() => handleRollback(job.id)}
                  >
                    <Undo2 className="h-3.5 w-3.5" strokeWidth={2.25} />
                    Roll back
                  </Button>
                )}
              </div>
            ))}
          </div>
        </Card>
      )}
    </div>
  );
}
