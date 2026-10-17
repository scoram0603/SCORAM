import { Check, Minus } from "lucide-react";
import { Link } from "react-router-dom";
import { useAdminAuth } from "../context/AdminAuthContext";
import { listAdmins } from "../api/adminAuth";
import { Alert, Card, PageHeader } from "../components/AdminUI";
import { useLoader } from "../components/analytics";
import { PERMISSIONS, PERMISSION_GROUPS, ROLE_PRESETS } from "../lib/permissions";

// Read-only matrix of who holds what. Editing stays in Manage Admins (the server only accepts permission
// changes from a Super Admin, and audits each one). Super Admins implicitly hold every permission.
export default function RolesPermissions() {
  const { token } = useAdminAuth();
  const q = useLoader(() => listAdmins(token), [token]);
  const admins = (q.data || []).filter((a) => a.isActive !== false);

  return (
    <div>
      <PageHeader title="Roles & Permissions" subtitle="Who can do what. Change access from Manage Admins." action={<Link to="/admin/admins" className="rounded-xl2 bg-primary-600 px-4 py-2.5 text-sm font-semibold text-white hover:bg-primary-700">Manage Admins</Link>} />
      <div className="flex flex-col gap-4 p-4 sm:p-6">
        {q.error && <Alert>{q.error}</Alert>}
        <Card className="overflow-x-auto !p-0">
          <table className="w-full min-w-[640px] text-left text-xs">
            <thead className="border-b border-primary-50 text-ink-400">
              <tr><th className="sticky left-0 bg-white px-3 py-3 font-semibold">Permission</th>
                {admins.map((a) => <th key={a.id} className="px-3 py-3 text-center font-semibold" title={a.email}><span className="block max-w-[90px] truncate">{a.fullName}</span><span className="block text-[10px] font-normal">{a.role}</span></th>)}</tr>
            </thead>
            <tbody>
              {q.isLoading && !q.data && <tr><td className="p-4"><div className="h-5 animate-pulse rounded bg-primary-50" /></td></tr>}
              {PERMISSION_GROUPS.map((group) => (
                <GroupRows key={group} group={group} admins={admins} />
              ))}
            </tbody>
          </table>
        </Card>

        <Card>
          <h2 className="text-sm font-extrabold text-ink-900">Suggested starting points</h2>
          <p className="mb-3 text-xs text-ink-400">Available as one-click presets in the permission editor. You can adjust before saving.</p>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {ROLE_PRESETS.map((p) => (
              <div key={p.name} className="rounded-xl2 bg-primary-50/60 p-3">
                <p className="text-sm font-bold text-ink-900">{p.name}</p>
                <p className="mt-1 text-[11px] leading-snug text-ink-600">{p.keys.map((k) => PERMISSIONS.find((x) => x.key === k)?.label || k).join(", ")}</p>
              </div>
            ))}
          </div>
          <p className="mt-3 text-[11px] text-ink-400">Super Admin is a role, not a preset: it has every permission, can manage admins, and is the only role that can change critical app settings.</p>
        </Card>
      </div>
    </div>
  );
}

function GroupRows({ group, admins }) {
  const perms = PERMISSIONS.filter((p) => p.group === group);
  return (
    <>
      <tr><td colSpan={admins.length + 1} className="bg-primary-50/60 px-3 py-1.5 text-[11px] font-bold uppercase tracking-wider text-ink-600">{group}</td></tr>
      {perms.map((p) => (
        <tr key={p.key} className="border-b border-primary-50">
          <td className="sticky left-0 bg-white px-3 py-2"><span className="block font-semibold text-ink-900">{p.label}</span></td>
          {admins.map((a) => {
            const has = a.role === "SuperAdmin" || a.permissions?.includes(p.key);
            return <td key={a.id} className="px-3 py-2 text-center">{has
              ? <Check className="mx-auto h-4 w-4 text-mint-500" aria-label="Granted" /> : <Minus className="mx-auto h-4 w-4 text-ink-400/50" aria-label="Not granted" />}</td>;
          })}
        </tr>
      ))}
    </>
  );
}
