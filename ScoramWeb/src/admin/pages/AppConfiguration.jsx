import { useMemo, useState } from "react";
import { History, Lock, RotateCcw, Save } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { getAppConfig, getVersionImpact, listConfigRevisions, rollbackConfigRevision, updateAppConfig } from "../api/platform";
import { Alert, Button, Card, PageHeader, TextArea, TextInput, friendlyError } from "../components/AdminUI";
import ConfirmDialog from "../components/ConfirmDialog";
import Pager from "../components/Pager";
import { useLoader } from "../components/analytics";

const GROUP_BLURB = {
  Maintenance: "Temporarily pause the student API. Admins keep access.",
  "Feature flags": "Turn student-facing features on or off. Enforced by the server, not just hidden in the app.",
  "App versions": "Force or recommend app updates. The mobile app checks this each time it opens.",
};
const MAX_PAGE = 10;
const when = (iso) => (iso ? new Date(iso).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" }) : "");

function Toggle({ checked, onChange, disabled, label }) {
  return (
    <button type="button" role="switch" aria-checked={checked} aria-label={label} disabled={disabled} onClick={() => onChange(!checked)}
      className={`relative h-6 w-11 shrink-0 rounded-full transition-colors disabled:opacity-50 ${checked ? "bg-mint-500" : "bg-ink-400/40"}`}>
      <span className={`absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-all ${checked ? "left-[22px]" : "left-0.5"}`} />
    </button>
  );
}

export default function AppConfiguration() {
  const { token } = useAdminAuth();
  const [tab, setTab] = useState("settings");
  return (
    <div>
      <PageHeader title="App Configuration" subtitle="Maintenance mode, feature flags and app version policy. Every change is recorded and can be rolled back." />
      <div className="flex flex-col gap-4 p-4 sm:p-6">
        <div role="tablist" className="flex gap-1 border-b border-primary-100">
          {[["settings", "Settings"], ["history", "Change history"]].map(([k, l]) => (
            <button key={k} role="tab" type="button" aria-selected={tab === k} onClick={() => setTab(k)}
              className={`border-b-2 px-4 py-2.5 text-sm font-semibold ${tab === k ? "border-primary-600 text-primary-600" : "border-transparent text-ink-400 hover:text-ink-600"}`}>{l}</button>
          ))}
        </div>
        {tab === "settings" ? <SettingsTab token={token} /> : <HistoryTab token={token} />}
      </div>
    </div>
  );
}

function SettingsTab({ token }) {
  const cfg = useLoader(() => getAppConfig(token), [token]);
  const [draft, setDraft] = useState({});
  const [note, setNote] = useState("");
  const [confirm, setConfirm] = useState(false);
  const [impact, setImpact] = useState([]);
  const [message, setMessage] = useState(null);

  const data = cfg.data;
  const groups = useMemo(() => {
    const map = new Map();
    (data?.definitions || []).forEach((d) => { if (!map.has(d.group)) map.set(d.group, []); map.get(d.group).push(d); });
    return [...map.entries()];
  }, [data]);

  const value = (k) => (k in draft ? draft[k] : data?.values?.[k] ?? "");
  const changes = Object.fromEntries(Object.entries(draft).filter(([k, v]) => v !== (data?.values?.[k] ?? "")));
  const changedKeys = Object.keys(changes);
  const labelOf = (k) => data.definitions.find((d) => d.key === k)?.label || k;

  async function openConfirm() {
    setMessage(null);
    // For a changed MINIMUM version, show how many known devices would be forced to update.
    const found = [];
    for (const k of changedKeys) {
      const m = /^app\.(android|ios)\.minVersion$/.exec(k);
      if (m && changes[k]) {
        try { found.push({ platform: m[1], ...(await getVersionImpact(token, m[1], changes[k])) }); } catch { /* shown as unknown */ }
      }
    }
    setImpact(found);
    setConfirm(true);
  }

  async function save() {
    await updateAppConfig(token, changes, note.trim() || undefined);
    setConfirm(false);
    setDraft({});
    setNote("");
    setMessage("Saved. Changes reach students within about 30 seconds.");
    cfg.reload();
  }

  if (cfg.error && !data) return <Alert>{cfg.error}</Alert>;
  if (!data) return <div className="h-40 animate-pulse rounded-xl2 bg-white shadow-card" aria-busy="true" />;

  const maintenanceOn = value("maintenance.enabled") === "true";

  return (
    <div className="flex flex-col gap-4">
      {message && <Alert type="success">{message}</Alert>}
      {maintenanceOn && <Alert>Maintenance mode is ON — students currently receive "service unavailable".</Alert>}
      {!data.canEditCritical && (
        <p className="flex items-center gap-2 rounded-xl2 bg-primary-50 px-3 py-2 text-xs text-primary-600"><Lock className="h-3.5 w-3.5" /> Settings marked "Super Admin" can only be changed by a Super Admin.</p>
      )}

      {groups.map(([group, defs]) => (
        <Card key={group}>
          <h2 className="text-sm font-extrabold text-ink-900">{group}</h2>
          {GROUP_BLURB[group] && <p className="mb-3 text-xs text-ink-400">{GROUP_BLURB[group]}</p>}
          <div className="divide-y divide-primary-50">
            {defs.map((d) => {
              const locked = d.superAdminOnly && !data.canEditCritical;
              const last = data.lastChanged?.[d.key];
              return (
                <div key={d.key} className="flex flex-col gap-2 py-3 sm:flex-row sm:items-start sm:justify-between">
                  <div className="min-w-0 sm:max-w-[55%]">
                    <p className="flex items-center gap-1.5 text-sm font-semibold text-ink-900">
                      {d.label}{d.superAdminOnly && <span className="rounded-full bg-accent-50 px-1.5 py-0.5 text-[10px] font-bold text-accent-600">Super Admin</span>}
                    </p>
                    <p className="text-xs text-ink-400">{d.description}</p>
                    {last && <p className="mt-0.5 text-[11px] text-ink-400">Last changed {when(last.updatedAt)}{last.updatedBy ? ` by ${last.updatedBy}` : ""}</p>}
                  </div>
                  <div className="sm:w-[40%]">
                    {d.type === "Bool" ? (
                      <Toggle label={d.label} checked={value(d.key) === "true"} disabled={locked} onChange={(on) => setDraft((p) => ({ ...p, [d.key]: on ? "true" : "false" }))} />
                    ) : d.maxLength > 300 ? (
                      <TextArea rows={2} maxLength={d.maxLength} disabled={locked} value={value(d.key)} onChange={(e) => setDraft((p) => ({ ...p, [d.key]: e.target.value }))} aria-label={d.label} />
                    ) : (
                      <TextInput maxLength={d.maxLength} disabled={locked} value={value(d.key)} placeholder={d.type === "Version" ? "e.g. 1.4.0 (empty = none)" : d.type === "Url" ? "https://…" : ""}
                        onChange={(e) => setDraft((p) => ({ ...p, [d.key]: e.target.value }))} aria-label={d.label} />
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        </Card>
      ))}

      <div className="sticky bottom-3 flex flex-wrap items-center gap-3 rounded-xl2 bg-white p-3 shadow-lg">
        <span className="text-sm text-ink-600">{changedKeys.length ? `${changedKeys.length} unsaved change(s)` : "No unsaved changes"}</span>
        <TextInput className="!w-auto min-w-[200px] flex-1" placeholder="Why? (optional note for the history)" maxLength={200} value={note} onChange={(e) => setNote(e.target.value)} aria-label="Change note" />
        <Button variant="secondary" disabled={!changedKeys.length} onClick={() => setDraft({})}>Discard</Button>
        <Button disabled={!changedKeys.length} onClick={openConfirm}><Save className="h-4 w-4" /> Review & save</Button>
      </div>

      <ConfirmDialog
        open={confirm} danger={changedKeys.some((k) => k === "maintenance.enabled" || /minVersion$/.test(k) || k === "feature.registration")}
        title="Apply these changes?"
        message={
          <div className="space-y-2">
            <ul className="space-y-1">
              {changedKeys.map((k) => (
                <li key={k} className="rounded-lg bg-primary-50 px-2.5 py-1.5 text-xs">
                  <b>{labelOf(k)}</b>: <span className="text-ink-400">{data.values[k] || "(empty)"}</span> → <span className="font-semibold text-ink-900">{changes[k] || "(empty)"}</span>
                </li>
              ))}
            </ul>
            {impact.map((i) => (
              <p key={i.platform} className="rounded-lg bg-accent-50 px-2.5 py-1.5 text-xs text-accent-600">
                {i.platform === "ios" ? "iOS" : "Android"} minimum {i.version}: <b>{i.devicesBelow.toLocaleString("en-IN")}</b> known device(s) would be forced to update
                ({i.devicesAtOrAbove.toLocaleString("en-IN")} already up to date{i.devicesUnknownVersion ? `, ${i.devicesUnknownVersion.toLocaleString("en-IN")} unknown` : ""}). {i.basis}
              </p>
            ))}
          </div>
        }
        confirmLabel="Apply" onCancel={() => setConfirm(false)} onConfirm={async () => { try { await save(); } catch (e) { throw new Error(friendlyError(e)); } }}
      />
    </div>
  );
}

function HistoryTab({ token }) {
  const [page, setPage] = useState(1);
  const [key, setKey] = useState(0);
  const [target, setTarget] = useState(null);
  const [message, setMessage] = useState(null);
  const q = useLoader(() => listConfigRevisions(token, { page, pageSize: MAX_PAGE }), [token, page, key]);

  return (
    <div className="flex flex-col gap-3">
      {message && <Alert type="success">{message}</Alert>}
      {q.error && <Alert>{q.error}</Alert>}
      {q.isLoading && !q.data && <div className="h-40 animate-pulse rounded-xl2 bg-white shadow-card" aria-busy="true" />}
      {q.data?.items?.length === 0 && <Card><p className="text-sm text-ink-400">No changes have been made yet.</p></Card>}
      {q.data?.items?.map((r) => (
        <Card key={r.id} className="flex flex-wrap items-center justify-between gap-3 !p-4">
          <div className="min-w-0">
            <p className="flex items-center gap-1.5 text-sm font-bold text-ink-900"><History className="h-3.5 w-3.5 text-ink-400" />{r.label}</p>
            <p className="mt-0.5 break-words text-xs text-ink-600"><span className="text-ink-400">{r.oldValue || "(empty)"}</span> → <b>{r.newValue || "(empty)"}</b></p>
            <p className="mt-0.5 text-[11px] text-ink-400">{when(r.changedAt)}{r.changedBy ? ` · ${r.changedBy}` : ""}{r.note ? ` · “${r.note}”` : ""}</p>
          </div>
          <Button variant="secondary" className="!py-1.5 text-xs" onClick={() => setTarget(r)}><RotateCcw className="h-3.5 w-3.5" /> Roll back</Button>
        </Card>
      ))}
      {q.data && <Pager page={page} pageSize={MAX_PAGE} totalCount={q.data.totalCount} onPage={setPage} />}

      <ConfirmDialog
        open={Boolean(target)} danger title="Roll back this change?"
        message={target && <>Restores <b>{target.label}</b> to <b>{target.oldValue || "(empty)"}</b>. This is itself recorded as a new change.</>}
        confirmLabel="Roll back" onCancel={() => setTarget(null)}
        onConfirm={async () => {
          try { await rollbackConfigRevision(token, target.id); } catch (e) { throw new Error(friendlyError(e)); }
          setTarget(null); setMessage("Rolled back."); setKey((k) => k + 1);
        }}
      />
    </div>
  );
}
