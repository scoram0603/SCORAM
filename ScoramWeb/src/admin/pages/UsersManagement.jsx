import { useEffect, useState } from "react";
import { BadgeCheck, Search, ShieldOff, UserCheck, X } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  getUser, getUserSessions, listUsers, revokeAllUserSessions, revokeUserSession, setUserStatus,
} from "../api/platform";
import { Alert, Button, Card, PageHeader, Select, TextInput, friendlyError } from "../components/AdminUI";
import ConfirmDialog from "../components/ConfirmDialog";
import Pager from "../components/Pager";

const PAGE_SIZE = 25;
const when = (iso) => (iso ? new Date(iso).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" }) : "Never");

function StatusPill({ active }) {
  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-semibold ${active ? "bg-mint-50 text-mint-500" : "bg-red-50 text-red-600"}`}>
      {active ? "Active" : "Suspended"}
    </span>
  );
}

export default function UsersManagement() {
  const { token } = useAdminAuth();
  const [filters, setFilters] = useState({ search: "", status: "all", sort: "newest" });
  const [applied, setApplied] = useState(filters);
  const [page, setPage] = useState(1);
  const [result, setResult] = useState(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState(null);
  const [selectedId, setSelectedId] = useState(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setIsLoading(true);
    setError(null);
    listUsers(token, { ...applied, page, pageSize: PAGE_SIZE })
      .then((r) => !cancelled && setResult(r))
      .catch((e) => !cancelled && setError(friendlyError(e)))
      .finally(() => !cancelled && setIsLoading(false));
    return () => { cancelled = true; };
  }, [token, applied, page, reloadKey]);

  function submit(e) {
    e.preventDefault();
    setPage(1);
    setApplied(filters);
  }

  return (
    <div>
      <PageHeader title="Students" subtitle="Find a student, review their activity, and suspend or reactivate their account." />
      <div className="flex flex-col gap-4 p-4 sm:p-6">
        <form onSubmit={submit} className="flex flex-wrap items-end gap-2">
          <div className="relative min-w-[220px] flex-1">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" />
            <TextInput className="!pl-9" placeholder="Search name, username, email or phone" value={filters.search}
              onChange={(e) => setFilters({ ...filters, search: e.target.value })} aria-label="Search students" />
          </div>
          <Select className="!w-auto" value={filters.status} onChange={(e) => setFilters({ ...filters, status: e.target.value })} aria-label="Status">
            <option value="all">All statuses</option><option value="active">Active</option><option value="suspended">Suspended</option>
          </Select>
          <Select className="!w-auto" value={filters.sort} onChange={(e) => setFilters({ ...filters, sort: e.target.value })} aria-label="Sort">
            <option value="newest">Newest first</option><option value="oldest">Oldest first</option>
            <option value="lastactive">Recently active</option><option value="name">Name A–Z</option>
          </Select>
          <Button type="submit">Search</Button>
        </form>

        {error && <Alert>{error}</Alert>}

        <Card className="overflow-x-auto !p-0">
          <table className="w-full min-w-[720px] text-left text-sm">
            <thead className="border-b border-primary-50 text-xs text-ink-400">
              <tr><th className="px-4 py-3 font-semibold">Student</th><th className="px-4 py-3 font-semibold">Contact (masked)</th><th className="px-4 py-3 font-semibold">Joined</th><th className="px-4 py-3 font-semibold">Last active</th><th className="px-4 py-3 font-semibold">Status</th></tr>
            </thead>
            <tbody className="divide-y divide-primary-50">
              {isLoading && Array.from({ length: 6 }).map((_, i) => (
                <tr key={i}><td colSpan={5} className="px-4 py-3"><div className="h-5 animate-pulse rounded bg-primary-50" /></td></tr>
              ))}
              {!isLoading && result?.items?.length === 0 && (
                <tr><td colSpan={5} className="px-4 py-10 text-center text-sm text-ink-400">No students match these filters.</td></tr>
              )}
              {!isLoading && result?.items?.map((u) => (
                <tr key={u.id} className="cursor-pointer hover:bg-primary-50/50" onClick={() => setSelectedId(u.id)}>
                  <td className="px-4 py-3">
                    <button type="button" className="text-left" onClick={() => setSelectedId(u.id)}>
                      <span className="block font-semibold text-ink-900">{u.fullName}</span>
                      <span className="block text-xs text-ink-400">@{u.username}</span>
                    </button>
                  </td>
                  <td className="px-4 py-3 text-xs text-ink-600">
                    <span className="block">{u.email}</span>
                    <span className="flex items-center gap-1">{u.phone}{u.phoneVerified && <BadgeCheck className="h-3.5 w-3.5 text-mint-500" aria-label="Phone verified" />}</span>
                  </td>
                  <td className="px-4 py-3 text-xs text-ink-600">{when(u.createdAt)}</td>
                  <td className="px-4 py-3 text-xs text-ink-600">{when(u.lastActiveAt)}</td>
                  <td className="px-4 py-3"><StatusPill active={u.isActive} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
        {result && <Pager page={page} pageSize={PAGE_SIZE} totalCount={result.totalCount} onPage={setPage} />}
      </div>

      {selectedId && (
        <UserDrawer id={selectedId} token={token} onClose={() => setSelectedId(null)} onChanged={() => setReloadKey((k) => k + 1)} />
      )}
    </div>
  );
}

function Stat({ label, value }) {
  return (
    <div className="rounded-xl2 bg-primary-50/60 p-3">
      <p className="text-lg font-extrabold text-ink-900">{value}</p>
      <p className="text-[11px] text-ink-600">{label}</p>
    </div>
  );
}

function UserDrawer({ id, token, onClose, onChanged }) {
  const [user, setUser] = useState(null);
  const [sessions, setSessions] = useState(null);
  const [error, setError] = useState(null);
  const [notice, setNotice] = useState(null);
  const [confirm, setConfirm] = useState(null); // 'suspend' | 'reactivate' | 'signout'
  const [tick, setTick] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setError(null);
    Promise.all([getUser(token, id), getUserSessions(token, id)])
      .then(([u, s]) => { if (!cancelled) { setUser(u); setSessions(s); } })
      .catch((e) => !cancelled && setError(friendlyError(e)));
    return () => { cancelled = true; };
  }, [token, id, tick]);

  useEffect(() => {
    const onKey = (e) => e.key === "Escape" && !confirm && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [confirm, onClose]);

  const refresh = () => { setTick((t) => t + 1); onChanged(); };

  return (
    <div className="fixed inset-0 z-40 flex justify-end" role="dialog" aria-modal="true" aria-label="Student details">
      <button type="button" className="absolute inset-0 bg-black/40" aria-label="Close details" onClick={onClose} />
      <aside className="relative z-10 h-full w-full max-w-lg overflow-y-auto bg-white p-5 shadow-xl">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h2 className="truncate text-lg font-extrabold text-ink-900">{user?.fullName || "Student"}</h2>
            {user && <p className="text-xs text-ink-400">@{user.username}</p>}
          </div>
          <button type="button" onClick={onClose} aria-label="Close" className="rounded-lg p-1.5 text-ink-600 hover:bg-primary-50"><X className="h-5 w-5" /></button>
        </div>

        {error && <div className="mt-4"><Alert>{error}</Alert></div>}
        {notice && <div className="mt-4"><Alert type="success">{notice}</Alert></div>}
        {!user && !error && <div className="mt-6 h-40 animate-pulse rounded-xl2 bg-primary-50" aria-busy="true" />}

        {user && (
          <div className="mt-4 flex flex-col gap-5">
            <div className="flex flex-wrap items-center gap-2">
              <StatusPill active={user.isActive} />
              {user.phoneVerified && <span className="rounded-full bg-mint-50 px-2 py-0.5 text-xs font-semibold text-mint-500">Phone verified</span>}
            </div>

            <dl className="grid grid-cols-[110px_1fr] gap-y-1.5 text-sm">
              <dt className="text-ink-400">Email</dt><dd className="break-all text-ink-900">{user.email}</dd>
              <dt className="text-ink-400">Phone</dt><dd className="text-ink-900">{user.phone}</dd>
              <dt className="text-ink-400">Joined</dt><dd className="text-ink-900">{when(user.createdAt)}</dd>
              <dt className="text-ink-400">Last active</dt><dd className="text-ink-900">{when(user.lastActiveAt)}</dd>
              <dt className="text-ink-400">Exams</dt>
              <dd className="text-ink-900">{user.exams.length ? user.exams.map((e) => e.name + (e.isPrimary ? " (primary)" : "")).join(", ") : "None selected"}</dd>
            </dl>

            <div className="grid grid-cols-3 gap-2">
              <Stat label="Test attempts" value={user.stats.attempts} />
              <Stat label="Completed" value={user.stats.completedAttempts} />
              <Stat label="Feedback sent" value={user.stats.feedbackCount} />
              <Stat label="Reports filed" value={user.stats.questionReportsFiled} />
              <Stat label="Active sessions" value={user.stats.activeSessions} />
              <Stat label="Push devices" value={user.stats.activeDevices} />
            </div>

            <div className="flex flex-wrap gap-2">
              {user.isActive ? (
                <Button variant="danger" onClick={() => setConfirm("suspend")}><ShieldOff className="h-4 w-4" /> Suspend account</Button>
              ) : (
                <Button variant="secondary" onClick={() => setConfirm("reactivate")}><UserCheck className="h-4 w-4" /> Reactivate account</Button>
              )}
              <Button variant="secondary" onClick={() => setConfirm("signout")}>Sign out everywhere</Button>
            </div>

            <section>
              <h3 className="mb-2 text-sm font-extrabold text-ink-900">Sign-in sessions</h3>
              {sessions?.sessions?.length ? (
                <ul className="divide-y divide-primary-50 rounded-xl2 border border-primary-100">
                  {sessions.sessions.map((s) => (
                    <li key={s.id} className="flex items-center justify-between gap-2 p-3 text-xs">
                      <span className="min-w-0"><span className="block font-semibold text-ink-900">{s.device}</span><span className="text-ink-400">{when(s.signedInAt)}{s.ip ? ` · ${s.ip}` : ""}</span></span>
                      <button type="button" className="shrink-0 rounded-lg bg-red-50 px-2.5 py-1 font-semibold text-red-600 hover:bg-red-100"
                        onClick={async () => { try { await revokeUserSession(token, id, s.id); setNotice("Session revoked."); refresh(); } catch (e) { setError(friendlyError(e)); } }}>Revoke</button>
                    </li>
                  ))}
                </ul>
              ) : <p className="text-xs text-ink-400">No active sessions.</p>}
              {sessions?.devices?.length > 0 && (
                <p className="mt-2 text-xs text-ink-600">Push devices: {sessions.devices.map((d) => `${d.platform}${d.appVersion ? ` v${d.appVersion}` : ""}`).join(", ")}</p>
              )}
              {sessions?.note && <p className="mt-2 text-[11px] leading-snug text-ink-400">{sessions.note}</p>}
            </section>

            <section>
              <h3 className="mb-2 text-sm font-extrabold text-ink-900">Status history</h3>
              {user.statusHistory.length === 0 ? <p className="text-xs text-ink-400">No admin actions recorded for this student.</p> : (
                <ul className="space-y-2">
                  {user.statusHistory.map((h) => (
                    <li key={h.id} className="rounded-xl2 bg-primary-50/50 p-3 text-xs">
                      <p className="font-semibold text-ink-900">{h.action.replace(".", " · ")} <span className="font-normal text-ink-400">by {h.by}</span></p>
                      {h.reason && <p className="mt-0.5 text-ink-600">{h.reason}</p>}
                      <p className="text-ink-400">{when(h.at)}</p>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </div>
        )}
      </aside>

      <ConfirmDialog
        open={confirm === "suspend"} danger title="Suspend this student?"
        message="They will be signed out and unable to sign in until you reactivate the account. Their data is kept."
        reasonLabel="Reason for suspending" confirmLabel="Suspend"
        onCancel={() => setConfirm(null)}
        onConfirm={async (reason) => { await setUserStatus(token, id, false, reason); setConfirm(null); setNotice("Account suspended."); refresh(); }}
      />
      <ConfirmDialog
        open={confirm === "reactivate"} title="Reactivate this student?"
        message="They will be able to sign in again."
        reasonLabel="Reason for reactivating" confirmLabel="Reactivate"
        onCancel={() => setConfirm(null)}
        onConfirm={async (reason) => { await setUserStatus(token, id, true, reason); setConfirm(null); setNotice("Account reactivated."); refresh(); }}
      />
      <ConfirmDialog
        open={confirm === "signout"} danger title="Sign this student out everywhere?"
        message="Every active session is revoked immediately. They can sign back in with their password."
        confirmLabel="Sign out everywhere"
        onCancel={() => setConfirm(null)}
        onConfirm={async () => { await revokeAllUserSessions(token, id); setConfirm(null); setNotice("Signed out of every device."); refresh(); }}
      />
    </div>
  );
}
