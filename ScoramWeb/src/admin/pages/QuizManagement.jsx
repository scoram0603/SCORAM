import { useEffect, useState } from "react";
import { Plus, X, Trash2 } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  listQuizzesAdmin, getQuizAdmin, createQuiz, updateQuiz, updateQuizStatus,
  addQuizQuestions, removeQuizQuestion,
} from "../api/quizzes";
import TestQuestionPicker from "../components/TestQuestionPicker";
import { PageHeader, Card, Button, FormField, TextInput, Select, Alert, friendlyError } from "../components/AdminUI";

// Admin side of Quizzes Phase 2 (see QuizzesAdminController and Models/QuizModels.cs). Deliberately
// leaner than Mock Test management -- no Duplicate/randomize-order/attempts dashboard yet (see the
// controller's own comment on why). Create is settings-only (no questions required up front, unlike
// Mock Test) -- immediately after creating, the same screen switches into edit mode so the admin
// adds questions right there, same two-step "identity first, then questions" flow as Papers.
export default function QuizManagement() {
  const { token } = useAdminAuth();
  const [mode, setMode] = useState("list");
  const [editingId, setEditingId] = useState(null);
  const [items, setItems] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(refresh, [token]);

  function refresh() {
    setLoading(true);
    listQuizzesAdmin(token, { page: 1, pageSize: 50 })
      .then((res) => setItems(res.items))
      .catch((err) => setError(friendlyError(err)))
      .finally(() => setLoading(false));
  }

  async function handleStatusChange(id, status) {
    try {
      await updateQuizStatus(token, id, status);
      refresh();
    } catch (err) {
      window.alert(friendlyError(err));
    }
  }

  if (mode === "form") {
    return (
      <QuizForm
        token={token}
        quizId={editingId}
        onCancel={() => { setMode("list"); setEditingId(null); }}
        onCreated={(id) => setEditingId(id)}
        onDone={() => { setMode("list"); setEditingId(null); refresh(); }}
      />
    );
  }

  return (
    <div>
      <PageHeader
        title="Quizzes"
        subtitle="Short, themed quizzes with their own live window — e.g. a daily Current Affairs quiz"
        action={
          <Button onClick={() => { setEditingId(null); setMode("form"); }}>
            <Plus className="h-4 w-4" strokeWidth={2.5} />
            Create Quiz
          </Button>
        }
      />

      <div className="p-6">
        {error && <div className="mb-4"><Alert>{error}</Alert></div>}

        <Card className="!p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="bg-primary-50 text-ink-600">
                <tr>
                  <th className="px-3 py-2.5">Title</th>
                  <th className="px-3 py-2.5">Topic</th>
                  <th className="px-3 py-2.5">Questions</th>
                  <th className="px-3 py-2.5">Duration</th>
                  <th className="px-3 py-2.5">Availability</th>
                  <th className="px-3 py-2.5">Status</th>
                  <th className="px-3 py-2.5"></th>
                </tr>
              </thead>
              <tbody>
                {loading && <tr><td colSpan={7} className="px-3 py-6 text-center text-ink-400">Loading…</td></tr>}
                {!loading && items.length === 0 && <tr><td colSpan={7} className="px-3 py-6 text-center text-ink-400">No quizzes yet.</td></tr>}
                {items.map((q) => (
                  <tr key={q.id} className="border-t border-primary-50">
                    <td className="px-3 py-2.5 font-medium text-ink-900">{q.title}</td>
                    <td className="px-3 py-2.5 text-ink-600">{q.topic || "—"}</td>
                    <td className="px-3 py-2.5 text-ink-600">{q.questionCount}</td>
                    <td className="px-3 py-2.5 text-ink-600">{q.durationMinutes} min</td>
                    <td className="px-3 py-2.5 text-ink-600">{q.availabilityStatus}</td>
                    <td className="px-3 py-2.5">
                      <Select value={q.status} onChange={(e) => handleStatusChange(q.id, e.target.value)} className="!h-8 !py-1 text-xs">
                        <option value="Draft">Draft</option>
                        <option value="Published">Published</option>
                        <option value="Archived">Archived</option>
                      </Select>
                    </td>
                    <td className="px-3 py-2.5">
                      <button type="button" onClick={() => { setEditingId(q.id); setMode("form"); }} className="font-semibold text-secondary-500 hover:underline">Edit</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      </div>
    </div>
  );
}

function QuizForm({ token, quizId, onCancel, onCreated, onDone }) {
  const isEdit = Boolean(quizId);
  const [title, setTitle] = useState("");
  const [topic, setTopic] = useState("");
  const [durationMinutes, setDurationMinutes] = useState(10);
  const [negativeMarkingRatio, setNegativeMarkingRatio] = useState(0);
  const [availableFrom, setAvailableFrom] = useState("");
  const [availableTo, setAvailableTo] = useState("");
  const [maxAttempts, setMaxAttempts] = useState(1);
  const [existingQuestions, setExistingQuestions] = useState([]);

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  useEffect(() => {
    if (!isEdit) return;
    getQuizAdmin(token, quizId).then((q) => {
      setTitle(q.title);
      setTopic(q.topic || "");
      setDurationMinutes(q.durationMinutes);
      setNegativeMarkingRatio(q.negativeMarkingRatio);
      setAvailableFrom(q.availableFrom ? q.availableFrom.slice(0, 16) : "");
      setAvailableTo(q.availableTo ? q.availableTo.slice(0, 16) : "");
      setMaxAttempts(q.maxAttempts ?? "");
      setExistingQuestions(q.questions || []);
    }).catch((err) => setError(friendlyError(err)));
  }, [isEdit, quizId, token]);

  function buildPayload() {
    return {
      title,
      topic: topic || null,
      durationMinutes: Number(durationMinutes),
      negativeMarkingRatio: Number(negativeMarkingRatio),
      availableFrom: availableFrom || null,
      availableTo: availableTo || null,
      maxAttempts: maxAttempts === "" ? null : Number(maxAttempts),
      status: "Draft",
    };
  }

  async function handleSubmit(e) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      if (isEdit) {
        await updateQuiz(token, quizId, buildPayload());
        onDone();
      } else {
        const created = await createQuiz(token, buildPayload());
        // Straight into edit mode for the same quiz -- questions are added in the next step, same
        // two-step flow as Papers ("create identity" then "add questions").
        onCreated(created.id);
      }
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleAddQuestion({ questionBankQuestionId }) {
    try {
      await addQuizQuestions(token, quizId, [questionBankQuestionId]);
      const q = await getQuizAdmin(token, quizId);
      setExistingQuestions(q.questions || []);
    } catch (err) {
      window.alert(friendlyError(err));
    }
  }

  async function handleRemoveExisting(quizQuestionId) {
    try {
      await removeQuizQuestion(token, quizId, quizQuestionId);
      setExistingQuestions((prev) => prev.filter((q) => q.quizQuestionId !== quizQuestionId));
    } catch (err) {
      window.alert(friendlyError(err));
    }
  }

  return (
    <div>
      <PageHeader
        title={isEdit ? "Edit Quiz" : "Create Quiz"}
        action={<Button variant="ghost" onClick={onCancel}><X className="h-4 w-4" strokeWidth={2.5} />Cancel</Button>}
      />

      <div className="grid grid-cols-1 gap-4 p-6 lg:grid-cols-2">
        <Card>
          <form onSubmit={handleSubmit} className="flex flex-col gap-3">
            <FormField label="Title"><TextInput required value={title} onChange={(e) => setTitle(e.target.value)} placeholder="e.g. Daily Current Affairs" /></FormField>
            <FormField label="Topic (optional)" hint="Shown to students as a theme label"><TextInput value={topic} onChange={(e) => setTopic(e.target.value)} placeholder="Current Affairs" /></FormField>

            <div className="grid grid-cols-2 gap-3">
              <FormField label="Duration (minutes)"><TextInput type="number" min="1" required value={durationMinutes} onChange={(e) => setDurationMinutes(e.target.value)} /></FormField>
              <FormField label="Negative Marking Ratio"><TextInput type="number" step="0.05" min="0" max="2" value={negativeMarkingRatio} onChange={(e) => setNegativeMarkingRatio(e.target.value)} /></FormField>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <FormField label="Live from (optional)"><TextInput type="datetime-local" value={availableFrom} onChange={(e) => setAvailableFrom(e.target.value)} /></FormField>
              <FormField label="Live until (optional)"><TextInput type="datetime-local" value={availableTo} onChange={(e) => setAvailableTo(e.target.value)} /></FormField>
            </div>

            <FormField label="Max Attempts (blank = unlimited)" hint="Defaults to 1 — a daily check-in, not a redo-able paper">
              <TextInput type="number" min="1" value={maxAttempts} onChange={(e) => setMaxAttempts(e.target.value)} />
            </FormField>

            {error && <Alert>{error}</Alert>}

            <Button type="submit" isLoading={saving}>{isEdit ? "Save changes" : "Create & add questions"}</Button>
          </form>
        </Card>

        <Card>
          <h3 className="text-sm font-bold text-ink-900">Questions</h3>
          <p className="mt-0.5 text-xs text-ink-400">Search and add from the Question Bank.</p>

          {!isEdit && (
            <p className="mt-3 rounded-xl bg-primary-50 px-3 py-2.5 text-xs text-ink-600">
              Save the quiz's details first — you'll be able to add questions right after.
            </p>
          )}

          {isEdit && (
            <>
              {existingQuestions.length > 0 && (
                <div className="mt-3 flex flex-col divide-y divide-primary-50 border-b border-primary-50 pb-2">
                  {existingQuestions.map((q, i) => (
                    <div key={q.quizQuestionId} className="flex items-start justify-between gap-2 py-2 text-xs">
                      <span className="min-w-0 flex-1">
                        <span className="font-semibold">{i + 1}.</span> {q.questionText}
                        <span className="ml-1 text-ink-400">({q.subject})</span>
                      </span>
                      <button type="button" onClick={() => handleRemoveExisting(q.quizQuestionId)} className="shrink-0 text-red-500">
                        <Trash2 className="h-3.5 w-3.5" strokeWidth={2.25} />
                      </button>
                    </div>
                  ))}
                </div>
              )}
              {existingQuestions.length === 0 && (
                <p className="mt-3 text-xs text-ink-400">No questions added yet — publishing needs at least one.</p>
              )}

              <div className="mt-3">
                <TestQuestionPicker token={token} selectedRefs={existingQuestions.map((q) => ({ questionBankQuestionId: q.questionBankQuestionId }))} onAdd={handleAddQuestion} />
              </div>

              <Button variant="secondary" className="mt-3" onClick={onDone}>Done</Button>
            </>
          )}
        </Card>
      </div>
    </div>
  );
}
