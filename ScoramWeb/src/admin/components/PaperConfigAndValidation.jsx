import { useState } from "react";
import { Settings2 } from "lucide-react";
import { Card, Button, FormField, TextInput } from "./AdminUI";

// Duration / negative marking / required-question-count for a Previous Year Paper Practice paper.
// Shared by PaperDetailView (editing an already-created paper) and the PYP Paper Builder wizard
// (setting it right after creating one) so there's exactly one place this form lives.
export function PracticeSettingsCard({ paper, canEdit, isLoading, onSave }) {
  const [durationMinutes, setDurationMinutes] = useState(paper.durationMinutes ?? "");
  const [negativeMarkingRatio, setNegativeMarkingRatio] = useState(paper.negativeMarkingRatio ?? "");
  const [requiredQuestionCount, setRequiredQuestionCount] = useState(paper.requiredQuestionCount ?? "");

  function handleSubmit(e) {
    e.preventDefault();
    onSave({
      durationMinutes: durationMinutes === "" ? null : Number(durationMinutes),
      negativeMarkingRatio: negativeMarkingRatio === "" ? null : Number(negativeMarkingRatio),
      requiredQuestionCount: requiredQuestionCount === "" ? null : Number(requiredQuestionCount),
    });
  }

  return (
    <Card className="mb-6">
      <div className="flex items-center gap-2">
        <Settings2 className="h-4 w-4 text-secondary-500" strokeWidth={2.25} />
        <h3 className="text-sm font-bold text-ink-900">Previous Year Paper Practice settings</h3>
      </div>
      <p className="mt-1 text-xs text-ink-400">
        Set these to let students attempt this as a real timed paper. Leave blank and it stays browsable
        (Find PYQs) but won't offer a "Start Paper" attempt.
      </p>

      {canEdit ? (
        <form onSubmit={handleSubmit} className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
          <FormField label="Duration (minutes)">
            <TextInput type="number" min="1" value={durationMinutes} onChange={(e) => setDurationMinutes(e.target.value)} placeholder="e.g. 60" />
          </FormField>
          <FormField label="Negative marking (per wrong answer)">
            <TextInput type="number" min="0" step="0.25" value={negativeMarkingRatio} onChange={(e) => setNegativeMarkingRatio(e.target.value)} placeholder="e.g. 0.5" />
          </FormField>
          <FormField label="Required question count">
            <TextInput type="number" min="1" value={requiredQuestionCount} onChange={(e) => setRequiredQuestionCount(e.target.value)} placeholder="e.g. 100" />
          </FormField>
          <div className="sm:col-span-3">
            <Button type="submit" isLoading={isLoading}>Save settings</Button>
          </div>
        </form>
      ) : (
        <div className="mt-3 grid grid-cols-3 gap-3 text-sm text-ink-600">
          <span>{paper.durationMinutes ? `${paper.durationMinutes} min` : "Not set"}</span>
          <span>{paper.negativeMarkingRatio != null ? `-${paper.negativeMarkingRatio} per wrong` : "Not set"}</span>
          <span>{paper.requiredQuestionCount ? `${paper.requiredQuestionCount} required` : "Not set"}</span>
        </div>
      )}
    </Card>
  );
}

export function ValidationSummary({ validation }) {
  const ok = validation.isReadyToPublish;
  return (
    <div className={`rounded-xl2 border p-3 text-xs font-medium ${ok ? "border-mint-200 bg-mint-50 text-mint-600" : "border-secondary-200 bg-secondary-50 text-secondary-600"}`}>
      {validation.actualQuestionCount} / {validation.requiredQuestionCount} questions mapped.
      {validation.missingCount > 0 && ` ${validation.missingCount} missing.`}
      {validation.duplicateQuestionNumbers.length > 0 && ` Duplicate Q.No: ${validation.duplicateQuestionNumbers.join(", ")}.`}
      {ok && " Ready to publish."}
      {validation.hasApproximateQuestionNumbers && (
        <span className="mt-1 block text-accent-600">
          ⚠ Some Q.No are auto-assigned (bulk-added from Question Bank), not the exact original
          position. Students will get a subject-grouped order for this paper instead of a numbered
          sequence.
        </span>
      )}
    </div>
  );
}
