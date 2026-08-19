import { useEffect, useState } from "react";
import { Plus, ShieldCheck, Shield, Settings2 } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { listAdmins, createAdmin, setAdminStatus, setAdminPermissions } from "../api/adminAuth";
import { PageHeader, Card, Button, FormField, TextInput, Select, Alert } from "../components/AdminUI";

const PERMISSIONS = [
  { key: "UploadPaper", label: "Upload Paper", hint: "Create exams and upload new PYQ papers/questions" },
  { key: "EditPaper", label: "Edit Paper", hint: "Edit questions in a Draft paper" },
  { key: "DeletePaper", label: "Delete Paper", hint: "Delete papers and individual questions" },
  { key: "PublishPaper", label: "Publish Paper", hint: "Approve/reject/publish/unpublish -- also skips review when this admin submits their own uploads" },
  { key: "ModerateSolutions", label: "Moderate Solutions", hint: "Approve/reject student-submitted solutions, and post Official/Teacher solutions" },
  { key: "ModerateDiscussions", label: "Moderate Discussions", hint: "Pin comments, mark reported comments resolved/removed, and post official replies" },
  { key: "ManageQuestionBank", label: "Manage Question Bank", hint: "Add/edit/delete Question Bank questions, bulk Excel/JSON import, manage Subjects and Topics" },
  { key: "ModerateQuestionReports", label: "Moderate Question Reports", hint: "Review \"Report Question\" submissions on PYQ and Question Bank questions" },
  { key: "ManageTests", label: "Manage Tests", hint: "Create/edit/publish/schedule Mock Tests and Practice Test templates, view student attempts and results" },
  { key: "Audit", label: "Audit", hint: "View the audit log of admin activity (publishes, rejections, deletions, permission changes)" },
];

export default function ManageAdmins() {
  const { token, admin: currentAdmin } = useAdminAuth();
  const [admins, setAdmins] = useState([]);
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState(null);
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [editingPermissionsId, setEditingPermissionsId] = useState(null);

  useEffect(() => {
    refresh();
  }, []);

  function refresh() {
    setIsLoading(true);
    listAdmins(token)
      .then(setAdmins)
      .catch((err) => setLoadError(err.message))
      .finally(() => setIsLoading(false));
  }

  async function handleToggleStatus(id, nextIsActive) {
    setAdmins((prev) => prev.map((a) => (a.id === id ? { ...a, isActive: nextIsActive } : a)));
    try {
      await setAdminStatus(token, id, nextIsActive);
    } catch {
      refresh();
    }
  }

  return (
    <div>
      <PageHeader
        title="Manage Admins"
        subtitle="Create admin accounts, control access, and grant permissions."
        action={
          <Button onClick={() => setShowCreateForm((s) => !s)}>
            <Plus className="h-4 w-4" strokeWidth={2.5} />
            New admin
          </Button>
        }
      />

      <div className="p-6">
        {showCreateForm && (
          <div className="mb-6 max-w-md">
            <CreateAdminForm
              token={token}
              onCreated={() => {
                setShowCreateForm(false);
                refresh();
              }}
            />
          </div>
        )}

        {isLoading && <p className="text-sm text-ink-400">Loading admins…</p>}
        {loadError && <Alert>{loadError}</Alert>}

        <div className="flex flex-col gap-3">
          {admins.map((a) => (
            <Card key={a.id}>
              <div className="flex flex-wrap items-center justify-between gap-4">
                <div>
                  <div className="flex items-center gap-2">
                    <h3 className="text-sm font-bold text-ink-900">{a.fullName}</h3>
                    <span className="flex items-center gap-1 rounded-full bg-primary-50 px-2 py-0.5 text-xs font-semibold text-primary-600">
                      {a.role === "SuperAdmin" ? <ShieldCheck className="h-3 w-3" strokeWidth={2.5} /> : <Shield className="h-3 w-3" strokeWidth={2.5} />}
                      {a.role}
                    </span>
                    {!a.isActive && (
                      <span className="rounded-full bg-red-50 px-2 py-0.5 text-xs font-semibold text-red-600">Deactivated</span>
                    )}
                  </div>
                  <p className="mt-0.5 text-xs text-ink-400">{a.email}</p>
                  {a.role === "Admin" && (
                    <p className="mt-1 text-xs text-ink-400">
                      {a.permissions.length === 0 ? "No permissions granted yet" : a.permissions.join(", ")}
                    </p>
                  )}
                </div>

                <div className="flex gap-2">
                  {a.role === "Admin" && (
                    <Button variant="secondary" onClick={() => setEditingPermissionsId(editingPermissionsId === a.id ? null : a.id)}>
                      <Settings2 className="h-4 w-4" strokeWidth={2.25} />
                      Permissions
                    </Button>
                  )}
                  <Button
                    variant={a.isActive ? "danger" : "secondary"}
                    onClick={() => handleToggleStatus(a.id, !a.isActive)}
                    disabled={a.id === currentAdmin?.adminId}
                    title={a.id === currentAdmin?.adminId ? "You can't change your own status" : undefined}
                  >
                    {a.isActive ? "Deactivate" : "Activate"}
                  </Button>
                </div>
              </div>

              {editingPermissionsId === a.id && (
                <PermissionsEditor
                  token={token}
                  admin={a}
                  onSaved={(updatedPermissions) => {
                    setAdmins((prev) => prev.map((x) => (x.id === a.id ? { ...x, permissions: updatedPermissions } : x)));
                    setEditingPermissionsId(null);
                  }}
                  onCancel={() => setEditingPermissionsId(null)}
                />
              )}
            </Card>
          ))}
        </div>
      </div>
    </div>
  );
}

function PermissionsEditor({ token, admin, onSaved, onCancel }) {
  const [selected, setSelected] = useState(new Set(admin.permissions));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  function toggle(key) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      const res = await setAdminPermissions(token, admin.id, Array.from(selected));
      onSaved(res.permissions);
    } catch (err) {
      setError(err.message);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="mt-4 border-t border-primary-100 pt-4">
      <p className="mb-3 text-xs font-semibold text-ink-600">
        Permissions for {admin.fullName} — everything starts unchecked until granted.
      </p>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        {PERMISSIONS.map((p) => (
          <label key={p.key} className="flex items-start gap-2.5 rounded-xl2 border border-primary-100 p-3">
            <input
              type="checkbox"
              checked={selected.has(p.key)}
              onChange={() => toggle(p.key)}
              className="mt-0.5 h-4 w-4 accent-primary-600"
            />
            <span>
              <span className="block text-sm font-semibold text-ink-900">{p.label}</span>
              <span className="block text-xs text-ink-400">{p.hint}</span>
            </span>
          </label>
        ))}
      </div>

      {error && <div className="mt-3"><Alert>{error}</Alert></div>}

      <div className="mt-3 flex gap-2">
        <Button isLoading={saving} onClick={handleSave}>Save permissions</Button>
        <Button variant="ghost" onClick={onCancel}>Cancel</Button>
      </div>
    </div>
  );
}

function CreateAdminForm({ token, onCreated }) {
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [role, setRole] = useState("Admin");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    setIsSubmitting(true);
    setError(null);
    try {
      await createAdmin(token, { fullName, email, password, role });
      onCreated();
    } catch (err) {
      setError(err.message);
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <Card>
      <h3 className="text-sm font-bold text-ink-900">Create a new admin account</h3>
      <form onSubmit={handleSubmit} className="mt-4 flex flex-col gap-3">
        <FormField label="Full name">
          <TextInput required value={fullName} onChange={(e) => setFullName(e.target.value)} />
        </FormField>
        <FormField label="Email">
          <TextInput required type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
        </FormField>
        <FormField label="Temporary password" hint="At least 8 characters. Share this with them securely.">
          <TextInput required type="password" minLength={8} value={password} onChange={(e) => setPassword(e.target.value)} />
        </FormField>
        <FormField label="Role">
          <Select value={role} onChange={(e) => setRole(e.target.value)}>
            <option value="Admin">Admin</option>
            <option value="SuperAdmin">Super Admin</option>
          </Select>
        </FormField>

        {error && <Alert>{error}</Alert>}

        <Button type="submit" isLoading={isSubmitting} className="self-start">
          Create admin
        </Button>
      </form>
    </Card>
  );
}
