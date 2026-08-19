import { useEffect, useState } from "react";
import { Plus, Calendar, User } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { listMyTasks, listAllTasks, createTask, updateTaskStatus } from "../api/adminTasks";
import { listAdmins } from "../api/adminAuth";
import { PageHeader, Card, Button, FormField, TextInput, TextArea, Select, Alert, StatusBadge } from "../components/AdminUI";

const STATUSES = ["Pending", "InProgress", "Completed"];

function formatDate(iso) {
  if (!iso) return null;
  return new Date(iso).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });
}

export default function TaskManagement() {
  const { token, isSuperAdmin } = useAdminAuth();
  const [view, setView] = useState(isSuperAdmin ? "all" : "mine"); // "mine" | "all"
  const [tasks, setTasks] = useState([]);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState(null);
  const [showAssignForm, setShowAssignForm] = useState(false);
  const [admins, setAdmins] = useState([]);

  useEffect(() => {
    refresh();
    if (isSuperAdmin) {
      listAdmins(token).then(setAdmins).catch(() => setAdmins([]));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [view]);

  function refresh() {
    setIsLoading(true);
    setLoadError(null);
    const fetcher = view === "all" ? listAllTasks(token) : listMyTasks(token);
    fetcher
      .then(setTasks)
      .catch((err) => setLoadError(err.message))
      .finally(() => setIsLoading(false));
  }

  async function handleStatusChange(taskId, status) {
    // Optimistic update -- task lists can get long and a full refetch per click feels sluggish.
    setTasks((prev) => prev.map((t) => (t.id === taskId ? { ...t, status } : t)));
    try {
      await updateTaskStatus(token, taskId, status);
    } catch {
      refresh(); // roll back to server truth if the update failed
    }
  }

  return (
    <div>
      <PageHeader
        title="Tasks"
        subtitle={isSuperAdmin ? "Assign and track tasks across your admin team." : "Tasks assigned to you."}
        action={
          isSuperAdmin && (
            <Button onClick={() => setShowAssignForm((s) => !s)}>
              <Plus className="h-4 w-4" strokeWidth={2.5} />
              Assign task
            </Button>
          )
        }
      />

      <div className="p-6">
        {isSuperAdmin && (
          <div className="mb-4 flex gap-2">
            <TabButton active={view === "all"} onClick={() => setView("all")}>All tasks</TabButton>
            <TabButton active={view === "mine"} onClick={() => setView("mine")}>My tasks</TabButton>
          </div>
        )}

        {showAssignForm && (
          <div className="mb-6 max-w-lg">
            <AssignTaskForm
              token={token}
              admins={admins}
              onCreated={() => {
                setShowAssignForm(false);
                refresh();
              }}
            />
          </div>
        )}

        {isLoading && <p className="text-sm text-ink-400">Loading tasks…</p>}
        {loadError && <Alert>{loadError}</Alert>}
        {!isLoading && !loadError && tasks.length === 0 && (
          <p className="text-sm text-ink-400">No tasks here yet.</p>
        )}

        <div className="flex flex-col gap-3">
          {tasks.map((task) => (
            <TaskCard
              key={task.id}
              task={task}
              showAssignee={view === "all"}
              onStatusChange={(status) => handleStatusChange(task.id, status)}
            />
          ))}
        </div>
      </div>
    </div>
  );
}

function TabButton({ active, onClick, children }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded-xl2 px-4 py-2 text-sm font-semibold transition-colors ${
        active ? "bg-primary-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"
      }`}
    >
      {children}
    </button>
  );
}

function TaskCard({ task, showAssignee, onStatusChange }) {
  return (
    <Card className="flex flex-wrap items-start justify-between gap-4">
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <h3 className="text-sm font-bold text-ink-900">{task.title}</h3>
          <StatusBadge status={task.status} />
        </div>
        {task.description && <p className="mt-1 text-xs text-ink-600">{task.description}</p>}
        <div className="mt-2 flex flex-wrap gap-3 text-xs text-ink-400">
          {showAssignee && (
            <span className="flex items-center gap-1">
              <User className="h-3.5 w-3.5" strokeWidth={2.25} />
              {task.assignedToAdminName}
            </span>
          )}
          {task.deadline && (
            <span className="flex items-center gap-1">
              <Calendar className="h-3.5 w-3.5" strokeWidth={2.25} />
              Due {formatDate(task.deadline)}
            </span>
          )}
          {task.assignedByAdminName && <span>Assigned by {task.assignedByAdminName}</span>}
        </div>
      </div>

      <Select value={task.status} onChange={(e) => onStatusChange(e.target.value)} className="w-auto">
        {STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
      </Select>
    </Card>
  );
}

function AssignTaskForm({ token, admins, onCreated }) {
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [assignedToAdminId, setAssignedToAdminId] = useState("");
  const [deadline, setDeadline] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    setIsSubmitting(true);
    setError(null);
    try {
      await createTask(token, {
        title,
        description,
        assignedToAdminId,
        deadline: deadline ? new Date(deadline).toISOString() : null,
      });
      onCreated();
    } catch (err) {
      setError(err.message);
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <Card>
      <h3 className="text-sm font-bold text-ink-900">Assign a new task</h3>
      <form onSubmit={handleSubmit} className="mt-4 flex flex-col gap-3">
        <FormField label="Title">
          <TextInput required value={title} onChange={(e) => setTitle(e.target.value)} placeholder="e.g. Upload SSC CGL 2024 Tier 1 PYQs" />
        </FormField>
        <FormField label="Description (optional)">
          <TextArea rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
        </FormField>
        <FormField label="Assign to">
          <Select required value={assignedToAdminId} onChange={(e) => setAssignedToAdminId(e.target.value)}>
            <option value="" disabled>Select an admin</option>
            {admins.map((a) => (
              <option key={a.id} value={a.id}>{a.fullName} ({a.role})</option>
            ))}
          </Select>
        </FormField>
        <FormField label="Deadline (optional)">
          <TextInput type="date" value={deadline} onChange={(e) => setDeadline(e.target.value)} />
        </FormField>

        {error && <Alert>{error}</Alert>}

        <Button type="submit" isLoading={isSubmitting} className="self-start">
          Assign task
        </Button>
      </form>
    </Card>
  );
}
