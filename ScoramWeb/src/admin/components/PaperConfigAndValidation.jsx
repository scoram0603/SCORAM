import { useState, useEffect } from "react";
import { Settings2, Pencil } from "lucide-react";
import { Card, Button, FormField, TextInput, Select } from "./AdminUI";
import { listExams } from "../api/exams";

// Exam/Year/Medium/Tier/Shift/Date/Code/Label -- a paper's full identity (see Models/Paper.cs).
// Fixed at creation everywhere else in the admin panel; this is the one place it can be corrected
// afterward, added for the bulk paper-shell upload flow so a row that resolved to the wrong exam
// (or had a typo'd year/tier/etc) doesn't force a delete-and-recreate. Read-only summary by default;
// canEdit gates whether the "Edit" button (and therefore the form) appears at all.
export function PaperIdentityCard({ paper, canEdit, isLoading, onSave }) {
  const [editing, setEditing] = useState(false);
  const [exams, setExams] = useState(null);
  const [examId, setExamId] = useState(paper.examId);
  const [year, setYear] = useState(paper.year);
  const [language, setLanguage] = useState(paper.language);
  const [tier, setTier] = useState(paper.tier ?? "");
  const [shift, setShift] = useState(paper.shift ?? "");
  const [examDate, setExamDate] = useState(paper.examDate ?? "");
  const [paperCode, setPaperCode] = useState(paper.paperCode ?? "");
  const [paperLabel, setPaperLabel] = useState(paper.paperLabel ?? "");

  useEffect(() => {
    if (!editing || exams) return;
    listExams().then(setExams).catch(() => setExams([]));
  }, [editing, exams]);

  function handleSubmit(e) {
    e.preventDefault();
    onSave({
      examId,
      year: Number(year),
      language,
      tier: tier || null,
      shift: shift || null,
      examDate: examDate || null,
      paperCode: paperCode || null,
      paperLabel: paperLabel || null,
    });
  }

  if (!editing) {
    return (
      <Card className="mb-6">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Pencil className="h-4 w-4 text-secondary-500" strokeWidth={2.25} />
            <h3 className="text-sm font-bold text-ink-900">Paper identity</h3>
          </div>
          {canEdit && <Button variant="ghost" onClick={() => setEditing(true)}>Edit</Button>}
        </div>
        <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-ink-500">
          <span>{paper.examName}</span>
          <span>{paper.year}</span>
          <span>{paper.language}</span>
          {paper.tier && <span>{paper.tier}</span>}
          {paper.shift && <span>{paper.shift}</span>}
          {paper.examDate && <span>{paper.examDate}</span>}
          {paper.paperCode && <span>Set {paper.paperCode}</span>}
        </div>
      </Card>
    );
  }

  return (
    <Card className="mb-6">
      <div className="flex items-center gap-2">
        <Pencil className="h-4 w-4 text-secondary-500" strokeWidth={2.25} />
        <h3 className="text-sm font-bold text-ink-900">Edit paper identity</h3>
      </div>
      <form onSubmit={handleSubmit} className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
        <FormField label="Exam">
          <Select value={examId} onChange={(e) => setExamId(e.target.value)}>
            {(exams ?? [{ id: paper.examId, name: paper.examName }]).map((ex) => (
              <option key={ex.id} value={ex.id}>{ex.name}</option>
            ))}
          </Select>
        </FormField>
        <FormField label="Year">
          <TextInput type="number" value={year} onChange={(e) => setYear(e.target.value)} />
        </FormField>
        <FormField label="Medium">
          <Select value={language} onChange={(e) => setLanguage(e.target.value)}>
            <option value="Hindi">Hindi</option>
            <option value="English">English</option>
          </Select>
        </FormField>
        <FormField label="Tier"><TextInput value={tier} onChange={(e) => setTier(e.target.value)} placeholder="optional" /></FormField>
        <FormField label="Shift"><TextInput value={shift} onChange={(e) => setShift(e.target.value)} placeholder="optional" /></FormField>
        <FormField label="Exam date"><TextInput type="date" value={examDate} onChange={(e) => setExamDate(e.target.value)} /></FormField>
        <FormField label="Paper code (Set)"><TextInput value={paperCode} onChange={(e) => setPaperCode(e.target.value)} placeholder="optional" /></FormField>
        <FormField label="Paper label"><TextInput value={paperLabel} onChange={(e) => setPaperLabel(e.target.value)} placeholder="optional" /></FormField>
        <div className="flex gap-2 sm:col-span-3">
          <Button type="submit" isLoading={isLoading}>Save</Button>
          <Button type="button" variant="ghost" onClick={() => setEditing(false)}>Cancel</Button>
        </div>
      </form>
    </Card>
  );
}

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
