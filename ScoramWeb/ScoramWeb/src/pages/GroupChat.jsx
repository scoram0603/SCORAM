import { useEffect, useRef, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import {
  Users, Lock, Send, Paperclip, X, Flag, Trash2, ArrowLeft, FileText, ImageIcon,
  BarChart3, Megaphone, Loader2, ChevronUp, MessageCircle,
} from "lucide-react";
import { useAuth } from "../context/AuthContext";
import { useChatConnection } from "../context/ChatConnectionContext";
import {
  listChatRooms, joinChatRoom, leaveChatRoom, getChatMessages, sendChatMessage,
  deleteOwnMessage, reportChatMessage, getMentionableUsers, votePoll,
} from "../api/chat";
import { API_BASE_URL } from "../api/client";
import ConversationsList from "../components/directMessages/ConversationsList";
import ConversationThread from "../components/directMessages/ConversationThread";

function fileSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

export default function GroupChat() {
  const navigate = useNavigate();
  const { isAuthenticated, user } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const tab = searchParams.get("tab") === "messages" ? "messages" : "rooms";

  const [selectedRoom, setSelectedRoom] = useState(null);
  const [selectedConversation, setSelectedConversation] = useState(null);
  const [dmRefreshSignal, setDmRefreshSignal] = useState(0);
  const { latestDirectMessage, clearLatestDirectMessage } = useChatConnection();

  // Bumps the Messages list's refresh signal on any DM activity anywhere in the app (even while
  // looking at Rooms), so previews/unread counts/ordering are never stale when the user switches tabs.
  useEffect(() => {
    if (latestDirectMessage) {
      setDmRefreshSignal((s) => s + 1);
      clearLatestDirectMessage();
    }
  }, [latestDirectMessage, clearLatestDirectMessage]);

  if (!isAuthenticated) {
    return (
      <div className="flex flex-col items-center gap-3 px-6 py-20 text-center">
        <Users className="h-10 w-10 text-ink-400" strokeWidth={1.5} />
        <p className="text-sm text-ink-400">Log in to chat with your exam group or message other students.</p>
        <button type="button" onClick={() => navigate("/login?redirect=/chat")} className="rounded-xl2 bg-primary-600 px-4 py-2 text-sm font-semibold text-white">
          Log In
        </button>
      </div>
    );
  }

  if (selectedRoom) {
    return <RoomChatView room={selectedRoom} currentUser={user} onBack={() => setSelectedRoom(null)} />;
  }

  if (selectedConversation) {
    return (
      <ConversationThread
        conversation={selectedConversation}
        onBack={() => setSelectedConversation(null)}
        onMessageSent={() => setDmRefreshSignal((s) => s + 1)}
      />
    );
  }

  function switchTab(nextTab) {
    setSearchParams(nextTab === "rooms" ? {} : { tab: nextTab });
  }

  return (
    <div>
      <div className="px-4 pt-4 sm:px-6 lg:px-8 lg:pt-6">
        <h1 className="text-xl font-extrabold text-ink-900 sm:text-2xl">Chat</h1>
        <p className="mt-1 text-sm text-ink-400">
          {tab === "rooms" ? "One chat room per exam. Joining is optional." : "Message any student directly, like a DM."}
        </p>

        <div className="mt-4 inline-flex rounded-xl2 border border-primary-100 bg-white p-1">
          <TabButton active={tab === "rooms"} onClick={() => switchTab("rooms")} icon={Users}>Rooms</TabButton>
          <TabButton active={tab === "messages"} onClick={() => switchTab("messages")} icon={MessageCircle}>Messages</TabButton>
        </div>
      </div>

      {tab === "rooms" ? (
        <RoomsList onOpenRoom={setSelectedRoom} />
      ) : (
        <ConversationsList onOpenConversation={setSelectedConversation} refreshSignal={dmRefreshSignal} />
      )}
    </div>
  );
}

function TabButton({ active, onClick, icon: Icon, children }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex items-center gap-1.5 rounded-lg px-3.5 py-2 text-sm font-semibold transition-colors ${
        active ? "bg-primary-600 text-white" : "text-ink-600 hover:bg-primary-50"
      }`}
    >
      <Icon className="h-4 w-4" strokeWidth={2.25} />
      {children}
    </button>
  );
}

// ---------- Rooms list ----------
function RoomsList({ onOpenRoom }) {
  const [rooms, setRooms] = useState([]);
  const [status, setStatus] = useState("loading");
  const [joiningId, setJoiningId] = useState(null);

  useEffect(() => {
    refresh();
  }, []);

  function refresh() {
    setStatus("loading");
    listChatRooms().then((data) => { setRooms(data); setStatus("ready"); }).catch(() => setStatus("error"));
  }

  async function handleJoin(room) {
    setJoiningId(room.id);
    try {
      await joinChatRoom(room.id);
      refresh();
    } catch {
      // keep it simple -- refresh() below will reflect whatever the true state ended up being
      refresh();
    } finally {
      setJoiningId(null);
    }
  }

  return (
    <div className="px-4 pb-8 pt-5 sm:px-6 lg:px-8">
      {status === "loading" && (
        <div className="flex justify-center py-16"><Loader2 className="h-6 w-6 animate-spin text-ink-400" strokeWidth={2.25} /></div>
      )}
      {status === "error" && <p className="py-16 text-center text-sm text-red-600">Couldn't load rooms right now.</p>}

      <div className="mt-5 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {rooms.map((room) => (
          <div key={room.id} className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
            <div className="flex items-center gap-3">
              {room.examLogoUrl ? (
                <img src={fileSrc(room.examLogoUrl)} alt="" className="h-10 w-10 rounded-full object-cover" />
              ) : (
                <span className="flex h-10 w-10 items-center justify-center rounded-full bg-secondary-50 text-sm font-bold text-secondary-500">
                  {room.examName.slice(0, 2).toUpperCase()}
                </span>
              )}
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-bold text-ink-900">{room.examName}</p>
                <p className="text-xs text-ink-400">{room.memberCount} member{room.memberCount === 1 ? "" : "s"}</p>
              </div>
              {room.isChatDisabled && <Lock className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />}
            </div>

            {room.isBanned ? (
              <p className="mt-3 rounded-lg bg-red-50 px-3 py-2 text-xs font-medium text-red-600">
                You've been removed from this group by an admin.
              </p>
            ) : room.isMember ? (
              <button
                type="button"
                onClick={() => onOpenRoom(room)}
                className="mt-3 w-full rounded-lg bg-primary-600 py-2 text-sm font-semibold text-white hover:bg-primary-700"
              >
                Open Chat
              </button>
            ) : (
              <button
                type="button"
                onClick={() => handleJoin(room)}
                disabled={joiningId === room.id}
                className="mt-3 w-full rounded-lg bg-primary-50 py-2 text-sm font-semibold text-primary-600 hover:bg-primary-100 disabled:opacity-60"
              >
                {joiningId === room.id ? "Joining…" : "Join"}
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

// ---------- Room chat view ----------
function RoomChatView({ room, currentUser, onBack }) {
  const { connection, joinRoomGroup, leaveRoomGroup } = useChatConnection();
  const [messages, setMessages] = useState([]);
  const [status, setStatus] = useState("loading");
  const [hasMore, setHasMore] = useState(true);
  const [isDisabled, setIsDisabled] = useState(room.isChatDisabled);
  const [wasKicked, setWasKicked] = useState(false);
  const scrollRef = useRef(null);

  useEffect(() => {
    getChatMessages(room.id, { pageSize: 30 })
      .then((data) => {
        setMessages(data);
        setHasMore(data.length === 30);
        setStatus("ready");
        requestAnimationFrame(() => scrollToBottom());
      })
      .catch(() => setStatus("error"));
  }, [room.id]);

  useEffect(() => {
    joinRoomGroup(room.id);
    return () => leaveRoomGroup(room.id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [connection, room.id]);

  useEffect(() => {
    if (!connection) return;

    const onMessage = (msg) => {
      if (msg.chatRoomId !== room.id) return;
      setMessages((prev) => [...prev, msg]);
      requestAnimationFrame(() => scrollToBottom());
    };
    const onDeleted = (messageId) => {
      setMessages((prev) => prev.map((m) => (m.id === messageId ? { ...m, isDeleted: true, messageText: null, attachmentUrl: null } : m)));
    };
    const onPollUpdated = (poll) => {
      setMessages((prev) => prev.map((m) => (m.pollId === poll.id ? { ...m, poll } : m)));
    };
    const onLockChanged = (data) => {
      if (data.roomId === room.id) setIsDisabled(data.isChatDisabled);
    };
    const onMemberRemoved = (data) => {
      if (data.roomId === room.id && data.userId === currentUser.userId) setWasKicked(true);
    };

    connection.on("ReceiveMessage", onMessage);
    connection.on("MessageDeleted", onDeleted);
    connection.on("PollUpdated", onPollUpdated);
    connection.on("ChatLockChanged", onLockChanged);
    connection.on("MemberRemoved", onMemberRemoved);

    return () => {
      connection.off("ReceiveMessage", onMessage);
      connection.off("MessageDeleted", onDeleted);
      connection.off("PollUpdated", onPollUpdated);
      connection.off("ChatLockChanged", onLockChanged);
      connection.off("MemberRemoved", onMemberRemoved);
    };
  }, [connection, room.id, currentUser.userId]);

  function scrollToBottom() {
    if (scrollRef.current) scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
  }

  async function loadMore() {
    if (messages.length === 0) return;
    const oldest = messages[0].sentAt;
    const prevHeight = scrollRef.current?.scrollHeight || 0;
    const older = await getChatMessages(room.id, { before: oldest, pageSize: 30 });
    setMessages((prev) => [...older, ...prev]);
    setHasMore(older.length === 30);
    requestAnimationFrame(() => {
      if (scrollRef.current) scrollRef.current.scrollTop = scrollRef.current.scrollHeight - prevHeight;
    });
  }

  async function handleLeave() {
    if (!window.confirm(`Leave ${room.examName} group?`)) return;
    await leaveChatRoom(room.id);
    onBack();
  }

  if (wasKicked) {
    return (
      <div className="flex flex-col items-center gap-3 px-6 py-20 text-center">
        <p className="text-sm font-medium text-red-600">You've been removed from this group by an admin.</p>
        <button type="button" onClick={onBack} className="text-sm font-semibold text-secondary-500">Back to Group Chat</button>
      </div>
    );
  }

  return (
    <div className="flex h-[calc(100vh-64px)] flex-col lg:h-[calc(100vh-32px)]">
      <div className="flex items-center gap-3 border-b border-primary-100 bg-white px-4 py-3">
        <button type="button" onClick={onBack} className="text-ink-400 hover:text-ink-600">
          <ArrowLeft className="h-5 w-5" strokeWidth={2.25} />
        </button>
        {room.examLogoUrl ? (
          <img src={fileSrc(room.examLogoUrl)} alt="" className="h-9 w-9 rounded-full object-cover" />
        ) : (
          <span className="flex h-9 w-9 items-center justify-center rounded-full bg-secondary-50 text-xs font-bold text-secondary-500">
            {room.examName.slice(0, 2).toUpperCase()}
          </span>
        )}
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-bold text-ink-900">{room.examName}</p>
          <p className="text-xs text-ink-400">{room.memberCount} members {isDisabled && "· chat disabled"}</p>
        </div>
        <button type="button" onClick={handleLeave} className="text-xs font-semibold text-ink-400 hover:text-red-600">Leave</button>
      </div>

      <div ref={scrollRef} className="flex-1 overflow-y-auto bg-surface px-3 py-3 sm:px-4">
        {status === "loading" && <div className="flex justify-center py-10"><Loader2 className="h-5 w-5 animate-spin text-ink-400" strokeWidth={2.25} /></div>}
        {hasMore && status === "ready" && (
          <button type="button" onClick={loadMore} className="mx-auto mb-3 flex items-center gap-1 text-xs font-semibold text-secondary-500">
            <ChevronUp className="h-3.5 w-3.5" strokeWidth={2.5} />
            Load earlier messages
          </button>
        )}
        <div className="flex flex-col gap-2">
          {messages.map((m) => (
            <MessageBubble key={m.id} message={m} isOwn={m.senderType === "Student" && m.senderId === currentUser.userId} roomId={room.id} />
          ))}
        </div>
      </div>

      {isDisabled ? (
        <div className="border-t border-primary-100 bg-white px-4 py-3 text-center text-xs font-medium text-ink-400">
          <Lock className="mr-1 inline h-3.5 w-3.5" strokeWidth={2} />
          An admin has disabled this chat.
        </div>
      ) : (
        <Composer roomId={room.id} />
      )}
    </div>
  );
}

function MessageBubble({ message, isOwn, roomId }) {
  const [deleting, setDeleting] = useState(false);
  const [reporting, setReporting] = useState(false);
  const [reportReason, setReportReason] = useState("");

  async function handleDelete() {
    if (!window.confirm("Delete this message?")) return;
    setDeleting(true);
    try { await deleteOwnMessage(message.id); } finally { setDeleting(false); }
  }

  async function handleReport() {
    if (!reportReason.trim()) return;
    await reportChatMessage(message.id, reportReason.trim());
    setReporting(false);
    setReportReason("");
  }

  if (message.messageType === "Notice") {
    return (
      <div className="mx-auto flex max-w-md items-start gap-2 rounded-xl2 border-2 border-accent-500 bg-accent-50 px-3 py-2.5">
        <Megaphone className="mt-0.5 h-4 w-4 shrink-0 text-accent-600" strokeWidth={2.25} />
        <div>
          <p className="text-xs font-bold text-accent-700">{message.senderName} · Notice</p>
          <p className="mt-0.5 text-sm text-ink-900">{message.messageText}</p>
        </div>
      </div>
    );
  }

  return (
    <div className={`flex ${isOwn ? "justify-end" : "justify-start"}`}>
      <div className={`group max-w-[85%] rounded-xl2 px-3 py-2 sm:max-w-[70%] ${isOwn ? "bg-primary-600 text-white" : "bg-white text-ink-900 shadow-card"}`}>
        {!isOwn && (
          <p className={`text-xs font-bold ${message.senderType === "Admin" ? "text-accent-500" : "text-secondary-500"}`}>
            {message.senderName}{message.senderUsername ? ` @${message.senderUsername}` : ""}
          </p>
        )}

        {message.isDeleted ? (
          <p className={`text-sm italic ${isOwn ? "text-white/70" : "text-ink-400"}`}>Message deleted</p>
        ) : message.messageType === "Poll" && message.poll ? (
          <PollBubble poll={message.poll} isOwn={isOwn} />
        ) : (
          <>
            {message.messageText && <p className="whitespace-pre-wrap text-sm">{message.messageText}</p>}
            {message.messageType === "Image" && message.attachmentUrl && (
              <img src={fileSrc(message.attachmentUrl)} alt="" className="mt-1.5 max-h-60 rounded-lg" />
            )}
            {message.messageType === "Document" && message.attachmentUrl && (
              <a href={fileSrc(message.attachmentUrl)} target="_blank" rel="noreferrer" className={`mt-1.5 flex items-center gap-2 rounded-lg p-2 text-xs font-semibold ${isOwn ? "bg-white/10" : "bg-primary-50"}`}>
                <FileText className="h-4 w-4 shrink-0" strokeWidth={2} />
                Open document
              </a>
            )}
          </>
        )}

        <div className={`mt-1 flex items-center gap-2 text-[10px] ${isOwn ? "text-white/70" : "text-ink-400"}`}>
          {new Date(message.sentAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
          {!message.isDeleted && !isOwn && (
            <button type="button" onClick={() => setReporting((r) => !r)} className="opacity-0 group-hover:opacity-100">
              <Flag className="h-3 w-3" strokeWidth={2.25} />
            </button>
          )}
          {!message.isDeleted && isOwn && (
            <button type="button" onClick={handleDelete} disabled={deleting} className="opacity-0 group-hover:opacity-100">
              <Trash2 className="h-3 w-3" strokeWidth={2.25} />
            </button>
          )}
        </div>

        {reporting && (
          <div className="mt-2 flex gap-1.5">
            <input
              value={reportReason}
              onChange={(e) => setReportReason(e.target.value)}
              placeholder="Reason for reporting..."
              className="min-w-0 flex-1 rounded-lg border border-primary-100 px-2 py-1 text-xs text-ink-900"
            />
            <button type="button" onClick={handleReport} className="rounded-lg bg-red-500 px-2 py-1 text-xs font-semibold text-white">Send</button>
          </div>
        )}
      </div>
    </div>
  );
}

function PollBubble({ poll, isOwn }) {
  const [voting, setVoting] = useState(false);
  const totalVotes = poll.totalVotes || 0;

  async function handleVote(optionId) {
    setVoting(true);
    try { await votePoll(poll.id, [optionId]); } finally { setVoting(false); }
  }

  return (
    <div className="min-w-[220px]">
      <p className="flex items-center gap-1.5 text-sm font-bold"><BarChart3 className="h-4 w-4" strokeWidth={2.25} />{poll.question}</p>
      <div className="mt-2 flex flex-col gap-1.5">
        {poll.options.map((opt) => {
          const pct = totalVotes > 0 ? Math.round((opt.voteCount / totalVotes) * 100) : 0;
          return (
            <button
              key={opt.id}
              type="button"
              disabled={poll.isClosed || voting}
              onClick={() => handleVote(opt.id)}
              className={`relative overflow-hidden rounded-lg border px-2.5 py-1.5 text-left text-xs font-medium ${isOwn ? "border-white/30" : "border-primary-100"} disabled:cursor-default`}
            >
              <span className={`absolute inset-y-0 left-0 ${isOwn ? "bg-white/20" : "bg-secondary-50"}`} style={{ width: `${pct}%` }} />
              <span className="relative flex items-center justify-between gap-2">
                <span>{opt.optionText} {opt.hasCurrentUserVoted && "✓"}</span>
                <span className="shrink-0">{pct}%</span>
              </span>
            </button>
          );
        })}
      </div>
      <p className={`mt-1 text-[10px] ${isOwn ? "text-white/70" : "text-ink-400"}`}>
        {totalVotes} vote{totalVotes === 1 ? "" : "s"} {poll.isClosed && "· Closed"}
      </p>
    </div>
  );
}

function Composer({ roomId }) {
  const [text, setText] = useState("");
  const [attachment, setAttachment] = useState(null);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(null);
  const [mentionQuery, setMentionQuery] = useState(null); // string being typed after "@", or null
  const [mentionResults, setMentionResults] = useState([]);
  const fileInputRef = useRef(null);

  useEffect(() => {
    if (mentionQuery === null) { setMentionResults([]); return; }
    const controller = new AbortController();
    getMentionableUsers(roomId, mentionQuery, { signal: controller.signal }).then(setMentionResults).catch(() => {});
    return () => controller.abort();
  }, [mentionQuery, roomId]);

  function handleTextChange(value) {
    setText(value);
    const match = /@([a-z0-9._]*)$/i.exec(value);
    setMentionQuery(match ? match[1].toLowerCase() : null);
  }

  function pickMention(username) {
    setText((t) => t.replace(/@([a-z0-9._]*)$/i, `@${username} `));
    setMentionQuery(null);
  }

  async function handleSend(e) {
    e.preventDefault();
    if (!text.trim() && !attachment) return;
    setSending(true);
    setError(null);
    try {
      await sendChatMessage(roomId, { messageText: text.trim() || undefined, attachment });
      setText("");
      setAttachment(null);
      if (fileInputRef.current) fileInputRef.current.value = "";
    } catch (err) {
      setError(err.message);
    } finally {
      setSending(false);
    }
  }

  return (
    <form onSubmit={handleSend} className="relative border-t border-primary-100 bg-white p-3">
      {mentionResults.length > 0 && (
        <div className="absolute bottom-full left-3 mb-1 w-56 overflow-hidden rounded-xl2 border border-primary-100 bg-white shadow-card">
          {mentionResults.map((u) => (
            <button
              key={u.id}
              type="button"
              onClick={() => pickMention(u.username)}
              className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-primary-50"
            >
              <span className="font-semibold text-ink-900">@{u.username}</span>
              <span className="text-xs text-ink-400">{u.fullName}</span>
            </button>
          ))}
        </div>
      )}

      {attachment && (
        <div className="mb-2 flex items-center gap-2 rounded-lg bg-primary-50 px-2.5 py-1.5 text-xs font-medium text-primary-600">
          {attachment.type.startsWith("image/") ? <ImageIcon className="h-3.5 w-3.5" strokeWidth={2} /> : <FileText className="h-3.5 w-3.5" strokeWidth={2} />}
          <span className="min-w-0 flex-1 truncate">{attachment.name}</span>
          <button type="button" onClick={() => { setAttachment(null); if (fileInputRef.current) fileInputRef.current.value = ""; }}>
            <X className="h-3.5 w-3.5" strokeWidth={2.25} />
          </button>
        </div>
      )}
      {error && <p className="mb-2 text-xs font-medium text-red-600">{error}</p>}

      <div className="flex items-center gap-2">
        <label className="cursor-pointer text-ink-400 hover:text-ink-600">
          <Paperclip className="h-5 w-5" strokeWidth={2} />
          <input
            ref={fileInputRef}
            type="file"
            accept=".png,.jpg,.jpeg,.webp,.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx"
            className="hidden"
            onChange={(e) => setAttachment(e.target.files?.[0] || null)}
          />
        </label>
        <input
          value={text}
          onChange={(e) => handleTextChange(e.target.value)}
          placeholder="Message... use @ to mention someone"
          className="h-10 min-w-0 flex-1 rounded-xl2 border border-primary-100 px-3 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500"
        />
        <button
          type="submit"
          disabled={sending || (!text.trim() && !attachment)}
          className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl2 bg-primary-600 text-white disabled:opacity-50"
        >
          {sending ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.25} /> : <Send className="h-4 w-4" strokeWidth={2.25} />}
        </button>
      </div>
    </form>
  );
}
