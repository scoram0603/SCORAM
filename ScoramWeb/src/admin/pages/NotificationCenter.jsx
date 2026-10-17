import { useEffect, useState } from "react";
import { Megaphone, X } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { findNotificationUsers, listCampaigns, previewAudience, sendAnnouncement } from "../api/platform";
import { listAdminExams } from "../api/exams";
import { Alert, Button, Card, FormField, PageHeader, Select, TextArea, TextInput, friendlyError } from "../components/AdminUI";
import ConfirmDialog from "../components/ConfirmDialog";
import Pager from "../components/Pager";
import { useLoader } from "../components/analytics";

const DESTINATIONS = [
  ["Notifications", "Notifications list"], ["Home", "Home"], ["Pyp", "Previous-year papers"], ["MockTests", "Mock tests"],
  ["Quizzes", "Quizzes"], ["Tests", "Practice tests"], ["Progress", "Progress"],
];
const PAGE_SIZE = 10;

export default function NotificationCenter() {
  const { token } = useAdminAuth();
  const [form, setForm] = useState({ title: "", body: "", target: "AllStudents", destination: "Notifications" });
  const [exams, setExams] = useState([]);
  const [examIds, setExamIds] = useState([]);
  const [picked, setPicked] = useState([]); // [{id, username, fullName}]
  const [userQuery, setUserQuery] = useState("");
  const [userHits, setUserHits] = useState([]);
  const [audience, setAudience] = useState({ loading: false, count: null, error: null });
  const [confirm, setConfirm] = useState(false);
  const [result, setResult] = useState(null);
  const [page, setPage] = useState(1);
  const [historyKey, setHistoryKey] = useState(0);
  const history = useLoader(() => listCampaigns(token, { page, pageSize: PAGE_SIZE }), [token, page, historyKey]);

  useEffect(() => {
    listAdminExams(token).then((e) => setExams(e || [])).catch(() => {});
  }, [token]);

  // Debounced student lookup (min 2 chars, server returns at most 10 with no contact details).
  useEffect(() => {
    if (form.target !== "Users" || userQuery.trim().length < 2) { setUserHits([]); return undefined; }
    const t = setTimeout(() => findNotificationUsers(token, userQuery.trim()).then(setUserHits).catch(() => setUserHits([])), 250);
    return () => clearTimeout(t);
  }, [token, form.target, userQuery]);

  // Live audience size so the admin knows how many people they are about to message.
  const userIds = picked.map((u) => u.id);
  const audienceKey = `${form.target}|${examIds.join(",")}|${userIds.join(",")}`;
  useEffect(() => {
    let cancelled = false;
    setAudience({ loading: true, count: null, error: null });
    previewAudience(token, { target: form.target, examIds, userIds })
      .then((r) => !cancelled && setAudience({ loading: false, count: r.recipients, error: null }))
      .catch((e) => !cancelled && setAudience({ loading: false, count: null, error: friendlyError(e) }));
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, audienceKey]);

  const set = (k) => (e) => setForm((f) => ({ ...f, [k]: e.target.value }));
  const targetReady =
    form.target === "AllStudents" || (form.target === "Exams" && examIds.length > 0) || (form.target === "Users" && picked.length > 0);
  const canSend = form.title.trim() && form.body.trim() && targetReady && audience.count !== 0 && !audience.loading;

  async function send() {
    await sendAnnouncement(token, {
      title: form.title.trim(), body: form.body.trim(), target: form.target, destination: form.destination, examIds, userIds,
    });
    setConfirm(false);
    setResult(`Queued for ${audience.count?.toLocaleString("en-IN")} student(s). Delivery happens in the background.`);
    setForm((f) => ({ ...f, title: "", body: "" }));
    setPage(1);
    setHistoryKey((k) => k + 1);
  }

  return (
    <div>
      <PageHeader title="Notifications" subtitle="Send an in-app and push announcement, and review what you've sent before." />
      <div className="grid grid-cols-1 gap-5 p-4 sm:p-6 xl:grid-cols-[minmax(0,5fr)_minmax(0,6fr)]">
        <Card className="flex flex-col gap-4 self-start">
          <h2 className="flex items-center gap-2 text-sm font-extrabold text-ink-900"><Megaphone className="h-4 w-4 text-primary-500" /> New announcement</h2>
          {result && <Alert type="success">{result}</Alert>}

          <FormField label="Title" hint={`${form.title.length}/120`}>
            <TextInput maxLength={120} value={form.title} onChange={set("title")} />
          </FormField>
          <FormField label="Message" hint={`${form.body.length}/240`}>
            <TextArea rows={3} maxLength={240} value={form.body} onChange={set("body")} />
          </FormField>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <FormField label="Send to">
              <Select value={form.target} onChange={(e) => { setForm((f) => ({ ...f, target: e.target.value })); setResult(null); }}>
                <option value="AllStudents">All active students</option>
                <option value="Exams">Students preparing for specific exams</option>
                <option value="Users">Specific students</option>
              </Select>
            </FormField>
            <FormField label="Tapping opens">
              <Select value={form.destination} onChange={set("destination")}>
                {DESTINATIONS.map(([k, l]) => <option key={k} value={k}>{l}</option>)}
              </Select>
            </FormField>
          </div>

          {form.target === "Exams" && (
            <fieldset className="max-h-44 overflow-y-auto rounded-xl2 border border-primary-100 p-2">
              <legend className="px-1 text-xs font-semibold text-ink-600">Exams</legend>
              {exams.length === 0 && <p className="p-2 text-xs text-ink-400">No exams found.</p>}
              {exams.map((x) => (
                <label key={x.id} className="flex items-center gap-2 rounded-lg px-2 py-1.5 text-sm hover:bg-primary-50">
                  <input type="checkbox" className="accent-primary-600" checked={examIds.includes(x.id)}
                    onChange={() => setExamIds((p) => (p.includes(x.id) ? p.filter((i) => i !== x.id) : [...p, x.id]))} />
                  {x.name}
                </label>
              ))}
            </fieldset>
          )}

          {form.target === "Users" && (
            <div>
              <TextInput placeholder="Search by username or name (min 2 letters)" value={userQuery} onChange={(e) => setUserQuery(e.target.value)} aria-label="Find students" />
              {userHits.length > 0 && (
                <ul className="mt-1 divide-y divide-primary-50 rounded-xl2 border border-primary-100 bg-white">
                  {userHits.filter((h) => !picked.some((p) => p.id === h.id)).map((h) => (
                    <li key={h.id}><button type="button" className="w-full px-3 py-2 text-left text-sm hover:bg-primary-50"
                      onClick={() => { setPicked((p) => [...p, h]); setUserQuery(""); setUserHits([]); }}>
                      <span className="font-semibold text-ink-900">{h.fullName}</span> <span className="text-xs text-ink-400">@{h.username}</span></button></li>
                  ))}
                </ul>
              )}
              <div className="mt-2 flex flex-wrap gap-1.5">
                {picked.map((u) => (
                  <span key={u.id} className="flex items-center gap-1 rounded-full bg-primary-50 px-2.5 py-1 text-xs font-semibold text-primary-600">
                    @{u.username}
                    <button type="button" aria-label={`Remove ${u.username}`} onClick={() => setPicked((p) => p.filter((x) => x.id !== u.id))}><X className="h-3 w-3" /></button>
                  </span>
                ))}
              </div>
            </div>
          )}

          <p className="rounded-xl2 bg-primary-50 px-3 py-2 text-sm text-primary-600" aria-live="polite">
            {!targetReady ? "Choose who should receive this." : audience.loading ? "Counting recipients…" : audience.error ? audience.error
              : `This will reach ${audience.count?.toLocaleString("en-IN")} active student(s).`}
          </p>

          <Button disabled={!canSend} onClick={() => setConfirm(true)}>Review & send…</Button>
        </Card>

        <div className="flex flex-col gap-3">
          <h2 className="text-sm font-extrabold text-ink-900">Sent announcements</h2>
          {history.error && <Alert>{history.error}</Alert>}
          {history.isLoading && !history.data && <div className="h-40 animate-pulse rounded-xl2 bg-white shadow-card" aria-busy="true" />}
          {history.data?.items?.length === 0 && <Card><p className="text-sm text-ink-400">Nothing sent yet.</p></Card>}
          {history.data?.items?.map((c) => (
            <Card key={c.id} className="!p-4">
              <div className="flex items-start justify-between gap-2">
                <h3 className="text-sm font-bold text-ink-900">{c.title}</h3>
                <span className="shrink-0 text-[11px] text-ink-400">{new Date(c.sentAt).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" })}</span>
              </div>
              <p className="mt-1 text-xs text-ink-600">{c.body}</p>
              <p className="mt-2 text-[11px] text-ink-400">
                {c.sentBy ? `Sent by ${c.sentBy} · ` : ""}{c.recipients.toLocaleString("en-IN")} recipients · {c.read.toLocaleString("en-IN")} read ({c.readRate}%)
              </p>
            </Card>
          ))}
          {history.data?.note && <p className="text-[11px] text-ink-400">{history.data.note}</p>}
          {history.data && <Pager page={page} pageSize={PAGE_SIZE} totalCount={history.data.totalCount} onPage={setPage} />}
        </div>
      </div>

      <ConfirmDialog
        open={confirm} title="Send this announcement?"
        message={<><p className="font-semibold text-ink-900">{form.title}</p><p className="mt-1">{form.body}</p>
          <p className="mt-2">It will be sent to <b>{audience.count?.toLocaleString("en-IN")}</b> student(s) and cannot be recalled.</p></>}
        confirmLabel="Send now" onCancel={() => setConfirm(false)} onConfirm={send}
      />
    </div>
  );
}
