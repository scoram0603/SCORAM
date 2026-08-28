import { useEffect, useState } from "react";
import { ArrowLeft, PlusCircle, CheckCircle2, XCircle, LockOpen, Link2Off, Library } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  getPaper, publishPaper, rejectPaper, unpublishPaper,
  getMappedQuestions, updatePaperConfig, mapQuestionToPaper, mapQuestionsBulkToPaper, unmapQuestionFromPaper, validatePaper,
} from "../api/papers";
import { deleteQuestion } from "../api/adminQuestions";
import { PageHeader, Card, Button, FormField, TextArea, Alert, StatusBadge, friendlyError } from "../components/AdminUI";
import TestQuestionPicker from "../components/TestQuestionPicker";
import PaperQuestionBulkPicker from "../components/PaperQuestionBulkPicker";
import { PracticeSettingsCard, ValidationSummary } from "../components/PaperConfigAndValidation";
import { QuestionCard, QuestionEditForm } from "../components/QuestionEditor";

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
          <Card className="mx-auto mb-6 max-w-lg">
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


