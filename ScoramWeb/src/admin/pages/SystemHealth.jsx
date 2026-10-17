import { CheckCircle2, CircleSlash, RefreshCw, XCircle, AlertTriangle } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { getSystemHealth } from "../api/platform";
import { Alert, Button, Card, PageHeader } from "../components/AdminUI";
import { UnavailableCard, timeLabel, useLoader } from "../components/analytics";

const STATUS = {
  healthy: { label: "Healthy", cls: "bg-mint-50 text-mint-500", Icon: CheckCircle2 },
  degraded: { label: "Degraded", cls: "bg-accent-50 text-accent-600", Icon: AlertTriangle },
  down: { label: "Down", cls: "bg-red-50 text-red-600", Icon: XCircle },
};

export default function SystemHealth() {
  const { token } = useAdminAuth();
  const q = useLoader(() => getSystemHealth(token), [token]);
  const d = q.data;
  return (
    <div>
      <PageHeader
        title="System Health"
        subtitle="Live checks made when you open this page. Nothing here is cached or estimated."
        action={<Button variant="secondary" isLoading={q.isLoading} onClick={() => q.reload()}>{!q.isLoading && <RefreshCw className="h-4 w-4" />} Re-check</Button>}
      />
      <div className="flex flex-col gap-4 p-4 sm:p-6">
        {q.error && <Alert>{q.error}</Alert>}
        {q.isLoading && !d && <div className="h-32 animate-pulse rounded-xl2 bg-white shadow-card" aria-busy="true" />}
        {d && (
          <>
            <p className="text-xs text-ink-400">Checked at {timeLabel(new Date(d.checkedAt))} · environment: {d.environment}</p>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              {d.services.map((s) => {
                const st = STATUS[s.status] || STATUS.degraded;
                return (
                  <Card key={s.key}>
                    <div className="flex items-center justify-between"><h3 className="text-sm font-extrabold text-ink-900">{s.label}</h3>
                      <span className={`flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold ${st.cls}`}><st.Icon className="h-3.5 w-3.5" />{st.label}</span></div>
                    <p className="mt-2 text-xs text-ink-600">{s.detail}</p>
                  </Card>
                );
              })}
            </div>
            <h2 className="flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-ink-400"><CircleSlash className="h-3.5 w-3.5" /> Not measured</h2>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">{d.unavailable.map((u) => <UnavailableCard key={u.key} item={u} />)}</div>
          </>
        )}
      </div>
    </div>
  );
}
