import { useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, UploadCloud, Download, CheckCircle2, XCircle, AlertTriangle } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { previewBulkPapers, commitBulkPapers } from "../api/bulkPapers";
import { PageHeader, Card, Button, Alert, friendlyError } from "../components/AdminUI";

const ACCEPTED = ".csv,.xlsx,.json";

// Bulk-creates paper SHELLS (Exam+Year+Medium+Tier+Shift+Date+Code+Label, Draft, zero questions)
// from a CSV/Excel file -- one row per paper, reached from the wizard's exam-picker screen ("Bulk
// Upload PYPs", next to "+ New Exam"). Deliberately its own page rather than a step bolted onto
// PyqUploadWizard: that wizard is scoped to one exam+language at a time, but a single bulk-papers
// file can span many different exams/mediums in one go (SSC CGL 2023 Hindi, SSC CGL 2024 Hindi,
// RRB NTPC 2023 English, ... all in the same upload). Every created paper still only shows up
// admin-side (Draft) -- the admin opens each one afterward from "All Papers" to fill in
// Duration/NegativeMarking/RequiredQuestionCount and add its actual questions, then submits and
// publishes it like any other paper.
export default function BulkPaperUpload() {
  const navigate = useNavigate();
  const { token } = useAdminAuth();
  const fileInputRef = useRef(null);

  const [selectedFile, setSelectedFile] = useState(null);
  const [previewing, setPreviewing] = useState(false);
  const [preview, setPreview] = useState(null); // { jobId, fileName, totalRows, validCount, invalidCount, alreadyExistsCount, rows }
  const [checkedRows, setCheckedRows] = useState(new Set());
  const [error, setError] = useState(null);

  const [committing, setCommitting] = useState(false);
  const [commitResult, setCommitResult] = useState(null); // { createdCount, skippedExistingCount, createdPapers }

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
      const res = await previewBulkPapers(token, selectedFile);
      setPreview(res);
      // Pre-check every row that will actually create something -- valid and not already existing.
      // An already-existing row stays visible (so the admin can see it'll be skipped) but unchecked
      // by default; a row can't be forced through if it's actually invalid, checked or not.
      setCheckedRows(new Set(res.rows.filter((r) => r.isValid && !r.paperAlreadyExists).map((r) => r.rowNumber)));
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
      const result = await commitBulkPapers(token, preview.jobId, Array.from(checkedRows));
      setCommitResult(result);
      setPreview(null);
      setSelectedFile(null);
      if (fileInputRef.current) fileInputRef.current.value = "";
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setCommitting(false);
    }
  }

  function handleReset() {
    setSelectedFile(null);
    setPreview(null);
    setCommitResult(null);
    setError(null);
    if (fileInputRef.current) fileInputRef.current.value = "";
  }

  return (
    <div>
      <PageHeader
        title="Bulk Upload PYPs"
        subtitle="Create many paper shells at once from a CSV/Excel file. Each one lands as a Draft, admin-side only -- add questions, set duration/negative marking, and publish each one separately when it's ready."
        action={
          <Button variant="ghost" onClick={() => navigate("/admin/upload")}>
            <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
            Back to Add Paper
          </Button>
        }
      />

      <div className="p-6">
        {error && <div className="mb-4"><Alert>{error}</Alert></div>}

        {commitResult ? (
          <Card>
            <div className="flex items-center gap-2">
              <CheckCircle2 className="h-5 w-5 text-mint-500" strokeWidth={2.25} />
              <h3 className="text-sm font-bold text-ink-900">
                {commitResult.createdCount} paper{commitResult.createdCount === 1 ? "" : "s"} created
                {commitResult.skippedExistingCount > 0 && ` (${commitResult.skippedExistingCount} already existed and were skipped)`}
              </h3>
            </div>

            {commitResult.createdPapers.length > 0 && (
              <div className="mt-4 flex flex-col divide-y divide-primary-50">
                {commitResult.createdPapers.map((p) => (
                  <button
                    key={p.id}
                    type="button"
                    onClick={() => navigate(`/admin/papers/${p.id}`)}
                    className="flex items-center justify-between py-2.5 text-left hover:bg-primary-50/50"
                  >
                    <span className="text-sm font-semibold text-ink-900">
                      {p.examName} · {p.year}{p.tier ? ` · ${p.tier}` : ""}{p.shift ? ` · ${p.shift}` : ""}
                    </span>
                    <span className="text-xs text-ink-400">{p.language}{p.examDate ? ` · ${p.examDate}` : ""} · open →</span>
                  </button>
                ))}
              </div>
            )}

            <div className="mt-4 flex gap-2">
              <Button onClick={() => navigate("/admin/papers?status=Draft")}>Go to All Papers</Button>
              <Button variant="ghost" onClick={handleReset}>Upload another file</Button>
            </div>
          </Card>
        ) : (
          <>
            <Card className="mb-6">
              <div className="flex items-center gap-2">
                <UploadCloud className="h-4 w-4 text-secondary-500" strokeWidth={2.25} />
                <h3 className="text-sm font-bold text-ink-900">Upload file</h3>
              </div>
              <p className="mt-1 text-xs text-ink-400">
                Expected columns: ExamName, Year, Medium (required); Tier, Shift, Date, PaperCode,
                PaperLabel (all optional). Date must be YYYY-MM-DD. Medium is Hindi or English. An
                ExamName that doesn't already exist creates a new exam, exactly like "+ New Exam" in
                the wizard. CSV, Excel (.xlsx), and JSON (an array of the same fields) are all
                supported -- pick whichever's easiest to prepare.
              </p>

              <div className="mt-3 flex flex-wrap items-center gap-3">
                <label className="flex cursor-pointer items-center gap-2.5 rounded-xl2 border border-dashed border-primary-100 bg-white px-3.5 py-2.5 text-sm text-ink-600 hover:border-secondary-500">
                  <UploadCloud className="h-4 w-4 text-ink-400" strokeWidth={2} />
                  {selectedFile ? selectedFile.name : "Choose a CSV, Excel, or JSON file"}
                  <input ref={fileInputRef} type="file" accept={ACCEPTED} className="hidden" onChange={handleFileChange} />
                </label>

                <Button onClick={handlePreview} disabled={!selectedFile} isLoading={previewing}>
                  Preview
                </Button>
              </div>

              <div className="mt-3 flex flex-wrap items-center gap-4">
                <span className="text-xs font-semibold text-ink-400">Sample templates:</span>
                {[
                  { href: "/samples/sample-bulk-papers.csv", label: "CSV" },
                  { href: "/samples/sample-bulk-papers.xlsx", label: "Excel" },
                  { href: "/samples/sample-bulk-papers.json", label: "JSON" },
                ].map((f) => (
                  <a
                    key={f.label}
                    href={f.href}
                    download
                    className="flex items-center gap-1.5 text-xs font-semibold text-secondary-600 hover:underline"
                  >
                    <Download className="h-3.5 w-3.5" strokeWidth={2.25} />
                    {f.label}
                  </a>
                ))}
              </div>
            </Card>

            {preview && (
              <Card>
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <h3 className="text-sm font-bold text-ink-900">{preview.fileName}</h3>
                  <div className="flex gap-3 text-xs font-semibold">
                    <span className="text-mint-600">{preview.validCount} will be created</span>
                    {preview.alreadyExistsCount > 0 && <span className="text-accent-600">{preview.alreadyExistsCount} already exist</span>}
                    {preview.invalidCount > 0 && <span className="text-red-600">{preview.invalidCount} invalid</span>}
                  </div>
                </div>

                <div className="mt-4 overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead>
                      <tr className="border-b border-primary-100 text-xs font-semibold text-ink-400">
                        <th className="w-8 py-2"></th>
                        <th className="py-2 pr-3">Exam</th>
                        <th className="py-2 pr-3">Year</th>
                        <th className="py-2 pr-3">Tier</th>
                        <th className="py-2 pr-3">Shift</th>
                        <th className="py-2 pr-3">Date</th>
                        <th className="py-2 pr-3">Medium</th>
                        <th className="py-2">Status</th>
                      </tr>
                    </thead>
                    <tbody>
                      {preview.rows.map((row) => (
                        <tr key={row.rowNumber} className="border-b border-primary-50 align-top">
                          <td className="py-2">
                            {row.isValid && !row.paperAlreadyExists && (
                              <input
                                type="checkbox"
                                checked={checkedRows.has(row.rowNumber)}
                                onChange={() => toggleRow(row.rowNumber)}
                              />
                            )}
                          </td>
                          <td className="py-2 pr-3 font-semibold text-ink-900">
                            {row.examName || <span className="text-ink-300">—</span>}
                            {row.isValid && !row.examExists && (
                              <span className="ml-1.5 rounded-full bg-secondary-50 px-1.5 py-0.5 text-[10px] font-bold text-secondary-600">NEW</span>
                            )}
                          </td>
                          <td className="py-2 pr-3 text-ink-600">{row.year || "—"}</td>
                          <td className="py-2 pr-3 text-ink-600">{row.tier || "—"}</td>
                          <td className="py-2 pr-3 text-ink-600">{row.shift || "—"}</td>
                          <td className="py-2 pr-3 text-ink-600">{row.examDate || "—"}</td>
                          <td className="py-2 pr-3 text-ink-600">{row.medium || "—"}</td>
                          <td className="py-2">
                            {!row.isValid ? (
                              <span className="flex items-start gap-1 text-xs font-medium text-red-600">
                                <XCircle className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" strokeWidth={2.25} />
                                {row.errors.join(" ")}
                              </span>
                            ) : row.paperAlreadyExists ? (
                              <span className="flex items-center gap-1 text-xs font-medium text-accent-600">
                                <AlertTriangle className="h-3.5 w-3.5" strokeWidth={2.25} />
                                Already exists -- will be skipped
                              </span>
                            ) : (
                              <span className="flex items-center gap-1 text-xs font-medium text-mint-600">
                                <CheckCircle2 className="h-3.5 w-3.5" strokeWidth={2.25} />
                                Ready
                              </span>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>

                <Button className="mt-4" onClick={handleCommit} disabled={checkedRows.size === 0} isLoading={committing}>
                  Create {checkedRows.size} paper{checkedRows.size === 1 ? "" : "s"}
                </Button>
              </Card>
            )}
          </>
        )}
      </div>
    </div>
  );
}
