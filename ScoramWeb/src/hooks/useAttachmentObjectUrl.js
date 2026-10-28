import { useEffect, useState } from "react";
import {
  UNAVAILABLE_ATTACHMENT_ERROR,
  acquireAttachment,
  describeAttachmentError,
  toPrivateAttachmentPath,
} from "../api/attachments";

/**
 * Loads one private chat / DM attachment through the authenticated API and exposes it as an object URL.
 *
 *   const { status, url, error, retry } = useAttachmentObjectUrl(message.attachmentUrl);
 *
 * status: "idle" (no attachment) | "loading" | "ready" | "error". `error` is { kind, message, retryable }.
 * The download is shared and reference-counted (api/attachments.js), so re-renders never refetch, and the
 * object URL is released when the component unmounts or the attachment changes. A value that is not an
 * authenticated attachment path (for example an old public /uploads/ URL) is reported as unavailable --
 * it is never loaded anonymously.
 */
export function useAttachmentObjectUrl(attachmentUrl) {
  const path = toPrivateAttachmentPath(attachmentUrl);
  const [attempt, setAttempt] = useState(0);
  // The settled result is remembered together with the path + attempt it belongs to, so a change of
  // attachment shows "loading" immediately instead of the previous message's file.
  const [result, setResult] = useState(null);

  useEffect(() => {
    if (!path) return undefined;
    let cancelled = false;
    let release = null;
    acquireAttachment(path).then(
      (loaded) => {
        if (cancelled) {
          loaded.release();
          return;
        }
        release = loaded.release;
        setResult({ path, attempt, status: "ready", url: loaded.url, error: null });
      },
      (error) => {
        if (cancelled || error?.name === "AbortError") return;
        setResult({ path, attempt, status: "error", url: null, error: describeAttachmentError(error) });
      }
    );
    return () => {
      cancelled = true;
      if (release) release();
    };
  }, [path, attempt]);

  const retry = () => setAttempt((n) => n + 1);

  if (!attachmentUrl) return { status: "idle", url: null, error: null, retry };
  if (!path) return { status: "error", url: null, error: UNAVAILABLE_ATTACHMENT_ERROR, retry };
  if (result && result.path === path && result.attempt === attempt) {
    return { status: result.status, url: result.url, error: result.error, retry };
  }
  return { status: "loading", url: null, error: null, retry };
}
