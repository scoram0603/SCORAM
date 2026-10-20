import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Megaphone, X, Loader2, SearchX, ArrowRight } from "lucide-react";
import { getNotification } from "../../api/notifications";
import { formatNoticeTime, noticeDestination } from "../../utils/notice";

// Full view of one admin notice, opened from the notification bell.
//
// Shows what the bell row already has immediately (title/body from the list), then re-reads the notice
// from GET /api/notifications/{id} so a notice that was deleted meanwhile is reported honestly instead of
// being shown from a stale list. The body is NEVER truncated here: it wraps, keeps the admin's line breaks
// and scrolls inside the dialog, so a long notice is fully readable on a phone too.
export default function NoticeDetailModal({ notification, onClose, onNavigate }) {
  const [current, setCurrent] = useState(notification);
  const [status, setStatus] = useState("loading"); // loading | ready | missing | error
  const closeRef = useRef(null);

  useEffect(() => {
    const controller = new AbortController();
    // Mounted fresh for each opened notice, so the initial state above is already right.
    getNotification(notification.id, { signal: controller.signal })
      .then((fresh) => {
        setCurrent(fresh);
        setStatus("ready");
      })
      .catch((err) => {
        if (controller.signal.aborted) return;
        // apiFetch throws an error carrying the HTTP status; 404 = deleted or not this user's notice.
        setStatus(err?.status === 404 ? "missing" : "error");
      });
    return () => controller.abort();
  }, [notification]);

  useEffect(() => {
    closeRef.current?.focus();
    const previous = document.activeElement;
    const onKey = (e) => e.key === "Escape" && onClose();
    document.addEventListener("keydown", onKey);
    const { overflow } = document.body.style;
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = overflow;
      if (previous && previous.focus) previous.focus();
    };
  }, [onClose]);

  const destination = status === "missing" ? null : noticeDestination(current);
  const time = formatNoticeTime(current.createdAt);

  return createPortal(
    <div
      className="fixed inset-0 z-[60] flex items-end justify-center bg-ink-900/50 p-0 sm:items-center sm:p-4"
      onMouseDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="notice-title"
        className="flex max-h-[88vh] w-full max-w-lg flex-col overflow-hidden rounded-t-2xl bg-white shadow-floating sm:rounded-2xl"
      >
        <div className="flex items-start gap-3 border-b border-primary-50 px-5 py-4">
          <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary-50 text-primary-600">
            <Megaphone className="h-[18px] w-[18px]" strokeWidth={2} />
          </span>
          <div className="min-w-0 flex-1">
            <p className="text-[11px] font-semibold uppercase tracking-wide text-ink-400">Notice from SCORAM{time ? ` · ${time}` : ""}</p>
            <h2 id="notice-title" className="mt-0.5 break-words text-base font-bold text-ink-900">
              {current.title}
            </h2>
          </div>
          <button
            ref={closeRef}
            type="button"
            onClick={onClose}
            aria-label="Close notice"
            className="-mr-1 flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-ink-400 hover:bg-primary-50 hover:text-ink-700"
          >
            <X className="h-4 w-4" strokeWidth={2.25} />
          </button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">
          {status === "missing" ? (
            <div className="flex flex-col items-center gap-2 py-6 text-center">
              <SearchX className="h-7 w-7 text-ink-300" strokeWidth={1.5} />
              <p className="text-sm font-semibold text-ink-700">This notice is no longer available.</p>
              <p className="text-xs text-ink-400">It may have been removed by an administrator.</p>
            </div>
          ) : (
            <>
              <p className="whitespace-pre-wrap break-words text-sm leading-relaxed text-ink-700">{current.body}</p>
              {status === "loading" && (
                <p className="mt-3 flex items-center gap-1.5 text-xs text-ink-400">
                  <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.25} /> Checking for updates…
                </p>
              )}
              {status === "error" && <p className="mt-3 text-xs text-ink-400">Couldn't refresh this notice; showing the saved copy.</p>}
            </>
          )}
        </div>

        {destination && (
          <div className="border-t border-primary-50 px-5 py-3">
            <button
              type="button"
              onClick={() => onNavigate(destination.path)}
              className="inline-flex w-full items-center justify-center gap-2 rounded-xl bg-primary-600 px-4 py-2.5 text-sm font-semibold text-white hover:bg-primary-700"
            >
              {destination.label}
              <ArrowRight className="h-4 w-4" strokeWidth={2.25} />
            </button>
          </div>
        )}
      </div>
    </div>,
    document.body,
  );
}
