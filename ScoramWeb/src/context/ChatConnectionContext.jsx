import { createContext, useContext, useEffect, useState, useCallback } from "react";
import * as signalR from "@microsoft/signalr";
import { useAuth } from "./AuthContext";
import { getStoredToken, API_BASE_URL } from "../api/client";

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
      .catch(() => {
        // Silent -- chat simply won't be real-time until reconnected; withAutomaticReconnect handles
        // transient drops, and REST endpoints (join/send/etc.) don't depend on this connection at all.
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
