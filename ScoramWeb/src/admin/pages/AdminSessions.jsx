import { useState } from "react";
import { LogOut, MonitorSmartphone } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { forceLogoutAdmin, listAdminSessions, revokeAdminSession } from "../api/platform";
import { Alert, Button, Card, PageHeader } from "../components/AdminUI";
import ConfirmDialog from "../components/ConfirmDialog";
import { useLoader } from "../components/analytics";

const when = (iso) => new Date(iso).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" });

export default function AdminSessions() {
  const { token } = useAdminAuth();
  const [tick, setTick] = useState(0);
  const q = useLoader(() => listAdminSessions(token), [token, tick]);
  const [confirm, setConfirm] = useState(null); // { kind: 'session'|'all', row }
  const [message, setMessage] = useState(null);

  return (
    <div>
      <PageHeader title="Admin Sessions" subtitle="Where admin accounts are signed in right now. Super Admin only." />
      <div className="flex flex-col gap-3 p-4 sm:p-6">
        {message && <Alert type="success">{message}</Alert>}
        {q.error && <Alert>{q.error}</Alert>}
        {q.isLoading && !q.data && <div className="h-32 animate-pulse rounded-xl2 bg-white shadow-card" aria-busy="true" />}
        {q.data?.length === 0 && <Card><p className="text-sm text-ink-400">No active admin sessions.</p></Card>}
        {q.data?.map((s) => (
          <Card key={s.id} className="flex flex-wrap items-center justify-between gap-3 !p-4">
            <div className="flex min-w-0 items-start gap-3">
              <MonitorSmartphone className="mt-0.5 h-5 w-5 shrink-0 text-primary-500" />
              <div className="min-w-0">
                <p className="text-sm font-bold text-ink-900">{s.adminName} {s.adminBusinessId && <span className="text-xs font-normal text-ink-400">{s.adminBusinessId}</span>} {s.isYou && <span className="ml-1 rounded-full bg-mint-50 px-2 py-0.5 text-[10px] font-bold text-mint-500">You</span>}</p>
                <p className="text-xs text-ink-600">{s.role} · {s.device}{s.ip ? ` · ${s.ip}` : ""}</p>
                <p className="text-[11px] text-ink-400">Signed in {when(s.signedInAt)} · renews until {when(s.expiresAt)}</p>
              </div>
            </div>
            <div className="flex gap-2">
              <Button variant="secondary" className="!py-1.5 text-xs" onClick={() => setConfirm({ kind: "session", row: s })}>Revoke session</Button>
              {!s.isYou && <Button variant="danger" className="!py-1.5 text-xs" onClick={() => setConfirm({ kind: "all", row: s })}><LogOut className="h-3.5 w-3.5" /> Force logout</Button>}
            </div>
          </Card>
        ))}
      </div>

      <ConfirmDialog
        open={Boolean(confirm)} danger
        title={confirm?.kind === "all" ? `Force ${confirm?.row.adminName} to log out everywhere?` : "Revoke this session?"}
        message={confirm?.kind === "all"
          ? "Every session and access token for this admin is invalidated immediately. They must sign in again."
          : "This device can no longer renew its sign-in. Its current access token expires on its own within minutes."}
        confirmLabel={confirm?.kind === "all" ? "Force logout" : "Revoke"}
        onCancel={() => setConfirm(null)}
        onConfirm={async () => {
          if (confirm.kind === "all") await forceLogoutAdmin(token, confirm.row.adminId);
          else await revokeAdminSession(token, confirm.row.id);
          setConfirm(null); setMessage("Done."); setTick((t) => t + 1);
        }}
      />
    </div>
  );
}
