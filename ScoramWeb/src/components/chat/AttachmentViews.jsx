import { useState } from "react";
import { FileText, ImageIcon, Loader2, RefreshCw } from "lucide-react";
import { useAttachmentObjectUrl } from "../../hooks/useAttachmentObjectUrl";
import {
  describeAttachmentError,
  openDocumentAttachment,
  toPrivateAttachmentPath,
} from "../../api/attachments";

// Views for PRIVATE chat / DM attachments. They load the file through the authenticated API (see
// api/attachments.js) -- never from a public URL -- and show a calm placeholder instead of breaking the
// chat when the file is loading, deleted, missing, or the session ended.

function Placeholder({ children, className = "" }) {
  return (
    <div className={`mt-1.5 flex items-center gap-2 rounded-lg bg-black/5 px-3 py-2 text-xs ${className}`}>{children}</div>
  );
}

function ErrorLine({ error, onRetry }) {
  return (
    <Placeholder>
      <ImageIcon className="h-4 w-4 shrink-0 opacity-60" strokeWidth={2} />
      <span className="flex-1">{error.message}</span>
      {error.retryable && (
        <button type="button" onClick={onRetry} className="inline-flex items-center gap-1 font-semibold underline">
          <RefreshCw className="h-3 w-3" strokeWidth={2.5} />
          Retry
        </button>
      )}
    </Placeholder>
  );
}

export function AttachmentImage({ attachmentUrl, className = "mt-1.5 max-h-60 rounded-lg" }) {
  const { status, url, error, retry } = useAttachmentObjectUrl(attachmentUrl);
  if (status === "idle") return null;
  if (status === "loading") {
    return (
      <Placeholder className="h-24 w-40 justify-center">
        <Loader2 className="h-4 w-4 animate-spin" />
      </Placeholder>
    );
  }
  if (status === "error") return <ErrorLine error={error} onRetry={retry} />;
  return <img src={url} alt="" className={className} />;
}

// Documents are fetched only when the student taps them (no point downloading 15 MB the person may never open).
export function AttachmentDocumentButton({ attachmentUrl, isOwn }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const path = toPrivateAttachmentPath(attachmentUrl);

  async function open() {
    if (!path || busy) return;
    setBusy(true);
    setError(null);
    try {
      await openDocumentAttachment(path);
    } catch (e) {
      setError(describeAttachmentError(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <button
        type="button"
        onClick={open}
        disabled={!path || busy}
        className={`mt-1.5 flex items-center gap-2 rounded-lg p-2 text-xs font-semibold disabled:opacity-60 ${isOwn ? "bg-white/10" : "bg-primary-50"}`}
      >
        {busy ? <Loader2 className="h-4 w-4 shrink-0 animate-spin" /> : <FileText className="h-4 w-4 shrink-0" strokeWidth={2} />}
        Open document
      </button>
      {!path && <p className="mt-1 text-[11px] opacity-70">This attachment is no longer available.</p>}
      {error && <p className="mt-1 text-[11px] opacity-80">{error.message}</p>}
    </>
  );
}
