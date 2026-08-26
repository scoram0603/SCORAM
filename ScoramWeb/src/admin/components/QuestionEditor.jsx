import { useState } from "react";
import { Pencil, Trash2 } from "lucide-react";
import { updateQuestion } from "../api/adminQuestions";
import { Card, Button, FormField, TextInput, TextArea, Select, Alert, friendlyError } from "./AdminUI";
import EditImageField, { imgSrc } from "./EditImageField";

const DIFFICULTIES = ["Easy", "Medium", "Hard"];

// Read-only display of one paper question, with optional Edit/Delete affordances -- used by
// PaperDetailView's main question list and by BulkImportPanel's "Recent imports" history (spec
// section 10, so an admin can find and fix a specific batch's questions without hunting through the
// paper's full Q.1..Q.N list).
export function QuestionCard({ question: q, canEdit, canDelete, isDeleting, onEdit, onDelete }) {
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

// Editable form for one paper question -- PATCH /api/questions/{id} (server-side gated to a
// Draft-or-PendingReview paper; see QuestionsController.Update). Same field set as the one-by-one
// upload form, including per-option/explanation images.
export function QuestionEditForm({ question: q, token, onSaved, onCancel }) {
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
