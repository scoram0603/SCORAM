import { useState } from "react";
import { ImagePlus, X } from "lucide-react";
import { API_BASE_URL } from "../../api/client";

export function imgSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

// An image field for an EDIT form -- shows the current image (if any) with a Remove button, plus a
// picker to replace it. Originally lived only in PaperDetailView.jsx (editing a legacy PYQ
// question's images); now shared with QuestionBankManagement.jsx too (Question Bank questions gained
// the same image support). onReplace(file|null) fires when a new file is picked or cleared;
// onRemove(bool) fires when the existing image is explicitly removed.
export default function EditImageField({ label, currentUrl, onReplace, onRemove, compact }) {
  const [removed, setRemoved] = useState(false);
  const [newFile, setNewFile] = useState(null);

  return (
    <div className={compact ? "mt-2" : ""}>
      {!compact && <span className="mb-1 block text-xs font-semibold text-ink-600">{label}</span>}
      {currentUrl && !removed && !newFile && (
        <div className="mb-1.5 flex items-center gap-2">
          <img src={imgSrc(currentUrl)} alt="" className={compact ? "h-10 rounded border border-primary-100" : "h-16 rounded-lg border border-primary-100"} />
          <button
            type="button"
            onClick={() => { setRemoved(true); onRemove(true); }}
            className="text-xs font-semibold text-red-600 hover:underline"
          >
            Remove
          </button>
        </div>
      )}
      <label className={`flex cursor-pointer items-center gap-2 rounded-xl2 border border-dashed border-primary-100 bg-white text-ink-600 hover:border-secondary-500 ${compact ? "px-2.5 py-1.5 text-xs" : "px-3.5 py-2.5 text-sm"}`}>
        <ImagePlus className={compact ? "h-3.5 w-3.5 text-ink-400" : "h-4 w-4 text-ink-400"} strokeWidth={2} />
        <span className="truncate">{newFile ? newFile.name : compact ? label : currentUrl ? "Replace image" : "Add image"}</span>
        <input
          type="file"
          accept=".png,.jpg,.jpeg,.webp,.svg"
          className="hidden"
          onChange={(e) => {
            const f = e.target.files?.[0] || null;
            setNewFile(f);
            setRemoved(false);
            onRemove(false);
            onReplace(f);
          }}
        />
        {newFile && (
          <button
            type="button"
            onClick={(e) => { e.preventDefault(); setNewFile(null); onReplace(null); }}
            className="ml-auto text-ink-400 hover:text-red-500"
          >
            <X className="h-3.5 w-3.5" strokeWidth={2.5} />
          </button>
        )}
      </label>
    </div>
  );
}
