import { useEffect, useRef, useState } from "react";
import { AlertTriangle } from "lucide-react";
import { Alert, Button, FormField, TextArea } from "./AdminUI";

// Accessible confirmation modal for destructive / sensitive actions.
//   reasonLabel   -> shows a required reason box (min length `minReason`); the reason is passed to onConfirm
//   onConfirm     -> async; a thrown error is shown inside the dialog and the dialog stays open
export default function ConfirmDialog({
  open, title, message, confirmLabel = "Confirm", danger = false, reasonLabel, minReason = 5, onConfirm, onCancel,
}) {
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const cancelRef = useRef(null);

  useEffect(() => {
    if (!open) return undefined;
    setReason("");
    setError(null);
    setBusy(false);
    cancelRef.current?.focus();
    const onKey = (e) => e.key === "Escape" && !busy && onCancel();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  if (!open) return null;

  const reasonTooShort = reasonLabel && reason.trim().length < minReason;

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      await onConfirm(reason.trim());
    } catch (err) {
      setError(err?.message || "That didn't work.");
      setBusy(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" role="alertdialog" aria-modal="true" aria-labelledby="confirm-title">
      <button type="button" className="absolute inset-0 bg-black/50" aria-label="Cancel" onClick={() => !busy && onCancel()} tabIndex={-1} />
      <div className="relative w-full max-w-md rounded-xl2 bg-white p-5 shadow-xl">
        <div className="flex items-start gap-3">
          {danger && (
            <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-red-50 text-red-600">
              <AlertTriangle className="h-5 w-5" strokeWidth={2.25} />
            </span>
          )}
          <div className="min-w-0">
            <h2 id="confirm-title" className="text-base font-extrabold text-ink-900">{title}</h2>
            <div className="mt-1 text-sm text-ink-600">{message}</div>
          </div>
        </div>

        {reasonLabel && (
          <div className="mt-4">
            <FormField label={reasonLabel} hint={`At least ${minReason} characters. Recorded in the audit log.`}>
              <TextArea rows={3} maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} />
            </FormField>
          </div>
        )}

        {error && <div className="mt-3"><Alert>{error}</Alert></div>}

        <div className="mt-5 flex justify-end gap-2">
          <button ref={cancelRef} type="button" onClick={onCancel} disabled={busy}
            className="rounded-xl2 px-4 py-2.5 text-sm font-semibold text-ink-600 hover:bg-primary-50 disabled:opacity-60">
            Cancel
          </button>
          <Button variant={danger ? "danger" : "primary"} isLoading={busy} disabled={Boolean(reasonTooShort)} onClick={submit}
            className={danger ? "!bg-red-600 !text-white hover:!bg-red-700" : ""}>
            {confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  );
}
