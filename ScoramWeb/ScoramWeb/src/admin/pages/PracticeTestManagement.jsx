import { useEffect, useState } from "react";
import { Plus, X, Trash2 } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  listPracticeTestTemplatesAdmin, getPracticeTestTemplateAdmin, createPracticeTestTemplate,
  updatePracticeTestTemplate, updatePracticeTestTemplateStatus,
} from "../api/practiceTests";
import { listSubjects, listTopics } from "../api/questionBank";
import { listExams } from "../api/exams";
import TestQuestionPicker from "../components/TestQuestionPicker";
import { PageHeader, Card, Button, FormField, TextInput, Select, Alert, friendlyError } from "../components/AdminUI";

export default function PracticeTestManagement() {
  const { token } = useAdminAuth();
  const [mode, setMode] = useState("list");
  const [editingId, setEditingId] = useState(null);
  const [items, setItems] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(refresh, [token]);

  function refresh() {
    setLoading(true);
    listPracticeTestTemplatesAdmin(token, { page: 1, pageSize: 50 })
      .then((res) => setItems(res.items))
      .catch((err) => setError(friendlyError(err)))
      .finally(() => setLoading(false));
  }

  async function handleStatusChange(id, status) {
    try {
      await updatePracticeTestTemplateStatus(token, id, status);
      refresh();
    } catch (err) {
      window.alert(friendlyError(err));
    }
  }

  if (mode === "form") {
    return (
      <PracticeTestForm
        token={token}
        templateId={editingId}
        onCancel={() => { setMode("list"); setEditingId(null); }}
        onSaved={() => { setMode("list"); setEditingId(null); refresh(); }}
      />
    );
  }

  return (
    <div>
      <PageHeader
        title="Practice Test Templates"
        subtitle="Named, browsable Practice Tests — curated question list or a saved filter"
        action={
          <Button onClick={() => { setEditingId(null); setMode("form"); }}>
            <Plus className="h-4 w-4" strokeWidth={2.5} />
            Create Template
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
                  <th className="px-3 py-2.5">Type</th>
                  <th className="px-3 py-2.5">Questions</th>
                  <th className="px-3 py-2.5">Attempts</th>
                  <th className="px-3 py-2.5">Status</th>
                  <th className="px-3 py-2.5"></th>
                </tr>
              </thead>
              <tbody>
                {loading && <tr><td colSpan={6} className="px-3 py-6 text-center text-ink-400">Loading…</td></tr>}
                {!loading && items.length === 0 && <tr><td colSpan={6} className="px-3 py-6 text-center text-ink-400">No templates yet.</td></tr>}
                {items.map((t) => (
                  <tr key={t.id} className="border-t border-primary-50">
                    <td className="px-3 py-2.5 font-medium text-ink-900">{t.title}</td>
                    <td className="px-3 py-2.5 text-ink-600">{t.isCurated ? "Curated" : "Filter-based"}</td>
                    <td className="px-3 py-2.5 text-ink-600">{t.questionCount}</td>
                    <td className="px-3 py-2.5 text-ink-600">{t.attemptCount}</td>
                    <td className="px-3 py-2.5">
                      <Select value={t.status} onChange={(e) => handleStatusChange(t.id, e.target.value)} className="!h-8 !py-1 text-xs">
                        <option value="Draft">Draft</option>
                        <option value="Published">Published</option>
                        <option value="Archived">Archived</option>
                      </Select>
                    </td>
                    <td className="px-3 py-2.5">
                      <button type="button" onClick={() => { setEditingId(t.id); setMode("form"); }} className="font-semibold text-secondary-500 hover:underline">Edit</button>
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

function PracticeTestForm({ token, templateId, onCancel, onSaved }) {
  const isEdit = Boolean(templateId);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [subjects, setSubjects] = useState([]);
  const [topics, setTopics] = useState([]);
  const [exams, setExams] = useState([]);
  const [subjectId, setSubjectId] = useState("");
  const [topicId, setTopicId] = useState("");
  const [examId, setExamId] = useState("");
  const [difficulty, setDifficulty] = useState("");
  const [questionCount, setQuestionCount] = useState(20);
  const [durationMinutes, setDurationMinutes] = useState(20);
  const [negativeMarkingRatio, setNegativeMarkingRatio] = useState(0);
  const [isRandomOrder, setIsRandomOrder] = useState(true);
  const [refs, setRefs] = useState([]);

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  useEffect(() => {
    listSubjects(token, false).then(setSubjects).catch(() => {});
    listExams().then(setExams).catch(() => {});
  }, [token]);

  useEffect(() => {
    if (!subjectId) { setTopics([]); return; }
    listTopics(token, { subjectId, includeInactive: false }).then(setTopics).catch(() => {});
  }, [subjectId, token]);

  useEffect(() => {
    if (!isEdit) return;
    getPracticeTestTemplateAdmin(token, templateId).then((t) => {
      setTitle(t.title);
      setDescription(t.description || "");
      setDifficulty(t.difficulty || "");
      setQuestionCount(t.questionCount);
      setDurationMinutes(t.durationMinutes);
      setNegativeMarkingRatio(t.negativeMarkingRatio);
    }).catch((err) => setError(friendlyError(err)));
  }, [isEdit, templateId, token]);

  function handleAddRef(ref) {
    setRefs((prev) => [...prev, ref]);
  }
  function handleRemoveRef(index) {
    setRefs((prev) => prev.filter((_, i) => i !== index));
  }

  async function handleSubmit(e) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      const payload = {
        title, description: description || null,
        subjectId: subjectId || null, topicId: topicId || null, examId: examId || null,
        difficulty: difficulty || null,
        questionCount: Number(questionCount),
        durationMinutes: Number(durationMinutes),
        negativeMarkingRatio: Number(negativeMarkingRatio),
        isRandomOrder,
        status: "Draft",
        questions: refs,
      };

      if (isEdit) await updatePracticeTestTemplate(token, templateId, payload);
      else await createPracticeTestTemplate(token, payload);

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
        title={isEdit ? "Edit Practice Test Template" : "Create Practice Test Template"}
        action={<Button variant="ghost" onClick={onCancel}><X className="h-4 w-4" strokeWidth={2.5} />Cancel</Button>}
      />

      <div className="grid grid-cols-1 gap-4 p-6 lg:grid-cols-2">
        <Card>
          <form onSubmit={handleSubmit} className="flex flex-col gap-3">
            <FormField label="Title"><TextInput required value={title} onChange={(e) => setTitle(e.target.value)} /></FormField>
            <FormField label="Description (optional)"><TextInput value={description} onChange={(e) => setDescription(e.target.value)} /></FormField>

            <p className="mt-1 text-xs font-semibold text-ink-600">
              Filters (used when no fixed questions are selected — the pool is generated fresh from these on every attempt)
            </p>
            <div className="grid grid-cols-2 gap-3">
              <FormField label="Subject">
                <Select value={subjectId} onChange={(e) => { setSubjectId(e.target.value); setTopicId(""); }}>
                  <option value="">Any subject</option>
                  {subjects.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                </Select>
              </FormField>
              <FormField label="Topic">
                <Select value={topicId} onChange={(e) => setTopicId(e.target.value)} disabled={!subjectId}>
                  <option value="">Any topic</option>
                  {topics.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                </Select>
              </FormField>
              <FormField label="Exam">
                <Select value={examId} onChange={(e) => setExamId(e.target.value)}>
                  <option value="">Any exam</option>
                  {exams.map((e) => <option key={e.id} value={e.id}>{e.name}</option>)}
                </Select>
              </FormField>
              <FormField label="Difficulty">
                <Select value={difficulty} onChange={(e) => setDifficulty(e.target.value)}>
                  <option value="">Any difficulty</option>
                  <option value="Easy">Easy</option>
                  <option value="Medium">Medium</option>
                  <option value="Hard">Hard</option>
                </Select>
              </FormField>
            </div>

            <div className="grid grid-cols-3 gap-3">
              <FormField label="Questions"><TextInput type="number" required value={questionCount} onChange={(e) => setQuestionCount(e.target.value)} /></FormField>
              <FormField label="Duration (min)"><TextInput type="number" required value={durationMinutes} onChange={(e) => setDurationMinutes(e.target.value)} /></FormField>
              <FormField label="Negative Marking"><TextInput type="number" step="0.05" min="0" max="1" value={negativeMarkingRatio} onChange={(e) => setNegativeMarkingRatio(e.target.value)} /></FormField>
            </div>

            <label className="flex items-center gap-2 text-xs font-semibold text-ink-600">
              <input type="checkbox" checked={isRandomOrder} onChange={(e) => setIsRandomOrder(e.target.checked)} />
              Randomize question order
            </label>

            {error && <Alert>{error}</Alert>}
            <Button type="submit" isLoading={saving}>{isEdit ? "Save changes" : "Create Template"}</Button>
          </form>
        </Card>

        <Card>
          <h3 className="text-sm font-bold text-ink-900">Fixed Questions (optional)</h3>
          <p className="mt-0.5 text-xs text-ink-400">
            Leave empty for a filter-based template (pool generated fresh each attempt). Add specific questions here to make it a Curated template instead.
          </p>

          {refs.length > 0 && (
            <div className="mt-3 flex flex-col divide-y divide-primary-50 border-b border-primary-50 pb-2">
              {refs.map((r, i) => (
                <div key={i} className="flex items-center justify-between gap-2 py-1.5 text-xs text-ink-600">
                  <span>Question #{i + 1} selected</span>
                  <button type="button" onClick={() => handleRemoveRef(i)} className="text-red-500"><Trash2 className="h-3.5 w-3.5" strokeWidth={2.25} /></button>
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
