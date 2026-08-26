import { useEffect, useState } from "react";
import { Plus, X, Trash2, Copy } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  listMockTestsAdmin, getMockTestAdmin, createMockTest, updateMockTest, updateMockTestStatus,
  duplicateMockTest, addMockTestQuestions, removeMockTestQuestion,
} from "../api/mockTests";
import TestQuestionPicker from "../components/TestQuestionPicker";
import { PageHeader, Card, Button, FormField, TextInput, TextArea, Select, Alert, friendlyError } from "../components/AdminUI";

export default function MockTestManagement() {
  const { token } = useAdminAuth();
  const [mode, setMode] = useState("list");
  const [editingId, setEditingId] = useState(null);
  const [items, setItems] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(refresh, [token]);

  function refresh() {
    setLoading(true);
    listMockTestsAdmin(token, { page: 1, pageSize: 50 })
      .then((res) => setItems(res.items))
      .catch((err) => setError(friendlyError(err)))
      .finally(() => setLoading(false));
  }

  async function handleStatusChange(id, status) {
    try {
      await updateMockTestStatus(token, id, status);
      refresh();
    } catch (err) {
      window.alert(friendlyError(err));
    }
  }

  async function handleDuplicate(id) {
    try {
      await duplicateMockTest(token, id);
      refresh();
    } catch (err) {
      window.alert(friendlyError(err));
    }
  }

  if (mode === "form") {
    return (
      <MockTestForm
        token={token}
        testId={editingId}
        onCancel={() => { setMode("list"); setEditingId(null); }}
        onSaved={() => { setMode("list"); setEditingId(null); refresh(); }}
      />
    );
  }

  return (
    <div>
      <PageHeader
        title="Mock Tests"
        subtitle="Full exam-simulation papers — admin-controlled, fixed question set"
        action={
          <Button onClick={() => { setEditingId(null); setMode("form"); }}>
            <Plus className="h-4 w-4" strokeWidth={2.5} />
            Create Mock Test
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
                  <th className="px-3 py-2.5">Exam</th>
                  <th className="px-3 py-2.5">Medium</th>
                  <th className="px-3 py-2.5">Questions</th>
                  <th className="px-3 py-2.5">Duration</th>
                  <th className="px-3 py-2.5">Attempts</th>
                  <th className="px-3 py-2.5">Status</th>
                  <th className="px-3 py-2.5"></th>
                </tr>
              </thead>
              <tbody>
                {loading && <tr><td colSpan={8} className="px-3 py-6 text-center text-ink-400">Loading…</td></tr>}
                {!loading && items.length === 0 && <tr><td colSpan={8} className="px-3 py-6 text-center text-ink-400">No mock tests yet.</td></tr>}
                {items.map((t) => (
                  <tr key={t.id} className="border-t border-primary-50">
                    <td className="px-3 py-2.5 font-medium text-ink-900">{t.title}</td>
                    <td className="px-3 py-2.5 text-ink-600">{t.examName}</td>
                    <td className="px-3 py-2.5 text-ink-600">{t.language || <span className="text-ink-300">—</span>}</td>
                    <td className="px-3 py-2.5 text-ink-600">{t.questionCount}</td>
                    <td className="px-3 py-2.5 text-ink-600">{t.durationMinutes} min</td>
                    <td className="px-3 py-2.5 text-ink-600">{t.attemptCount ?? 0}</td>
                    <td className="px-3 py-2.5">
                      <Select value={t.status} onChange={(e) => handleStatusChange(t.id, e.target.value)} className="!h-8 !py-1 text-xs">
                        <option value="Draft">Draft</option>
                        <option value="Published">Published</option>
                        <option value="Archived">Archived</option>
                      </Select>
                    </td>
                    <td className="px-3 py-2.5">
                      <div className="flex items-center justify-end gap-2">
                        <button type="button" onClick={() => { setEditingId(t.id); setMode("form"); }} className="font-semibold text-secondary-500 hover:underline">Edit</button>
                        <button type="button" onClick={() => handleDuplicate(t.id)} className="flex items-center gap-1 font-semibold text-ink-400 hover:text-ink-600">
                          <Copy className="h-3.5 w-3.5" strokeWidth={2.25} />
                        </button>
                      </div>
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

function MockTestForm({ token, testId, onCancel, onSaved }) {
  const isEdit = Boolean(testId);
  const [title, setTitle] = useState("");
  const [examName, setExamName] = useState("");
  const [durationMinutes, setDurationMinutes] = useState(60);
  const [negativeMarkingRatio, setNegativeMarkingRatio] = useState(0.25);
  const [isRandomOrder, setIsRandomOrder] = useState(false);
  const [language, setLanguage] = useState(""); // "" | "Hindi" | "English"
  const [scheduledAt, setScheduledAt] = useState("");
  const [endAt, setEndAt] = useState("");
  const [maxAttempts, setMaxAttempts] = useState("");
  const [instructions, setInstructions] = useState("");
  const [refs, setRefs] = useState([]);
  const [existingQuestions, setExistingQuestions] = useState([]);

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  useEffect(() => {
    if (!isEdit) return;
    getMockTestAdmin(token, testId).then((t) => {
      setTitle(t.title);
      setExamName(t.examName);
      setDurationMinutes(t.durationMinutes);
      setNegativeMarkingRatio(t.negativeMarkingRatio);
      setIsRandomOrder(t.isRandomOrder);
      setLanguage(t.language || "");
      setScheduledAt(t.scheduledAt ? t.scheduledAt.slice(0, 16) : "");
      setEndAt(t.endAt ? t.endAt.slice(0, 16) : "");
      setMaxAttempts(t.maxAttempts ?? "");
      setInstructions(t.instructions || "");
      setExistingQuestions(t.questions || []);
    }).catch((err) => setError(friendlyError(err)));
  }, [isEdit, testId, token]);

  function handleAddRef(ref) {
    setRefs((prev) => [...prev, ref]);
  }

  function handleRemoveRef(index) {
    setRefs((prev) => prev.filter((_, i) => i !== index));
  }

  async function handleRemoveExisting(mockTestQuestionId) {
    try {
      await removeMockTestQuestion(token, testId, mockTestQuestionId);
      setExistingQuestions((prev) => prev.filter((q) => q.id !== mockTestQuestionId));
    } catch (err) {
      window.alert(friendlyError(err));
    }
  }

  async function handleSubmit(e) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      const payload = {
        title, examName,
        testType: "FullMockTest",
        durationMinutes: Number(durationMinutes),
        negativeMarkingRatio: Number(negativeMarkingRatio),
        isRandomOrder,
        isShuffleOptions: false,
        language: language || null,
        scheduledAt: scheduledAt || null,
        endAt: endAt || null,
        maxAttempts: maxAttempts === "" ? null : Number(maxAttempts),
        instructions: instructions || null,
        status: "Draft",
        questionRefs: refs,
      };

      if (isEdit) {
        await updateMockTest(token, testId, payload);
        if (refs.length > 0) await addMockTestQuestions(token, testId, refs);
      } else {
        if (refs.length === 0) {
          setError("Add at least one question.");
          setSaving(false);
          return;
        }
        await createMockTest(token, payload);
      }
      onSaved();
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div>
      <PageHeader
        title={isEdit ? "Edit Mock Test" : "Create Mock Test"}
        action={<Button variant="ghost" onClick={onCancel}><X className="h-4 w-4" strokeWidth={2.5} />Cancel</Button>}
      />

      <div className="grid grid-cols-1 gap-4 p-6 lg:grid-cols-2">
        <Card>
          <form onSubmit={handleSubmit} className="flex flex-col gap-3">
            <FormField label="Title"><TextInput required value={title} onChange={(e) => setTitle(e.target.value)} /></FormField>
            <FormField label="Exam Name"><TextInput required value={examName} onChange={(e) => setExamName(e.target.value)} /></FormField>

            <div className="grid grid-cols-2 gap-3">
              <FormField label="Duration (minutes)"><TextInput type="number" required value={durationMinutes} onChange={(e) => setDurationMinutes(e.target.value)} /></FormField>
              <FormField label="Negative Marking Ratio"><TextInput type="number" step="0.05" min="0" max="1" value={negativeMarkingRatio} onChange={(e) => setNegativeMarkingRatio(e.target.value)} /></FormField>
            </div>

            <FormField label="Medium / Language (optional)">
              <Select value={language} onChange={(e) => setLanguage(e.target.value)}>
                <option value="">Not set (shows for every student)</option>
                <option value="Hindi">Hindi</option>
                <option value="English">English</option>
              </Select>
            </FormField>

            <div className="grid grid-cols-2 gap-3">
              <FormField label="Starts (optional)"><TextInput type="datetime-local" value={scheduledAt} onChange={(e) => setScheduledAt(e.target.value)} /></FormField>
              <FormField label="Ends (optional)"><TextInput type="datetime-local" value={endAt} onChange={(e) => setEndAt(e.target.value)} /></FormField>
            </div>

            <FormField label="Max Attempts (blank = unlimited)"><TextInput type="number" min="1" value={maxAttempts} onChange={(e) => setMaxAttempts(e.target.value)} /></FormField>

            <label className="flex items-center gap-2 text-xs font-semibold text-ink-600">
              <input type="checkbox" checked={isRandomOrder} onChange={(e) => setIsRandomOrder(e.target.checked)} />
              Randomize question order per student
            </label>

            <FormField label="Instructions (optional)"><TextArea rows={3} value={instructions} onChange={(e) => setInstructions(e.target.value)} /></FormField>

            {error && <Alert>{error}</Alert>}

            <Button type="submit" isLoading={saving}>{isEdit ? "Save changes" : "Create Mock Test"}</Button>
          </form>
        </Card>

        <Card>
          <h3 className="text-sm font-bold text-ink-900">Questions</h3>
          <p className="mt-0.5 text-xs text-ink-400">Search and add from the Question Bank.</p>

          {existingQuestions.length > 0 && (
            <div className="mt-3 flex flex-col divide-y divide-primary-50 border-b border-primary-50 pb-2">
              {existingQuestions.map((q, i) => (
                <div key={q.id} className="flex items-start justify-between gap-2 py-2 text-xs">
                  <span className="min-w-0 flex-1"><span className="font-semibold">{i + 1}.</span> {q.questionText}</span>
                  <button type="button" onClick={() => handleRemoveExisting(q.id)} className="shrink-0 text-red-500"><Trash2 className="h-3.5 w-3.5" strokeWidth={2.25} /></button>
                </div>
              ))}
            </div>
          )}

          {refs.length > 0 && (
            <div className="mt-2 flex flex-col divide-y divide-primary-50 border-b border-primary-50 pb-2">
              {refs.map((r, i) => (
                <div key={i} className="flex items-center justify-between gap-2 py-1.5 text-xs text-ink-600">
                  <span>New question #{i + 1} selected</span>
                  <button type="button" onClick={() => handleRemoveRef(i)} className="text-red-500"><X className="h-3.5 w-3.5" strokeWidth={2.25} /></button>
                </div>
              ))}
            </div>
          )}

          <div className="mt-3">
            <TestQuestionPicker token={token} selectedRefs={refs} onAdd={handleAddRef} />
          </div>
        </Card>
      </div>
    </div>
  );
}
