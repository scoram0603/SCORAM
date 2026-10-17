import { useState } from "react";
import { Download, FileSpreadsheet, FileText } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { downloadReport, getReportCatalog } from "../api/analytics";
import { Alert, Card, PageHeader } from "../components/AdminUI";
import { DateRangeBar, useLoader } from "../components/analytics";
import { resolvePreset } from "../lib/dateRange";

export default function ReportsExports() {
  const { token } = useAdminAuth();
  const [range, setRange] = useState(() => resolvePreset("30d"));
  const catalog = useLoader(() => getReportCatalog(token), [token]);
  const [busy, setBusy] = useState(null); // `${key}:${format}`
  const [message, setMessage] = useState(null);

  async function run(report, format) {
    setBusy(`${report.key}:${format}`);
    setMessage(null);
    try {
      const { truncated, filename } = await downloadReport(token, report.key, range, format);
      setMessage({
        type: truncated ? "error" : "success",
        text: truncated
          ? `${filename} was cut off at 50,000 rows. Narrow the date range to get everything.`
          : `Downloaded ${filename}.`,
      });
    } catch (err) {
      setMessage({ type: "error", text: err.message });
    } finally {
      setBusy(null);
    }
  }

  return (
    <div>
      <PageHeader title="Reports & Exports" subtitle="Download CSV or Excel files for the selected period. Every export is recorded in the audit log." />
      <div className="flex flex-col gap-4 p-4 sm:p-6">
        <DateRangeBar range={range} onChange={setRange} />
        {message && <Alert type={message.type}>{message.text}</Alert>}
        {catalog.error && <Alert>{catalog.error}</Alert>}

        {catalog.isLoading && !catalog.data && <div className="h-32 animate-pulse rounded-xl2 bg-white shadow-card" aria-busy="true" />}
        {catalog.data?.length === 0 && (
          <Card><p className="text-sm text-ink-600">No reports are available to your account. Reports need the Export Reports permission plus access to the underlying data.</p></Card>
        )}

        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {catalog.data?.map((r) => (
            <Card key={r.key} className="flex flex-col gap-3">
              <div>
                <div className="flex items-center gap-2">
                  <h3 className="text-sm font-extrabold text-ink-900">{r.label}</h3>
                  <span className="rounded-full bg-primary-50 px-2 py-0.5 text-[10px] font-bold uppercase text-primary-600">{r.kind}</span>
                </div>
                <p className="mt-1 text-xs text-ink-600">{r.description}</p>
              </div>
              <div className="mt-auto flex gap-2">
                {[["csv", "CSV", FileText], ["xlsx", "Excel", FileSpreadsheet]].map(([fmt, label, Icon]) => (
                  <button key={fmt} type="button" disabled={busy !== null} onClick={() => run(r, fmt)}
                    className="flex items-center gap-1.5 rounded-xl2 bg-primary-50 px-3 py-2 text-xs font-semibold text-primary-600 hover:bg-primary-100 disabled:opacity-60">
                    {busy === `${r.key}:${fmt}` ? <Download className="h-4 w-4 animate-bounce" /> : <Icon className="h-4 w-4" />}
                    {label}
                  </button>
                ))}
              </div>
            </Card>
          ))}
        </div>
        <p className="text-[11px] text-ink-400">Spreadsheet cells that start with = + - @ are neutralised so a malicious value cannot run as a formula when opened in Excel. Exports are capped at 50,000 rows.</p>
      </div>
    </div>
  );
}
