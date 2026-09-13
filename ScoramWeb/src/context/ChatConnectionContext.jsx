import { createContext, useContext, useEffect, useState, useCallback } from "react";
import * as signalR from "@microsoft/signalr";
import { useAuth } from "./AuthContext";
import { getStoredToken, API_BASE_URL, notifyStudentSessionExpired } from "../api/client";

const ChatConnectionContext = createContext(null);

export function ChatConnectionProvider({ children }) {
  const { isAuthenticated } = useAuth();
  // Kept as state (not a ref) on purpose -- consumers' effects depend on this value, so they correctly
  // re-run and attach handlers once the connection object actually exists, instead of racing against it.
  const [connection, setConnection] = useState(null);
  const [latestMention, setLatestMention] = useState(null);
  const [latestDirectMessage, setLatestDirectMessage] = useState(null);
  const [latestNotification, setLatestNotification] = useState(null);

  useEffect(() => {
    if (!isAuthenticated) {
      setConnection(null);
      return;
    }

    const token = getStoredToken();
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE_URL}/hubs/chat`, { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .build();

    // The events this provider cares about globally -- everything room/DM-view-specific
    // (ReceiveMessage, MessageDeleted, PollUpdated, ChatLockChanged, MemberRemoved, and DM presence's
    // DmPresenceUpdated) is subscribed to directly by whichever view is currently open, via the shared
    // `connection` below -- ConversationsList and ConversationThread do exactly that for
    // DmPresenceUpdated.
    conn.on("ReceiveMention", (message) => setLatestMention(message));
    conn.on("ReceiveDirectMessage", (message) => setLatestDirectMessage(message));
    conn.on("ReceiveNotification", (notification) => setLatestNotification(notification));

    let cancelled = false;
    conn.start()
      .then(() => {
        if (!cancelled) setConnection(conn);
      })
      .catch((err) => {
        // A dead/expired token never goes through apiFetch (SignalR's own negotiate call bypasses
        // it entirely), so parseApiResponse's 401 handling in api/client.js never sees this one --
        // without this check, an expired token just silently fails to connect forever, and the
        // stale session in localStorage never gets cleared. This IS reachable on the Login page
        // itself: a token from a previous, since-expired session is enough for `isAuthenticated`
        // above to read true until some OTHER authenticated call happens to hit the real 401
        // first. Anything else (offline, backend down, CORS) is left alone -- withAutomaticReconnect
        // already covers a connection dropping AFTER it connects, and a transient failure here
        // shouldn't log anyone out.
        const isAuthFailure =
          err?.statusCode === 401 ||
          err?.statusCode === 403 ||
          /unauthorized|status code '?40[13]'?/i.test(String(err?.message ?? ""));
        if (isAuthFailure) notifyStudentSessionExpired();
      });

    return () => {
      cancelled = true;
      conn.stop();
    };
  }, [isAuthenticated]);

  const joinRoomGroup = useCallback((roomId) => connection?.invoke("JoinRoomGroup", roomId).catch(() => {}), [connection]);
  const leaveRoomGroup = useCallback((roomId) => connection?.invoke("LeaveRoomGroup", roomId).catch(() => {}), [connection]);
  // Deliberately decoupled from `connection`'s own lifecycle (which spans the whole authenticated
  // session, for @mentions/notifications on every page) -- these are the DM-section presence signal,
  // called explicitly by GroupChat.jsx only while its Messages tab is the one actually visible. See
  // ChatHub's own header comment for why "connection exists" alone isn't a valid proxy for that here.
  const enterDmSection = useCallback(() => connection?.invoke("EnterDmSection").catch(() => {}), [connection]);
  const leaveDmSection = useCallback(() => connection?.invoke("LeaveDmSection").catch(() => {}), [connection]);

  const value = {
    connection,
    isConnected: Boolean(connection),
    joinRoomGroup,
    leaveRoomGroup,
    enterDmSection,
    leaveDmSection,
    latestMention,
    clearLatestMention: () => setLatestMention(null),
    latestDirectMessage,
    clearLatestDirectMessage: () => setLatestDirectMessage(null),
    latestNotification,
    clearLatestNotification: () => setLatestNotification(null),
  };

  return <ChatConnectionContext.Provider value={value}>{children}</ChatConnectionContext.Provider>;
}

export function useChatConnection() {
  const ctx = useContext(ChatConnectionContext);
  if (!ctx) throw new Error("useChatConnection must be used within a ChatConnectionProvider");
  return ctx;
}
