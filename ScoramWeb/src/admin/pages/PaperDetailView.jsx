import { useEffect, useState } from "react";
import { ArrowLeft, Pencil, Trash2, PlusCircle, ImagePlus, X, CheckCircle2, XCircle, LockOpen, Link2Off, Library } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  getPaper, publishPaper, rejectPaper, unpublishPaper,
  getMappedQuestions, updatePaperConfig, mapQuestionToPaper, mapQuestionsBulkToPaper, unmapQuestionFromPaper, validatePaper,
} from "../api/papers";
import { updateQuestion, deleteQuestion } from "../api/adminQuestions";
import { API_BASE_URL } from "../../api/client";
import { PageHeader, Card, Button, FormField, TextInput, TextArea, Select, Alert, StatusBadge, friendlyError } from "../components/AdminUI";
import TestQuestionPicker from "../components/TestQuestionPicker";
import PaperQuestionBulkPicker from "../components/PaperQuestionBulkPicker";
import { PracticeSettingsCard, ValidationSummary } from "../components/PaperConfigAndValidation";

const DIFFICULTIES = ["Easy", "Medium", "Hard"];

function imgSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

export default function PaperDetailView() {
  const { paperId } = useParams();
  const navigate = useNavigate();
  const { token, hasPermission } = useAdminAuth();
  const [paper, setPaper] = useState(null);
  const [questions, setQuestions] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const [editingId, setEditingId] = useState(null);
  const [deletingId, setDeletingId] = useState(null);
  const [actionLoading, setActionLoading] = useState(false);
  const [actionError, setActionError] = useState(null);
  const [showRejectForm, setShowRejectForm] = useState(false);
  const [rejectReason, setRejectReason] = useState("");

  // ---------- Previous Year Paper Practice ----------
  const [mappedQuestions, setMappedQuestions] = useState(null); // full merged list, tagged by source
  const [validation, setValidation] = useState(null);
  const [showQuestionPicker, setShowQuestionPicker] = useState(false);
  const [showBulkPicker, setShowBulkPicker] = useState(false);
  const [mappingError, setMappingError] = useState(null);

  useEffect(() => {
    refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [paperId]);

  function refresh() {
    setLoading(true);
    setError(null);
    Promise.all([getPaper(token, paperId), getMappedQuestions(token, paperId), validatePaper(token, paperId)])
      .then(([{ paper, questions }, mapped, validationResult]) => {
        setPaper(paper);
        setQuestions(questions);
        setMappedQuestions(mapped);
        setValidation(validationResult);
      })
      .catch((err) => setError(err.message))
      .finally(() => setLoading(false));
  }

  function refreshMappedQuestionsAndValidation() {
    Promise.all([getMappedQuestions(token, paperId), validatePaper(token, paperId), getPaper(token, paperId)])
      .then(([mapped, validationResult, { paper: refreshedPaper }]) => {
        setMappedQuestions(mapped);
        setValidation(validationResult);
        setPaper(refreshedPaper);
      })
      .catch(() => {});
  }

  async function handleSaveConfig({ durationMinutes, negativeMarkingRatio, requiredQuestionCount }) {
    setActionLoading(true);
    setActionError(null);
    try {
      const updated = await updatePaperConfig(token, paperId, { durationMinutes, negativeMarkingRatio, requiredQuestionCount });
      setPaper(updated);
      refreshMappedQuestionsAndValidation();
    } catch (err) {
      setActionError(friendlyError(err));
    } finally {
      setActionLoading(false);
    }
  }

  async function handleMapQuestion({ questionBankQuestionId }) {
    setMappingError(null);
    // Auto-assign the next free question number -- simplest "no confusion" default; admins can
    // still fix numbering by unmapping and re-adding at a specific point if a paper's Q.Nos aren't
    // simply 1..N in upload order.
    const usedNumbers = (mappedQuestions || []).map((m) => m.questionNumber);
    const nextNumber = usedNumbers.length > 0 ? Math.max(...usedNumbers) + 1 : 1;
    try {
      await mapQuestionToPaper(token, paperId, { questionBankQuestionId, questionNumber: nextNumber });
      refreshMappedQuestionsAndValidation();
    } catch (err) {
      setMappingError(friendlyError(err));
    }
  }

  async function handleUnmapQuestion(linkId) {
    setMappingError(null);
    try {
      await unmapQuestionFromPaper(token, paperId, linkId);
      refreshMappedQuestionsAndValidation();
    } catch (err) {
      setMappingError(friendlyError(err));
    }
  }

  async function handleBulkAdd(questionBankQuestionIds) {
    const result = await mapQuestionsBulkToPaper(token, paperId, questionBankQuestionIds);
    refreshMappedQuestionsAndValidation();
    return result;
  }

  async function handlePublish() {
    setActionLoading(true);
    setActionError(null);
    try {
      const updated = await publishPaper(token, paperId);
      setPaper(updated);
    } catch (err) {
      setActionError(friendlyError(err));
    } finally {
      setActionLoading(false);
    }
  }

  async function handleReject() {
    setActionLoading(true);
    setActionError(null);
    try {
      const updated = await rejectPaper(token, paperId, rejectReason);
      setPaper(updated);
      setShowRejectForm(false);
      setRejectReason("");
    } catch (err) {
      setActionError(friendlyError(err));
    } finally {
      setActionLoading(false);
    }
  }

  async function handleUnpublish() {
    setActionLoading(true);
    setActionError(null);
    try {
      const updated = await unpublishPaper(token, paperId);
      setPaper(updated);
    } catch (err) {
      setActionError(friendlyError(err));
    } finally {
      setActionLoading(false);
    }
  }

  async function handleDeleteQuestion(questionId) {
    setDeletingId(questionId);
    setActionError(null);
    try {
      await deleteQuestion(token, questionId);
      setQuestions((prev) => prev.filter((q) => q.id !== questionId));
    } catch (err) {
      setActionError(friendlyError(err));
    } finally {
      setDeletingId(null);
    }
  }

  if (loading) return <div className="p-6 text-sm text-ink-400">Loading…</div>;
  if (error) return <div className="p-6"><Alert>{error}</Alert></div>;
  if (!paper) return null;

  const isEditableStatus = paper.status === "Draft" || paper.status === "PendingReview";
  const canEdit = isEditableStatus && hasPermission("EditPaper");
  const canDelete = isEditableStatus && hasPermission("DeletePaper");
  const canUpload = paper.status === "Draft" && hasPermission("UploadPaper");
  const canReview = paper.status === "PendingReview" && hasPermission("PublishPaper");
  const canUnpublish = paper.status === "Published" && hasPermission("PublishPaper");

  return (
    <div>
      <PageHeader
        title={`${paper.examName} · ${paper.year}${paper.tier ? ` · ${paper.tier}` : ""}${paper.shift ? ` · ${paper.shift}` : ""}`}
        subtitle={`${paper.language}${paper.examDate ? ` · ${paper.examDate}` : ""}${paper.paperLabel ? ` · ${paper.paperLabel}` : ""}${paper.paperCode ? ` · ${paper.paperCode}` : ""} · ${questions.length} question${questions.length === 1 ? "" : "s"}`}
        action={
          <Button variant="ghost" onClick={() => navigate("/admin/papers")}>
            <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
            Back to Uploaded Papers
          </Button>
        }
      />

      <div className="p-6">
        <div className="mb-6 flex flex-wrap items-center gap-3">
          <StatusBadge status={paper.status} />
          {paper.rejectionReason && (
            <span className="text-xs font-medium text-red-600">Last rejection: {paper.rejectionReason}</span>
          )}

          <div className="ml-auto flex flex-wrap gap-2">
            {canUpload && (
              <Button onClick={() => navigate(`/admin/upload?resume=${paper.id}`)}>
                <PlusCircle className="h-4 w-4" strokeWidth={2.25} />
                Add More Questions
              </Button>
            )}
            {canReview && (
              <>
                <Button isLoading={actionLoading} onClick={handlePublish}>
                  <CheckCircle2 className="h-4 w-4" strokeWidth={2.25} />
                  Approve &amp; Publish
                </Button>
                <Button variant="danger" onClick={() => setShowRejectForm(true)}>
                  <XCircle className="h-4 w-4" strokeWidth={2.25} />
                  Reject
                </Button>
              </>
            )}
            {canUnpublish && (
              <Button variant="secondary" isLoading={actionLoading} onClick={handleUnpublish}>
                <LockOpen className="h-4 w-4" strokeWidth={2.25} />
                Unpublish to edit
              </Button>
            )}
          </div>
        </div>

        {showRejectForm && (
          <Card className="mb-6 max-w-lg">
            <h3 className="text-sm font-bold text-ink-900">Reject this paper</h3>
            <FormField label="Reason" hint="Shown to the admin who submitted it.">
              <TextArea rows={2} required value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} autoFocus />
            </FormField>
            <div className="mt-3 flex gap-2">
              <Button variant="danger" isLoading={actionLoading} onClick={handleReject} disabled={!rejectReason.trim()}>
                Confirm reject
              </Button>
              <Button variant="ghost" onClick={() => setShowRejectForm(false)}>Cancel</Button>
            </div>
          </Card>
        )}

        {/* MASTER PROMPT -- Previous Year Paper Practice: config + Question Bank mapping. Only
            editable while the paper is Draft/PendingReview (same rule as everything else here --
            Unpublish first to change a live paper). */}
        <PracticeSettingsCard
          paper={paper}
          canEdit={canEdit || canUpload}
          isLoading={actionLoading}
          onSave={handleSaveConfig}
        />

        {validation && paper.requiredQuestionCount != null && (
          <div className="mb-6">
            <ValidationSummary validation={validation} />
          </div>
        )}

        {(canEdit || canUpload) && (
          <Card className="mb-6">
            <div className="flex items-center gap-2">
              <Library className="h-4 w-4 text-secondary-500" strokeWidth={2.25} />
              <h3 className="text-sm font-bold text-ink-900">Question Bank questions mapped to this paper</h3>
            </div>
            <p className="mt-1 text-xs text-ink-400">
              Reuse existing Question Bank questions instead of re-entering them -- this paper's real question
              list is its {questions.length} PYQ-upload question{questions.length === 1 ? "" : "s"} below, plus
              whatever's mapped here, merged in Q.No order for the student.
            </p>

            {mappingError && <div className="mt-3"><Alert>{mappingError}</Alert></div>}

            <div className="mt-3 flex flex-col gap-2">
              {(mappedQuestions || []).filter((m) => m.source === "QuestionBank").map((m) => (
                <div key={m.linkId} className="flex items-start gap-2 rounded-lg border border-primary-100 p-2.5 text-xs">
                  <span className="mt-0.5 shrink-0 rounded-full bg-primary-50 px-2 py-0.5 font-bold text-primary-600">
                    Q.{m.questionNumber}{!m.isNumberExact && <span className="ml-1 font-normal text-accent-600" title="Auto-assigned, not the exact original position">~</span>}
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="line-clamp-2 text-ink-900">{m.questionText}</span>
                    <span className="mt-0.5 block text-[11px] text-ink-400">{m.subject} / {m.topic}</span>
                  </span>
                  <button
                    type="button"
                    onClick={() => handleUnmapQuestion(m.linkId)}
                    className="shrink-0 rounded-lg p-1.5 text-ink-400 hover:bg-red-50 hover:text-red-600"
                    title="Unmap from this paper"
                  >
                    <Link2Off className="h-3.5 w-3.5" strokeWidth={2.25} />
                  </button>
                </div>
              ))}
              {(mappedQuestions || []).filter((m) => m.source === "QuestionBank").length === 0 && (
                <p className="text-xs text-ink-400">No Question Bank questions mapped yet.</p>
              )}
            </div>

            {showQuestionPicker && (
              <div className="mt-4 border-t border-primary-100 pt-4">
                <h4 className="mb-2 text-xs font-bold text-ink-900">Add one, at an exact Q.No</h4>
                <TestQuestionPicker
                  token={token}
                  selectedRefs={(mappedQuestions || []).filter((m) => m.source === "QuestionBank").map((m) => ({ questionBankQuestionId: m.questionId }))}
                  onAdd={handleMapQuestion}
                />
              </div>
            )}

            {showBulkPicker && (
              <div className="mt-4 border-t border-primary-100 pt-4">
                <h4 className="mb-2 text-xs font-bold text-ink-900">Bulk-add several at once</h4>
                <PaperQuestionBulkPicker
                  token={token}
                  examId={paper.examId}
                  year={paper.year}
                  mappedQuestionBankIds={(mappedQuestions || []).filter((m) => m.source === "QuestionBank").map((m) => m.questionId)}
                  onBulkAdd={handleBulkAdd}
                />
              </div>
            )}

            {(showQuestionPicker || showBulkPicker) ? (
              <Button variant="ghost" className="mt-3" onClick={() => { setShowQuestionPicker(false); setShowBulkPicker(false); }}>Done</Button>
            ) : (
              <div className="mt-3 flex flex-wrap gap-2">
                <Button variant="secondary" onClick={() => setShowQuestionPicker(true)}>
                  <PlusCircle className="h-4 w-4" strokeWidth={2.25} />
                  Map one, at an exact Q.No
                </Button>
                <Button variant="secondary" onClick={() => setShowBulkPicker(true)}>
                  <PlusCircle className="h-4 w-4" strokeWidth={2.25} />
                  Bulk-add from Question Bank
                </Button>
              </div>
            )}
          </Card>
        )}

        {actionError && <div className="mb-4"><Alert>{actionError}</Alert></div>}

        <div className="flex flex-col gap-3">
          {questions.map((q) =>
            editingId === q.id ? (
              <QuestionEditForm
                key={q.id}
                question={q}
                token={token}
                onSaved={(updated) => {
                  setQuestions((prev) => prev.map((x) => (x.id === updated.id ? updated : x)));
                  setEditingId(null);
                }}
                onCancel={() => setEditingId(null)}
              />
            ) : (
              <QuestionCard
                key={q.id}
                question={q}
                canEdit={canEdit}
                canDelete={canDelete}
                isDeleting={deletingId === q.id}
                onEdit={() => setEditingId(q.id)}
                onDelete={() => {
                  if (window.confirm(`Delete question ${q.questionNumber}? This can't be undone.`)) {
                    handleDeleteQuestion(q.id);
                  }
                }}
              />
            )
          )}
        </div>
      </div>
    </div>
  );
}

function QuestionCard({ question: q, canEdit, canDelete, isDeleting, onEdit, onDelete }) {
  return (
    <Card>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2 text-xs font-semibold text-ink-400">
            <span className="rounded-full bg-primary-50 px-2 py-0.5 text-primary-600">Q.{q.questionNumber}</span>
            <span>{q.subject}</span>
            <span>· {q.topic}</span>
            <span>· {q.difficultyLevel}</span>
          </div>
          <p className="mt-2 text-sm font-medium text-ink-900">{q.questionText}</p>
          {imgSrc(q.questionImageUrl) && <img src={imgSrc(q.questionImageUrl)} alt="" className="mt-2 max-h-40 rounded-lg border border-primary-100" />}

          <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-2">
            {["A", "B", "C", "D"].map((letter) => (
              <div
                key={letter}
                className={`flex items-start gap-2 rounded-lg border p-2 text-xs ${
                  q.correctOption === letter ? "border-mint-500 bg-mint-50" : "border-primary-100"
                }`}
              >
                <span className="font-bold">{letter}.</span>
                <span className="flex-1">
                  {q[`option${letter}`]}
                  {imgSrc(q[`option${letter}ImageUrl`]) && (
                    <img src={imgSrc(q[`option${letter}ImageUrl`])} alt="" className="mt-1 max-h-20 rounded border border-primary-100" />
                  )}
                </span>
              </div>
            ))}
          </div>

          {q.explanation && <p className="mt-3 text-xs text-ink-600"><span className="font-semibold">Explanation:</span> {q.explanation}</p>}
          {imgSrc(q.explanationImageUrl) && <img src={imgSrc(q.explanationImageUrl)} alt="" className="mt-2 max-h-32 rounded-lg border border-primary-100" />}
        </div>

        {(canEdit || canDelete) && (
          <div className="flex shrink-0 gap-1.5">
            {canEdit && (
              <button onClick={onEdit} className="rounded-lg p-2 text-ink-400 hover:bg-primary-50 hover:text-primary-600" title="Edit">
                <Pencil className="h-4 w-4" strokeWidth={2.25} />
              </button>
            )}
            {canDelete && (
              <button onClick={onDelete} disabled={isDeleting} className="rounded-lg p-2 text-ink-400 hover:bg-red-50 hover:text-red-600" title="Delete">
                <Trash2 className="h-4 w-4" strokeWidth={2.25} />
              </button>
            )}
          </div>
        )}
      </div>
    </Card>
  );
}



function QuestionEditForm({ question: q, token, onSaved, onCancel }) {
  const [fields, setFields] = useState({
    questionNumber: q.questionNumber,
    subject: q.subject,
    topic: q.topic,
    difficultyLevel: q.difficultyLevel,
    questionText: q.questionText,
    optionA: q.optionA,
    optionB: q.optionB,
    optionC: q.optionC,
    optionD: q.optionD,
    correctOption: q.correctOption,
    explanation: q.explanation || "",
    sourceReference: q.sourceReference || "",
  });
  const [images, setImages] = useState({});
  const [removeImages, setRemoveImages] = useState({});
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
      const updated = await updateQuestion(token, q.id, fields, images, removeImages);
      onSaved(updated);
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Card>
      <form onSubmit={handleSave} className="flex flex-col gap-3">
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
          <TextArea required rows={3} value={fields.questionText} onChange={(e) => updateField("questionText", e.target.value)} />
        </FormField>
        <EditImageField label="Question image" currentUrl={q.questionImageUrl} onReplace={(f) => setImages((i) => ({ ...i, questionImage: f }))} onRemove={(v) => setRemoveImages((r) => ({ ...r, questionImage: v }))} />

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          {["A", "B", "C", "D"].map((letter) => (
            <div key={letter} className="rounded-xl2 border border-primary-100 p-3">
              <FormField label={`Option ${letter}`}>
                <div className="flex items-center gap-2">
                  <input type="radio" name={`correct-${q.id}`} checked={fields.correctOption === letter} onChange={() => updateField("correctOption", letter)} className="h-4 w-4 accent-mint-500" />
                  <TextInput required value={fields[`option${letter}`]} onChange={(e) => updateField(`option${letter}`, e.target.value)} />
                </div>
              </FormField>
              <EditImageField
                compact
                label={`Option ${letter} image`}
                currentUrl={q[`option${letter}ImageUrl`]}
                onReplace={(f) => setImages((i) => ({ ...i, [`option${letter}Image`]: f }))}
                onRemove={(v) => setRemoveImages((r) => ({ ...r, [`option${letter}Image`]: v }))}
              />
            </div>
          ))}
        </div>

        <FormField label="Explanation">
          <TextArea rows={2} value={fields.explanation} onChange={(e) => updateField("explanation", e.target.value)} />
        </FormField>
        <EditImageField label="Explanation image" currentUrl={q.explanationImageUrl} onReplace={(f) => setImages((i) => ({ ...i, explanationImage: f }))} onRemove={(v) => setRemoveImages((r) => ({ ...r, explanationImage: v }))} />

        <FormField label="Source reference">
          <TextInput value={fields.sourceReference} onChange={(e) => updateField("sourceReference", e.target.value)} />
        </FormField>

        {error && <Alert>{error}</Alert>}

        <div className="flex gap-2">
          <Button type="submit" isLoading={saving}>Save changes</Button>
          <Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button>
        </div>
      </form>
    </Card>
  );
}

function EditImageField({ label, currentUrl, onReplace, onRemove, compact }) {
  const [removed, setRemoved] = useState(false);
  const [newFile, setNewFile] = useState(null);

  return (
    <div className={compact ? "mt-2" : ""}>
      {!compact && <span className="mb-1 block text-xs font-semibold text-ink-600">{label}</span>}
      {currentUrl && !removed && !newFile && (
        <div className="mb-1.5 flex items-center gap-2">
          <img src={imgSrc(currentUrl)} alt="" className={compact ? "h-10 rounded border border-primary-100" : "h-16 rounded-lg border border-primary-100"} />
          <button
            type="button"
            onClick={() => { setRemoved(true); onRemove(true); }}
            className="text-xs font-semibold text-red-600 hover:underline"
          >
            Remove
          </button>
        </div>
      )}
      <label className={`flex cursor-pointer items-center gap-2 rounded-xl2 border border-dashed border-primary-100 bg-white text-ink-600 hover:border-secondary-500 ${compact ? "px-2.5 py-1.5 text-xs" : "px-3.5 py-2.5 text-sm"}`}>
        <ImagePlus className={compact ? "h-3.5 w-3.5 text-ink-400" : "h-4 w-4 text-ink-400"} strokeWidth={2} />
        <span className="truncate">{newFile ? newFile.name : compact ? label : currentUrl ? "Replace image" : "Add image"}</span>
        <input
          type="file"
          accept=".png,.jpg,.jpeg,.webp,.svg"
          className="hidden"
          onChange={(e) => {
            const f = e.target.files?.[0] || null;
            setNewFile(f);
            setRemoved(false);
            onRemove(false);
            onReplace(f);
          }}
        />
        {newFile && (
          <button
            type="button"
            onClick={(e) => { e.preventDefault(); setNewFile(null); onReplace(null); }}
            className="ml-auto text-ink-400 hover:text-red-500"
          >
            <X className="h-3.5 w-3.5" strokeWidth={2.5} />
          </button>
        )}
      </label>
    </div>
  );
}
