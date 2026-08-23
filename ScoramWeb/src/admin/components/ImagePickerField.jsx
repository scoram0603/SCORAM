import { useRef } from "react";
import { ImagePlus, X } from "lucide-react";

// A single "choose an image" control with a preview-name + remove button. Originally lived only in
// PyqUploadWizard.jsx; now shared with QuestionBankManagement.jsx too (Question Bank questions --
// single-add and edit, including a bulk-imported one -- gained image support alongside PYQ questions).
export default function ImagePickerField({ label, file, onChange, compact }) {
  const inputRef = useRef(null);
  return (
    <div className={compact ? "mt-2" : ""}>
      {!compact && <span className="mb-1 block text-xs font-semibold text-ink-600">{label}</span>}
      <label className={`flex cursor-pointer items-center gap-2 rounded-xl2 border border-dashed border-primary-100 bg-white text-ink-600 hover:border-secondary-500 ${compact ? "px-2.5 py-1.5 text-xs" : "px-3.5 py-2.5 text-sm"}`}>
        <ImagePlus className={compact ? "h-3.5 w-3.5 text-ink-400" : "h-4 w-4 text-ink-400"} strokeWidth={2} />
        <span className="truncate">{file ? file.name : compact ? label : "Choose an image"}</span>
        <input
          ref={inputRef}
          type="file"
          accept=".png,.jpg,.jpeg,.webp,.svg"
          className="hidden"
          onChange={(e) => onChange(e.target.files?.[0] || null)}
        />
        {file && (
          <button
            type="button"
            onClick={(e) => {
              e.preventDefault();
              onChange(null);
              if (inputRef.current) inputRef.current.value = "";
            }}
            className="ml-auto text-ink-400 hover:text-red-500"
          >
            <X className="h-3.5 w-3.5" strokeWidth={2.5} />
          </button>
        )}
      </label>
    </div>
  );
}
