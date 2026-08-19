import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, Plus, Power } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { listSubjects, createSubject, toggleSubjectActive, listTopics, createTopic, toggleTopicActive } from "../api/questionBank";
import { PageHeader, Card, Button, FormField, TextInput, Select, Alert, friendlyError } from "../components/AdminUI";

// Spec section 7: "Manage Subject" / "Manage Topic" -- Topics are scoped to a Subject (the Topic
// dropdown everywhere else in the Question Bank depends on the chosen Subject). "Removing" either
// just retires it (IsActive=false) rather than a hard delete, so existing questions tagged with a
// retired Subject/Topic keep working -- it simply stops showing up as a NEW option going forward.
export default function QuestionBankSubjectsTopics() {
  const { token } = useAdminAuth();
  const navigate = useNavigate();

  const [subjects, setSubjects] = useState([]);
  const [newSubjectName, setNewSubjectName] = useState("");
  const [subjectError, setSubjectError] = useState(null);
  const [subjectBusy, setSubjectBusy] = useState(false);

  const [selectedSubjectId, setSelectedSubjectId] = useState("");
  const [topics, setTopics] = useState([]);
  const [newTopicName, setNewTopicName] = useState("");
  const [topicError, setTopicError] = useState(null);
  const [topicBusy, setTopicBusy] = useState(false);

  useEffect(() => {
    refreshSubjects();
  }, [token]);

  useEffect(() => {
    if (!selectedSubjectId) {
      setTopics([]);
      return;
    }
    refreshTopics();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedSubjectId, token]);

  function refreshSubjects() {
    listSubjects(token, true).then(setSubjects).catch(() => {});
  }
  function refreshTopics() {
    listTopics(token, { subjectId: selectedSubjectId, includeInactive: true }).then(setTopics).catch(() => {});
  }

  async function handleAddSubject(e) {
    e.preventDefault();
    if (!newSubjectName.trim()) return;
    setSubjectBusy(true);
    setSubjectError(null);
    try {
      await createSubject(token, newSubjectName.trim());
      setNewSubjectName("");
      refreshSubjects();
    } catch (err) {
      setSubjectError(friendlyError(err));
    } finally {
      setSubjectBusy(false);
    }
  }

  async function handleToggleSubject(id) {
    try {
      await toggleSubjectActive(token, id);
      refreshSubjects();
    } catch (err) {
      window.alert(friendlyError(err));
    }
  }

  async function handleAddTopic(e) {
    e.preventDefault();
    if (!newTopicName.trim() || !selectedSubjectId) return;
    setTopicBusy(true);
    setTopicError(null);
    try {
      await createTopic(token, selectedSubjectId, newTopicName.trim());
      setNewTopicName("");
      refreshTopics();
    } catch (err) {
      setTopicError(friendlyError(err));
    } finally {
      setTopicBusy(false);
    }
  }

  async function handleToggleTopic(id) {
    try {
      await toggleTopicActive(token, id);
      refreshTopics();
    } catch (err) {
      window.alert(friendlyError(err));
    }
  }

  return (
    <div>
      <PageHeader
        title="Subjects & Topics"
        subtitle="Question Bank master data"
        action={
          <Button variant="ghost" onClick={() => navigate("/admin/question-bank")}>
            <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
            Back to Question Bank
          </Button>
        }
      />

      <div className="grid grid-cols-1 gap-4 p-6 lg:grid-cols-2">
        <Card>
          <h2 className="text-sm font-bold text-ink-900">Subjects</h2>
          <form onSubmit={handleAddSubject} className="mt-3 flex items-end gap-2">
            <div className="flex-1">
              <FormField label="New subject">
                <TextInput value={newSubjectName} onChange={(e) => setNewSubjectName(e.target.value)} placeholder="e.g. Ancient History" />
              </FormField>
            </div>
            <Button type="submit" isLoading={subjectBusy}>
              <Plus className="h-4 w-4" strokeWidth={2.5} />
              Add
            </Button>
          </form>
          {subjectError && <div className="mt-2"><Alert>{subjectError}</Alert></div>}

          <div className="mt-4 flex flex-col divide-y divide-primary-50">
            {subjects.map((s) => (
              <button
                key={s.id}
                type="button"
                onClick={() => setSelectedSubjectId(s.id)}
                className={`flex items-center justify-between gap-2 py-2.5 text-left text-sm ${s.id === selectedSubjectId ? "text-secondary-500" : "text-ink-900"} ${s.isActive ? "" : "opacity-50"}`}
              >
                <span className="font-medium">{s.name} <span className="text-xs font-normal text-ink-400">({s.questionCount})</span></span>
                <span
                  role="button"
                  tabIndex={0}
                  onClick={(e) => { e.stopPropagation(); handleToggleSubject(s.id); }}
                  className="flex items-center gap-1 text-xs font-semibold text-ink-400 hover:text-primary-600"
                  title={s.isActive ? "Retire this subject" : "Reactivate this subject"}
                >
                  <Power className="h-3.5 w-3.5" strokeWidth={2.25} />
                  {s.isActive ? "Retire" : "Reactivate"}
                </span>
              </button>
            ))}
            {subjects.length === 0 && <p className="py-4 text-center text-xs text-ink-400">No subjects yet.</p>}
          </div>
        </Card>

        <Card>
          <h2 className="text-sm font-bold text-ink-900">Topics</h2>
          <div className="mt-3">
            <FormField label="Subject">
              <Select value={selectedSubjectId} onChange={(e) => setSelectedSubjectId(e.target.value)}>
                <option value="">Select a subject to manage its topics</option>
                {subjects.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </Select>
            </FormField>
          </div>

          {selectedSubjectId && (
            <>
              <form onSubmit={handleAddTopic} className="mt-3 flex items-end gap-2">
                <div className="flex-1">
                  <FormField label="New topic">
                    <TextInput value={newTopicName} onChange={(e) => setNewTopicName(e.target.value)} placeholder="e.g. Stone Age" />
                  </FormField>
                </div>
                <Button type="submit" isLoading={topicBusy}>
                  <Plus className="h-4 w-4" strokeWidth={2.5} />
                  Add
                </Button>
              </form>
              {topicError && <div className="mt-2"><Alert>{topicError}</Alert></div>}

              <div className="mt-4 flex flex-col divide-y divide-primary-50">
                {topics.map((t) => (
                  <div key={t.id} className={`flex items-center justify-between gap-2 py-2.5 text-sm text-ink-900 ${t.isActive ? "" : "opacity-50"}`}>
                    <span className="font-medium">{t.name} <span className="text-xs font-normal text-ink-400">({t.questionCount})</span></span>
                    <button
                      type="button"
                      onClick={() => handleToggleTopic(t.id)}
                      className="flex items-center gap-1 text-xs font-semibold text-ink-400 hover:text-primary-600"
                    >
                      <Power className="h-3.5 w-3.5" strokeWidth={2.25} />
                      {t.isActive ? "Retire" : "Reactivate"}
                    </button>
                  </div>
                ))}
                {topics.length === 0 && <p className="py-4 text-center text-xs text-ink-400">No topics under this subject yet.</p>}
              </div>
            </>
          )}
        </Card>
      </div>
    </div>
  );
}
