import { useEffect, useState } from "react";
import { Plus, Pencil, Ban, CheckCircle2, Trash2, X } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { listAdminExams, createExam, updateExam, setExamBlocked, deleteExam } from "../api/exams";
import { PageHeader, Card, Button, FormField, TextInput, Alert, friendlyError } from "../components/AdminUI";
import { API_BASE_URL } from "../../api/client";

function logoSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

// ADMIN EXAM MANAGEMENT -- rename/re-logo, block (hide from students without deleting anything),
// and delete (SuperAdmin only, and only when the server confirms the exam is genuinely empty --
// see ExamsController.Delete). This is the one place all three actions live; exam *creation* still
// also happens inline from the PYQ/Question Bank upload wizards for convenience.
export default function ExamManagement() {
  const { token, isSuperAdmin } = useAdminAuth();
  const [exams, setExams] = useState([]);
  const [status, setStatus] = useState("loading");
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [editingExam, setEditingExam] = useState(null);

  useEffect(() => {
    refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function refresh() {
    setStatus("loading");
    listAdminExams(token)
      .then((data) => {
        setExams(data);
        setStatus("ready");
      })
      .catch(() => setStatus("error"));
  }

  async function handleToggleBlock(exam) {
    setExams((prev) => prev.map((e) => (e.id === exam.id ? { ...e, isBlocked: !e.isBlocked } : e)));
    try {
      await setExamBlocked(token, exam.id, !exam.isBlocked);
    } catch {
      refresh();
    }
  }

  async function handleDelete(exam) {
    if (!window.confirm(`Delete "${exam.name}"? This can't be undone.`)) return;
    try {
      await deleteExam(token, exam.id);
      setExams((prev) => prev.filter((e) => e.id !== exam.id));
    } catch (err) {
      window.alert(friendlyError(err));
    }
  }

  return (
    <div>
      <PageHeader
        title="Manage Exams"
        subtitle="Rename, re-logo, block, or delete exams. Blocking hides an exam from students without deleting anything."
        action={
          <Button variant="secondary" onClick={() => setShowCreateForm((s) => !s)}>
            <Plus className="h-4 w-4" strokeWidth={2.5} />
            New Exam
          </Button>
        }
      />

      <div className="p-6">
        {showCreateForm && (
          <div className="mb-4">
            <CreateExamForm
              token={token}
              onDone={() => {
                setShowCreateForm(false);
                refresh();
              }}
              onCancel={() => setShowCreateForm(false)}
            />
          </div>
        )}

        {editingExam && (
          <div className="mb-4">
            <EditExamForm
              token={token}
              exam={editingExam}
              onDone={(updated) => {
                setExams((prev) => prev.map((e) => (e.id === updated.id ? updated : e)));
                setEditingExam(null);
              }}
              onCancel={() => setEditingExam(null)}
            />
          </div>
        )}

        {status === "loading" && <p className="text-sm text-ink-400">Loading exams…</p>}
        {status === "error" && <Alert>Couldn't load exams right now.</Alert>}

        {status === "ready" && (
          <div className="flex flex-col gap-3">
            {exams.map((exam) => (
              <Card key={exam.id} className="flex flex-wrap items-center justify-between gap-4">
                <div className="flex min-w-0 flex-1 items-center gap-3">
                  {logoSrc(exam.logoUrl) ? (
                    <img src={logoSrc(exam.logoUrl)} alt="" className="h-10 w-10 rounded-full object-cover" />
                  ) : (
                    <span className="flex h-10 w-10 items-center justify-center rounded-full bg-primary-50 text-xs font-bold text-primary-600">
                      {exam.name.slice(0, 2).toUpperCase()}
                    </span>
                  )}
                  <div className="min-w-0">
                    <div className="flex items-center gap-1.5">
                      <span className="truncate text-sm font-bold text-ink-900">{exam.name}</span>
                      {exam.isBlocked && (
                        <span className="rounded-full bg-red-50 px-2 py-0.5 text-[10px] font-semibold text-red-600">Blocked</span>
                      )}
                    </div>
                    <p className="text-xs text-ink-400">{exam.questionCount} questions</p>
                  </div>
                </div>

                <div className="flex gap-2">
                  <Button variant="ghost" onClick={() => setEditingExam(exam)}>
                    <Pencil className="h-4 w-4" strokeWidth={2.25} />
                  </Button>
                  <Button variant={exam.isBlocked ? "secondary" : "danger"} onClick={() => handleToggleBlock(exam)}>
                    {exam.isBlocked ? <CheckCircle2 className="h-4 w-4" strokeWidth={2.25} /> : <Ban className="h-4 w-4" strokeWidth={2.25} />}
                    {exam.isBlocked ? "Unblock" : "Block"}
                  </Button>
                  {isSuperAdmin && (
                    <Button variant="danger" onClick={() => handleDelete(exam)}>
                      <Trash2 className="h-4 w-4" strokeWidth={2.25} />
                    </Button>
                  )}
                </div>
              </Card>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function CreateExamForm({ token, onDone, onCancel }) {
  const [name, setName] = useState("");
  const [logoFile, setLogoFile] = useState(null);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    setSending(true);
    setError(null);
    try {
      await createExam(token, { name: name.trim(), logoFile });
      onDone();
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setSending(false);
    }
  }

  return (
    <Card className="max-w-lg">
      <form onSubmit={handleSubmit} className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-bold text-ink-900">New Exam</h3>
          <button type="button" onClick={onCancel} className="text-ink-400 hover:text-ink-600">
            <X className="h-4 w-4" strokeWidth={2.25} />
          </button>
        </div>
        <FormField label="Exam name">
          <TextInput required value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. SSC CGL" autoFocus />
        </FormField>
        <FormField label="Logo (optional)">
          <input type="file" accept="image/*" onChange={(e) => setLogoFile(e.target.files?.[0] || null)} className="text-sm" />
        </FormField>
        {error && <Alert>{error}</Alert>}
        <div className="flex gap-2">
          <Button type="submit" isLoading={sending}>Create</Button>
          <Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button>
        </div>
      </form>
    </Card>
  );
}

function EditExamForm({ token, exam, onDone, onCancel }) {
  const [name, setName] = useState(exam.name);
  const [logoFile, setLogoFile] = useState(null);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    setSending(true);
    setError(null);
    try {
      const updated = await updateExam(token, exam.id, { name: name.trim(), logoFile });
      onDone(updated);
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setSending(false);
    }
  }

  return (
    <Card className="max-w-lg">
      <form onSubmit={handleSubmit} className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-bold text-ink-900">Edit "{exam.name}"</h3>
          <button type="button" onClick={onCancel} className="text-ink-400 hover:text-ink-600">
            <X className="h-4 w-4" strokeWidth={2.25} />
          </button>
        </div>
        <FormField label="Exam name">
          <TextInput required value={name} onChange={(e) => setName(e.target.value)} autoFocus />
        </FormField>
        <FormField label="Replace logo (optional)" hint={exam.logoUrl ? "Leave empty to keep the current logo." : undefined}>
          <input type="file" accept="image/*" onChange={(e) => setLogoFile(e.target.files?.[0] || null)} className="text-sm" />
        </FormField>
        {error && <Alert>{error}</Alert>}
        <div className="flex gap-2">
          <Button type="submit" isLoading={sending}>Save</Button>
          <Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button>
        </div>
      </form>
    </Card>
  );
}
