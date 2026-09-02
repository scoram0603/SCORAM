import { useEffect, useState } from "react";
import { Plus, Pencil, Ban, CheckCircle2, Trash2, X } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  listAdminOrganizations, createOrganization, updateOrganization,
  setOrganizationBlocked, deleteOrganization,
} from "../api/organizations";
import { PageHeader, Card, Button, FormField, TextInput, Alert, friendlyError } from "../components/AdminUI";
import { API_BASE_URL } from "../../api/client";

function logoSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

// ORGANIZATION HIERARCHY -- Manage Organizations (SSC, RRB, UPSC, ...), the body that sits above
// individual exams (SSC CGL, SSC JE, ... all under SSC). Deliberately the same page structure as
// Manage Exams (ExamManagement.jsx) since it's the exact same set of admin actions for the exact
// same reasons -- rename/re-logo, block (hides the organization AND every exam under it from
// students, without touching each exam's own IsBlocked flag), and delete (SuperAdmin only, and
// only when genuinely empty -- see OrganizationsController.Delete). Assigning which exams belong
// to an organization happens from the exam's own side, in Manage Exams' create/edit forms.
export default function OrganizationManagement() {
  const { token, isSuperAdmin } = useAdminAuth();
  const [organizations, setOrganizations] = useState([]);
  const [status, setStatus] = useState("loading");
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [editingOrg, setEditingOrg] = useState(null);

  useEffect(() => {
    refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function refresh() {
    setStatus("loading");
    listAdminOrganizations(token)
      .then((data) => {
        setOrganizations(data);
        setStatus("ready");
      })
      .catch(() => setStatus("error"));
  }

  async function handleToggleBlock(org) {
    setOrganizations((prev) => prev.map((o) => (o.id === org.id ? { ...o, isBlocked: !o.isBlocked } : o)));
    try {
      await setOrganizationBlocked(token, org.id, !org.isBlocked);
    } catch {
      refresh();
    }
  }

  async function handleDelete(org) {
    if (!window.confirm(`Delete "${org.name}"? This can't be undone.`)) return;
    try {
      await deleteOrganization(token, org.id);
      setOrganizations((prev) => prev.filter((o) => o.id !== org.id));
    } catch (err) {
      window.alert(friendlyError(err));
    }
  }

  return (
    <div>
      <PageHeader
        title="Manage Organizations"
        subtitle="SSC, RRB, UPSC, and so on -- each runs several of its own exams. Assign an exam to one from Manage Exams."
        action={
          <Button variant="secondary" onClick={() => setShowCreateForm((s) => !s)}>
            <Plus className="h-4 w-4" strokeWidth={2.5} />
            New Organization
          </Button>
        }
      />

      <div className="p-6">
        {showCreateForm && (
          <div className="mb-4">
            <CreateOrganizationForm
              token={token}
              onDone={() => {
                setShowCreateForm(false);
                refresh();
              }}
              onCancel={() => setShowCreateForm(false)}
            />
          </div>
        )}

        {editingOrg && (
          <div className="mb-4">
            <EditOrganizationForm
              token={token}
              organization={editingOrg}
              onDone={(updated) => {
                setOrganizations((prev) => prev.map((o) => (o.id === updated.id ? updated : o)));
                setEditingOrg(null);
              }}
              onCancel={() => setEditingOrg(null)}
            />
          </div>
        )}

        {status === "loading" && <p className="text-sm text-ink-400">Loading organizations…</p>}
        {status === "error" && <Alert>Couldn't load organizations right now.</Alert>}

        {status === "ready" && (
          <div className="flex flex-col gap-3">
            {organizations.length === 0 && (
              <p className="text-sm text-ink-400">No organizations yet -- create one to start mapping exams to it.</p>
            )}
            {organizations.map((org) => (
              <Card key={org.id} className="flex flex-wrap items-center justify-between gap-4">
                <div className="flex min-w-0 flex-1 items-center gap-3">
                  {logoSrc(org.logoUrl) ? (
                    <img src={logoSrc(org.logoUrl)} alt="" className="h-10 w-10 rounded-full object-cover" />
                  ) : (
                    <span className="flex h-10 w-10 items-center justify-center rounded-full bg-primary-50 text-xs font-bold text-primary-600">
                      {org.name.slice(0, 2).toUpperCase()}
                    </span>
                  )}
                  <div className="min-w-0">
                    <div className="flex items-center gap-1.5">
                      <span className="truncate text-sm font-bold text-ink-900">{org.name}</span>
                      {org.isBlocked && (
                        <span className="rounded-full bg-red-50 px-2 py-0.5 text-[10px] font-semibold text-red-600">Blocked</span>
                      )}
                    </div>
                    <p className="text-xs text-ink-400">{org.examCount} {org.examCount === 1 ? "exam" : "exams"}</p>
                  </div>
                </div>

                <div className="flex gap-2">
                  <Button variant="ghost" onClick={() => setEditingOrg(org)}>
                    <Pencil className="h-4 w-4" strokeWidth={2.25} />
                  </Button>
                  <Button variant={org.isBlocked ? "secondary" : "danger"} onClick={() => handleToggleBlock(org)}>
                    {org.isBlocked ? <CheckCircle2 className="h-4 w-4" strokeWidth={2.25} /> : <Ban className="h-4 w-4" strokeWidth={2.25} />}
                    {org.isBlocked ? "Unblock" : "Block"}
                  </Button>
                  {isSuperAdmin && (
                    <Button variant="danger" onClick={() => handleDelete(org)}>
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

function CreateOrganizationForm({ token, onDone, onCancel }) {
  const [name, setName] = useState("");
  const [logoFile, setLogoFile] = useState(null);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    setSending(true);
    setError(null);
    try {
      await createOrganization(token, { name: name.trim(), logoFile });
      onDone();
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setSending(false);
    }
  }

  return (
    <Card className="mx-auto max-w-lg">
      <form onSubmit={handleSubmit} className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-bold text-ink-900">New Organization</h3>
          <button type="button" onClick={onCancel} className="text-ink-400 hover:text-ink-600">
            <X className="h-4 w-4" strokeWidth={2.25} />
          </button>
        </div>
        <FormField label="Organization name">
          <TextInput required value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. SSC" autoFocus />
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

function EditOrganizationForm({ token, organization, onDone, onCancel }) {
  const [name, setName] = useState(organization.name);
  const [logoFile, setLogoFile] = useState(null);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    setSending(true);
    setError(null);
    try {
      const updated = await updateOrganization(token, organization.id, { name: name.trim(), logoFile });
      onDone(updated);
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setSending(false);
    }
  }

  return (
    <Card className="mx-auto max-w-lg">
      <form onSubmit={handleSubmit} className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-bold text-ink-900">Edit "{organization.name}"</h3>
          <button type="button" onClick={onCancel} className="text-ink-400 hover:text-ink-600">
            <X className="h-4 w-4" strokeWidth={2.25} />
          </button>
        </div>
        <FormField label="Organization name">
          <TextInput required value={name} onChange={(e) => setName(e.target.value)} autoFocus />
        </FormField>
        <FormField label="Replace logo (optional)" hint={organization.logoUrl ? "Leave empty to keep the current logo." : undefined}>
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
