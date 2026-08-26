import { AlertCircle, CheckCircle2, Loader2 } from "lucide-react";

// Shared full-question preview used by both bulk-import wizards (Question Bank's
// QuestionBankUploadWizard and the paper-level BulkImportPanel) when an admin expands a preview
// row -- shows every option with the correct one highlighted, plus explanation/source, so mistakes
// in the source file are caught before commit instead of after (rows are text-only here; neither
// importer supports per-option images yet).
export function ImportRowOptionsDetail({ row }) {
  const options = [
    { letter: "A", text: row.optionA },
    { letter: "B", text: row.optionB },
    { letter: "C", text: row.optionC },
    { letter: "D", text: row.optionD },
  ];
  const correct = (row.correctOption || "").trim().toUpperCase();

  return (
    <div className="rounded-lg border border-primary-100 bg-primary-50/40 p-3 text-xs">
      <p className="whitespace-pre-wrap font-semibold text-ink-900">{row.questionText || "—"}</p>

      <div className="mt-2.5 grid grid-cols-1 gap-1.5 sm:grid-cols-2">
        {options.map((opt) => {
          const isCorrect = opt.letter === correct;
          return (
            <div
              key={opt.letter}
              className={`flex items-start gap-1.5 rounded-lg px-2 py-1.5 ${
                isCorrect ? "bg-mint-50 font-semibold text-mint-600" : "bg-white text-ink-600"
              }`}
            >
              <span
                className={`mt-px shrink-0 rounded-full px-1.5 py-0.5 text-[10px] font-bold ${
                  isCorrect ? "bg-mint-500 text-white" : "bg-primary-100 text-ink-600"
                }`}
              >
                {opt.letter}
              </span>
              <span className="min-w-0 flex-1">{opt.text || "—"}</span>
              {isCorrect && <CheckCircle2 className="mt-0.5 h-3.5 w-3.5 shrink-0" strokeWidth={2.5} />}
            </div>
          );
        })}
      </div>

      {row.explanation && (
        <div className="mt-2 rounded-lg bg-white p-2">
          <span className="font-semibold text-ink-900">Explanation: </span>
          <span className="text-ink-600">{row.explanation}</span>
        </div>
      )}

      {row.sourceReference && (
        <p className="mt-1.5 text-[11px] text-ink-400">Source: {row.sourceReference}</p>
      )}
    </div>
  );
}

export function PageHeader({ title, subtitle, action }) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-3 border-b border-primary-100 bg-white px-6 py-5">
      <div>
        <h1 className="text-lg font-extrabold text-ink-900">{title}</h1>
        {subtitle && <p className="mt-0.5 text-sm text-ink-400">{subtitle}</p>}
      </div>
      {action}
    </div>
  );
}

export function Card({ children, className = "" }) {
  return <div className={`rounded-xl2 bg-white p-5 shadow-card ${className}`}>{children}</div>;
}

const BUTTON_VARIANTS = {
  primary: "bg-primary-600 text-white hover:bg-primary-700 disabled:opacity-60",
  secondary: "bg-primary-50 text-primary-600 hover:bg-primary-100 disabled:opacity-60",
  danger: "bg-red-50 text-red-600 hover:bg-red-100 disabled:opacity-60",
  ghost: "bg-transparent text-ink-600 hover:bg-primary-50 disabled:opacity-60",
};

export function Button({ variant = "primary", isLoading, children, className = "", ...rest }) {
  return (
    <button
      type="button"
      disabled={isLoading || rest.disabled}
      className={`flex items-center justify-center gap-1.5 rounded-xl2 px-4 py-2.5 text-sm font-semibold transition-colors ${BUTTON_VARIANTS[variant]} ${className}`}
      {...rest}
    >
      {isLoading && <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />}
      {children}
    </button>
  );
}

export function FormField({ label, hint, children }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-semibold text-ink-600">{label}</span>
      {children}
      {hint && <span className="mt-1 block text-xs text-ink-400">{hint}</span>}
    </label>
  );
}

const inputClasses =
  "w-full rounded-xl2 border border-primary-100 bg-white px-3.5 py-2.5 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500 focus:outline-none";

export function TextInput(props) {
  return <input {...props} className={`${inputClasses} ${props.className || ""}`} />;
}

export function TextArea(props) {
  return <textarea {...props} className={`${inputClasses} ${props.className || ""}`} />;
}

export function Select(props) {
  return <select {...props} className={`${inputClasses} ${props.className || ""}`} />;
}

export function friendlyError(err) {
  if (err?.status === 403) {
    return "You don't have permission to do this. Ask a Super Admin to grant you the right permission under Manage Admins → Permissions.";
  }
  return err?.message || "Something went wrong.";
}

export function Alert({ type = "error", children }) {
  const isError = type === "error";
  return (
    <div
      className={`flex items-start gap-2 rounded-xl2 p-3 text-xs font-medium ${
        isError ? "bg-red-50 text-red-600" : "bg-mint-50 text-mint-500"
      }`}
    >
      {isError ? (
        <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
      ) : (
        <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
      )}
      <span>{children}</span>
    </div>
  );
}

const STATUS_STYLES = {
  Pending: "bg-accent-50 text-accent-600",
  InProgress: "bg-secondary-50 text-secondary-600",
  Completed: "bg-mint-50 text-mint-500",
  Draft: "bg-ink-400/10 text-ink-600",
  PendingReview: "bg-accent-50 text-accent-600",
  Published: "bg-mint-50 text-mint-500",
};

const STATUS_LABELS = {
  PendingReview: "Pending Review",
};

export function StatusBadge({ status }) {
  return (
    <span className={`rounded-full px-2.5 py-1 text-xs font-semibold ${STATUS_STYLES[status] || "bg-ink-400/10 text-ink-600"}`}>
      {STATUS_LABELS[status] || status}
    </span>
  );
}
