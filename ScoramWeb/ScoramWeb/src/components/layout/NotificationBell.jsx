import { useEffect, useRef, useState } from "react";
import { Bell, AtSign, MessageCircle, Loader2, Inbox } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { listNotifications, getUnreadCount, markNotificationRead, markAllNotificationsRead } from "../../api/notifications";
import { useChatConnection } from "../../context/ChatConnectionContext";
import { timeAgo } from "../../utils/format";

const TYPE_ICON = { Mention: AtSign, DirectMessage: MessageCircle };

export default function NotificationBell({ variant = "desktop" }) {
  const navigate = useNavigate();
  const { latestNotification, clearLatestNotification } = useChatConnection();
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState([]);
  const [status, setStatus] = useState("idle");
  const [unreadCount, setUnreadCount] = useState(0);
  const wrapperRef = useRef(null);

  useEffect(() => {
    getUnreadCount()
      .then((d) => setUnreadCount(d.count))
      .catch(() => {});
  }, []);

  // Live: a notification arriving anywhere in the app bumps the badge and prepends to the list
  // immediately, without waiting for the panel to be reopened.
  useEffect(() => {
    if (!latestNotification) return;
    setUnreadCount((c) => c + 1);
    setItems((prev) => [latestNotification, ...prev]);
    clearLatestNotification();
  }, [latestNotification, clearLatestNotification]);

  useEffect(() => {
    if (!open) return;
    function handleClickOutside(e) {
      if (wrapperRef.current && !wrapperRef.current.contains(e.target)) setOpen(false);
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, [open]);

  function openPanel() {
    setOpen(true);
    setStatus("loading");
    listNotifications({ pageSize: 20 })
      .then((data) => {
        setItems(data);
        setStatus("ready");
      })
      .catch(() => setStatus("error"));
  }

  function handleItemClick(n) {
    setOpen(false);
    if (!n.isRead) {
      setItems((prev) => prev.map((x) => (x.id === n.id ? { ...x, isRead: true } : x)));
      setUnreadCount((c) => Math.max(0, c - 1));
      markNotificationRead(n.id).catch(() => {});
    }
    navigate(n.linkUrl || "/");
  }

  function handleMarkAllRead() {
    setItems((prev) => prev.map((x) => ({ ...x, isRead: true })));
    setUnreadCount(0);
    markAllNotificationsRead().catch(() => {});
  }

  const isMobile = variant === "mobile";
  const triggerClasses = isMobile
    ? "relative flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-primary-600 transition-colors hover:bg-primary-50 active:bg-primary-100"
    : "relative flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-primary-100 text-primary-600 transition-colors hover:bg-primary-50";

  return (
    <div className="relative" ref={wrapperRef}>
      <button
        type="button"
        onClick={() => (open ? setOpen(false) : openPanel())}
        aria-label={unreadCount > 0 ? `Notifications, ${unreadCount} unread` : "Notifications"}
        className={triggerClasses}
      >
        <Bell className={isMobile ? "h-6 w-6" : "h-5 w-5"} strokeWidth={2.25} />
        {unreadCount > 0 && (
          <span
            className={`absolute flex h-[18px] min-w-[18px] items-center justify-center rounded-full bg-accent-500 px-1 text-[10px] font-bold leading-none text-white ${
              isMobile ? "-right-0.5 -top-0.5" : "-right-1 -top-1"
            }`}
          >
            {unreadCount > 9 ? "9+" : unreadCount}
          </span>
        )}
      </button>

      {open && (
        <div className="absolute right-0 top-full z-40 mt-2 w-[340px] max-w-[calc(100vw-32px)] overflow-hidden rounded-xl2 border border-primary-100 bg-white shadow-floating">
          <div className="flex items-center justify-between border-b border-primary-50 px-4 py-3">
            <p className="text-sm font-bold text-ink-900">Notifications</p>
            {unreadCount > 0 && (
              <button type="button" onClick={handleMarkAllRead} className="text-xs font-semibold text-secondary-500 hover:text-secondary-600">
                Mark all read
              </button>
            )}
          </div>

          <div className="max-h-[420px] overflow-y-auto">
            {status === "loading" && (
              <div className="flex justify-center py-8 text-ink-400">
                <Loader2 className="h-5 w-5 animate-spin" strokeWidth={2.25} />
              </div>
            )}
            {status === "error" && <p className="px-4 py-8 text-center text-sm text-red-600">Couldn't load notifications.</p>}
            {status === "ready" && items.length === 0 && (
              <div className="flex flex-col items-center gap-2 py-10 text-center">
                <Inbox className="h-7 w-7 text-ink-300" strokeWidth={1.5} />
                <p className="text-sm text-ink-400">Nothing yet.</p>
              </div>
            )}
            {items.map((n) => {
              const Icon = TYPE_ICON[n.type] || Bell;
              return (
                <button
                  key={n.id}
                  type="button"
                  onClick={() => handleItemClick(n)}
                  className={`flex w-full items-start gap-3 border-b border-primary-50 px-4 py-3 text-left transition-colors hover:bg-primary-50/40 ${
                    n.isRead ? "" : "bg-secondary-50/40"
                  }`}
                >
                  <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-primary-50 text-primary-600">
                    <Icon className="h-4 w-4" strokeWidth={2} />
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-semibold text-ink-900">{n.title}</span>
                    <span className="block truncate text-xs text-ink-400">{n.body}</span>
                    <span className="mt-0.5 block text-[11px] text-ink-300">{timeAgo(n.createdAt)}</span>
                  </span>
                  {!n.isRead && <span className="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-secondary-500" />}
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
