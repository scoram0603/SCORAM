import { useEffect, useState } from "react";
import { KeyRound } from "lucide-react";
import { changeBusinessId } from "../api/businessIds";
import { Modal } from "./SubjectManagementDialogs";
import { Alert, Button, FormField, TextInput, friendlyError } from "./AdminUI";

// Formats mirror the backend (Services/BusinessIdFormats.cs) -- used only for instant feedback in the
// change dialog. The server is the authority and re-validates everything.
const FORMATS = {
  exam: { label: "Exam", example: "EXMSSC001", regex: /^EXM[A-Z]{1,10}\d{3,9}$/ },
  subject: { label: "Subject", example: "SUB001", regex: /^SUB\d{3,9}$/ },
  test: { label: "Test", example: "TST0001", regex: /^TST\d{4,9}$/ },
  mocktest: { label: "Mock Test", example: "MCK0001", regex: /^MCK\d{4,9}$/ },
  admin: { label: "Admin", example: "ADM0001", regex: /^ADM\d{4,9}$/ },
};

// Small monospace chip -- distinguishable from the entity name without competing with it.
export function BusinessIdBadge({ id, className = "" }) {
  if (!id) return null;
  return (
    <span
      title="Business ID"
      className={`inline-block shrink-0 rounded-md bg-primary-50 px-1.5 py-0.5 font-mono text-[10px] font-semibold tracking-wide text-primary-600 ${className}`}
    >
      {id}
    </span>
  );
}

// SuperAdmin-only affordance. Renders nothing for anyone else (the server enforces this too).
export function ChangeBusinessIdButton({ show, onClick, className = "" }) {
  if (!show) return null;
  return (
    <button
      type="button"
      onClick={onClick}
      title="Change Business ID (Super Admin)"
      aria-label="Change Business ID"
      className={`rounded-full p-1.5 text-ink-400 hover:bg-primary-50 hover:text-primary-600 ${className}`}
    >
      <KeyRound className="h-4 w-4" strokeWidth={2.25} />
    </button>
  );
}

// Case-insensitive match on the Business ID OR any of the record's other searchable fields, so adding
// Business ID search never removes the existing name/title/email search.
export function matchesSearch(query, businessId, ...fields) {
  const q = (query || "").trim().toLowerCase();
  if (!q) return true;
  return [businessId, ...fields].some((f) => (f || "").toString().toLowerCase().includes(q));
}

export function useDebouncedValue(value, delay = 300) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(t);
  }, [value, delay]);
  return debounced;
}

// Two steps, on purpose: (1) enter + validate the new ID, (2) the explicit "Change Business ID?"
// warning. The GUID is what's sent to identify the record, never the old Business ID.
export function BusinessIdChangeDialog({ token, entityType, entity, name, onClose, onChanged }) {
  const format = FORMATS[entityType];
  const [step, setStep] = useState(1);
  const [value, setValue] = useState(entity.businessId || "");
  const [typed, setTyped] = useState("");
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(null);

  const next = value.trim().toUpperCase();
  const formatOk = format.regex.test(next);
  const unchanged = next === (entity.businessId || "").toUpperCase();

  async function submit() {
    setSending(true);
    setError(null);
    try {
      const res = await changeBusinessId(token, {
        entityType,
        entityId: entity.id,
        newBusinessId: next,
        confirm: true,
      });
      onChanged(res);
    } catch (err) {
      // Duplicate / invalid format etc. -> go back to the field so it can be fixed.
      setError(friendlyError(err));
      setStep(1);
    } finally {
      setSending(false);
    }
  }

  if (step === 1) {
    return (
      <Modal
        title={`Change ${format.label} Business ID`}
        icon={KeyRound}
        onClose={onClose}
        busy={sending}
        footer={
          <>
            <Button variant="ghost" onClick={onClose}>Cancel</Button>
            <Button disabled={!formatOk || unchanged} onClick={() => { setError(null); setStep(2); }}>Continue</Button>
          </>
        }
      >
        <p className="text-sm text-ink-600">
          <span className="font-bold text-ink-900">{name}</span>
          {entity.businessId && <> is currently <BusinessIdBadge id={entity.businessId} className="!text-xs" /></>}
        </p>
        {error && <Alert>{error}</Alert>}
        <FormField
          label="New Business ID"
          hint={`Format like ${format.example}. IDs are never reused, so an ID that was ever issued to another record can't be chosen.`}
        >
          <TextInput
            autoFocus
            value={value}
            onChange={(e) => setValue(e.target.value.toUpperCase())}
            placeholder={format.example}
            className="font-mono"
          />
        </FormField>
        {value && !formatOk && (
          <p className="text-xs font-medium text-red-600">Not a valid {format.label} Business ID (e.g. {format.example}).</p>
        )}
      </Modal>
    );
  }

  return (
    <Modal
      title="Change Business ID?"
      icon={KeyRound}
      tone="warning"
      onClose={onClose}
      busy={sending}
      footer={
        <>
          <Button variant="ghost" disabled={sending} onClick={() => setStep(1)}>Back</Button>
          <Button isLoading={sending} disabled={typed.trim().toUpperCase() !== next} onClick={submit}>
            Change Business ID
          </Button>
        </>
      }
    >
      <div className="rounded-xl2 bg-accent-50 p-3 text-sm text-accent-600">
        This identifier is used for administrative identification and may be referenced in reports, logs or external processes.
      </div>
      <div className="flex items-center justify-center gap-3 py-1 font-mono text-sm">
        <BusinessIdBadge id={entity.businessId || "(none)"} className="!text-sm" />
        <span className="text-ink-400">→</span>
        <BusinessIdBadge id={next} className="!text-sm" />
      </div>
      <p className="text-xs text-ink-400">
        Only the Business ID changes. The record itself and everything linked to it stay exactly as they are. The change is written to the audit log.
      </p>
      <FormField label={`Type ${next} to confirm`}>
        <TextInput value={typed} onChange={(e) => setTyped(e.target.value)} className="font-mono" autoFocus />
      </FormField>
    </Modal>
  );
}
