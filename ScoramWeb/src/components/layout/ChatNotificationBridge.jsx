import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../../context/AuthContext";
import { useChatConnection } from "../../context/ChatConnectionContext";
import Toast from "./Toast";

const DM_PREVIEW_BY_TYPE = {
  Image: "📷 Photo",
  Document: "📄 Document",
  Audio: "🎤 Voice message",
};

// Requests permission once messaging is actually used, rather than nagging on page load --
// most browsers also just silently ignore a permission request that isn't tied to a user gesture.
function ensureBrowserPermission() {
  if (!("Notification" in window)) return;
  if (Notification.permission === "default") Notification.requestPermission();
}

// Fires a native OS/browser notification only when the tab isn't the one the person is looking
// at right now -- if they're already in the app, the in-app toast below is enough and a native
// popup on top would just be redundant noise.
function showBrowserNotification(title, body) {
  if (!("Notification" in window) || Notification.permission !== "granted") return;
  if (!document.hidden) return;
  try {
    new Notification(title, { body, icon: "/favicon.ico" });
  } catch {
    // Some browsers (older Safari, some in-app webviews) throw on `new Notification` even when
    // permission is "granted" -- a missed native popup isn't worth crashing anything over.
  }
}

// Mounted once inside AppLayout so it's alive on every page, not just while /chat is open --
// that's the whole point: seeing a toast for a DM while you're on Search or Tests, not just
// when you happen to already be looking at the Messages tab.
export default function ChatNotificationBridge({ onUnseenChatActivity }) {
  const { user } = useAuth();
  const { latestMention, clearLatestMention, latestDirectMessage, clearLatestDirectMessage } = useChatConnection();
  const navigate = useNavigate();
  const [toast, setToast] = useState(null);

  // The service worker can't call react-router's navigate() itself (it runs outside the page) --
  // it posts a message instead when a native push notification is clicked, and this is what
  // actually moves the SPA to the right screen.
  useEffect(() => {
    if (!("serviceWorker" in navigator)) return;
    function onMessage(event) {
      if (event.data?.type === "navigate" && event.data.url) navigate(event.data.url);
    }
    navigator.serviceWorker.addEventListener("message", onMessage);
    return () => navigator.serviceWorker.removeEventListener("message", onMessage);
  }, [navigate]);

  useEffect(() => {
    if (!latestMention) return;
    ensureBrowserPermission();

    if (user.notifyOnGroupMessages !== false) {
      const title = `${latestMention.senderName} mentioned you`;
      const body = latestMention.messageText || "Tap to view the conversation";
      setToast({ title, body, onClick: () => navigate("/chat") });
      showBrowserNotification(title, body);
      onUnseenChatActivity?.();
    }
    clearLatestMention();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [latestMention]);

  useEffect(() => {
    if (!latestDirectMessage) return;
    // The hub pushes to both participants' groups so the sender's other devices stay in sync --
    // that means the sender also receives this event for their own message. Skip notifying yourself.
    if (latestDirectMessage.senderId === user.userId) {
      clearLatestDirectMessage();
      return;
    }
    ensureBrowserPermission();

    if (user.notifyOnDirectMessages !== false) {
      const title = latestDirectMessage.senderFullName;
      const body = latestDirectMessage.messageText || DM_PREVIEW_BY_TYPE[latestDirectMessage.messageType] || "New message";
      setToast({ title, body, onClick: () => navigate("/chat?tab=messages") });
      showBrowserNotification(title, body);
      onUnseenChatActivity?.();
    }
    clearLatestDirectMessage();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [latestDirectMessage]);

  if (!toast) return null;

  return (
    <div className="pointer-events-none fixed right-4 top-4 z-50 flex flex-col gap-2">
      <Toast {...toast} onDismiss={() => setToast(null)} />
    </div>
  );
}
