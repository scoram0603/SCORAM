import { useEffect, useRef, useState } from "react";
import { useNavigate, useSearchParams, Link } from "react-router-dom";
import {
  Users, Lock, Send, Paperclip, X, Flag, Trash2, ArrowLeft, FileText, ImageIcon,
  BarChart3, Megaphone, Loader2, ChevronUp, MessageCircle, Search, Share2, HelpCircle,
} from "lucide-react";
import { useAuth } from "../context/AuthContext";
import { useChatConnection } from "../context/ChatConnectionContext";
import {
  listChatRooms, joinChatRoom, leaveChatRoom, getChatMessages, sendChatMessage,
  deleteOwnMessage, reportChatMessage, getMentionableUsers, votePoll, getOnlineUsers,
} from "../api/chat";
import { API_BASE_URL } from "../api/client";
import ConversationsList from "../components/directMessages/ConversationsList";
import ConversationThread from "../components/directMessages/ConversationThread";
import RoomInfoPanel from "../components/chat/RoomInfoPanel";
import NotificationBell from "../components/layout/NotificationBell";

function fileSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

// PREMIUM UI PASS -- true 3-pane desktop layout (list | active room or DM | info panel), matching
// the reference design, while keeping mobile's existing "pick from a list, then go full-screen"
// pattern untouched. Both are the SAME JSX tree at once now (not two different code paths chosen by
// screen size in JS) -- Tailwind's responsive classes decide which panes are visible at which
// breakpoint, and RoomChatView/ConversationThread's own internal logic (SignalR wiring, message
// fetch/send, presence) is completely unchanged from before this pass, only their surrounding shell
// and some inline styling moved.
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

  function switchTab(nextTab) {
    setSearchParams(nextTab === "rooms" ? {} : { tab: nextTab });
  }

  const detailOpen = Boolean(selectedRoom || selectedConversation);

  return (
    <div className="lg:flex lg:h-[calc(100vh-32px)] lg:flex-col">
      {/* ---------- Top bar -- desktop only; mobile keeps its existing shared Header ---------- */}
      <div className="hidden shrink-0 items-center justify-between border-b border-primary-100 bg-white px-6 py-4 lg:flex">
        <div className="flex items-center gap-2.5">
          <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-primary-50 text-primary-600">
            <MessageCircle className="h-5 w-5" strokeWidth={2.25} />
          </span>
          <div>
            <h1 className="text-lg font-extrabold leading-tight text-ink-900">Chat &amp; Connect</h1>
            <p className="text-xs text-ink-400">Discuss, share, and learn together.</p>
          </div>
        </div>
        <div className="flex items-center gap-4">
          <NotificationBell />
          <Link to="/profile" className="flex items-center gap-2">
            {user?.photoUrl ? (
              <img src={fileSrc(user.photoUrl)} alt="" className="h-8 w-8 rounded-full object-cover" />
            ) : (
              <span className="flex h-8 w-8 items-center justify-center rounded-full bg-primary-600 text-xs font-bold text-white">
                {(user?.fullName || "?").slice(0, 1).toUpperCase()}
              </span>
            )}
            <span className="text-sm font-semibold text-ink-900">{user?.fullName}</span>
          </Link>
        </div>
      </div>

      <div className="lg:flex lg:flex-1 lg:overflow-hidden">
        {/* ---------- List panel (Communities / Direct Messages) ---------- */}
        <div className={`${detailOpen ? "hidden lg:flex" : "flex"} flex-col lg:w-96 lg:shrink-0 lg:border-r lg:border-primary-100`}>
          <div className="px-4 pt-4 sm:px-6 lg:px-5 lg:pt-5">
            <h1 className="text-xl font-extrabold text-ink-900 sm:text-2xl lg:hidden">Chat</h1>
            <p className="mt-1 text-sm text-ink-400 lg:hidden">
              {tab === "rooms" ? "One chat room per exam. Joining is optional." : "Message any student directly, like a DM."}
            </p>

            <div className="mt-4 inline-flex rounded-xl2 border border-primary-100 bg-white p-1 lg:mt-0">
              <TabButton active={tab === "rooms"} onClick={() => switchTab("rooms")} icon={Users}>Communities</TabButton>
              <TabButton active={tab === "messages"} onClick={() => switchTab("messages")} icon={MessageCircle}>Direct Messages</TabButton>
            </div>
          </div>

          <div className="lg:flex-1 lg:overflow-y-auto">
            {tab === "rooms" ? (
              <RoomsList onOpenRoom={setSelectedRoom} />
            ) : (
              <ConversationsList onOpenConversation={setSelectedConversation} refreshSignal={dmRefreshSignal} />
            )}
          </div>
        </div>

        {/* ---------- Detail panel (active room or DM thread) ---------- */}
        <div className={`${detailOpen ? "flex" : "hidden lg:flex"} flex-1 flex-col lg:min-w-0`}>
          {selectedRoom && <RoomChatView room={selectedRoom} currentUser={user} onBack={() => setSelectedRoom(null)} />}
          {selectedConversation && (
            <ConversationThread
              conversation={selectedConversation}
              onBack={() => setSelectedConversation(null)}
              onMessageSent={() => setDmRefreshSignal((s) => s + 1)}
            />
          )}
          {!detailOpen && (
            <div className="hidden flex-1 flex-col items-center justify-center gap-2 text-ink-300 lg:flex">
              <MessageCircle className="h-10 w-10" strokeWidth={1.5} />
              <p className="text-sm text-ink-400">Select a community or conversation to start chatting.</p>
            </div>
          )}
        </div>

        {/* ---------- Info panel (rooms only, xl+ screens) ---------- */}
        {selectedRoom && <RoomInfoPanel room={selectedRoom} />}
      </div>
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
  const [query, setQuery] = useState("");
  const debounceRef = useRef(null);

  useEffect(() => {
    refresh(query);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // GROUP CHAT FIX -- debounced search-as-you-type, same 300ms pattern ConversationsList uses for
  // the DM user search. With an empty query the backend only returns featured + already-joined
  // rooms (see ChatController.ListRooms) -- typing an exam name searches every room by name,
  // which is how a Question-Bank-generated exam room (not featured by default) gets found.
  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => refresh(query), 300);
    return () => clearTimeout(debounceRef.current);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query]);

  function refresh(searchText) {
    setStatus("loading");
    listChatRooms(searchText ? { search: searchText } : {})
      .then((data) => { setRooms(data); setStatus("ready"); })
      .catch(() => setStatus("error"));
  }

  async function handleJoin(room) {
    setJoiningId(room.id);
    try {
      await joinChatRoom(room.id);
      refresh(query);
    } catch {
      // keep it simple -- refresh() below will reflect whatever the true state ended up being
      refresh(query);
    } finally {
      setJoiningId(null);
    }
  }

  const myRooms = rooms.filter((r) => r.isMember);
  const otherRooms = rooms.filter((r) => !r.isMember);

  return (
    <div className="px-4 pb-8 pt-4 sm:px-6 lg:px-3">
      <div className="relative">
        <Search className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-300" strokeWidth={2.25} />
        <input
          type="text"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Search communities..."
          className="w-full rounded-xl2 border border-primary-100 bg-white py-2.5 pl-10 pr-3.5 text-sm text-ink-900 placeholder:text-ink-300 focus:border-primary-300 focus:outline-none"
        />
      </div>

      {status === "loading" && (
        <div className="flex justify-center py-16"><Loader2 className="h-6 w-6 animate-spin text-ink-400" strokeWidth={2.25} /></div>
      )}
      {status === "error" && <p className="py-16 text-center text-sm text-red-600">Couldn't load rooms right now.</p>}

      {status === "ready" && rooms.length === 0 && (
        <p className="py-16 text-center text-sm text-ink-400">
          {query.trim() ? `No room matches "${query.trim()}".` : "No rooms yet -- search for your exam above."}
        </p>
      )}

      {myRooms.length > 0 && (
        <div className="mt-5">
          <p className="px-2 text-[11px] font-bold uppercase tracking-wide text-ink-400">My Communities</p>
          <div className="mt-2 flex flex-col gap-1">
            {myRooms.map((room) => <RoomRow key={room.id} room={room} onOpenRoom={onOpenRoom} onJoin={handleJoin} joining={joiningId === room.id} />)}
          </div>
        </div>
      )}

      {otherRooms.length > 0 && (
        <div className="mt-5">
          <p className="px-2 text-[11px] font-bold uppercase tracking-wide text-ink-400">Other Communities</p>
          <div className="mt-2 flex flex-col gap-1">
            {otherRooms.map((room) => <RoomRow key={room.id} room={room} onOpenRoom={onOpenRoom} onJoin={handleJoin} joining={joiningId === room.id} />)}
          </div>
        </div>
      )}
    </div>
  );
}

function RoomRow({ room, onOpenRoom, onJoin, joining }) {
  const content = (
    <>
      <span className="relative shrink-0">
        {room.iconUrl ? (
          <img src={fileSrc(room.iconUrl)} alt="" className="h-11 w-11 rounded-full object-cover" />
        ) : (
          <span className="flex h-11 w-11 items-center justify-center rounded-full bg-secondary-50 text-sm font-bold text-secondary-500">
            {room.examName.slice(0, 2).toUpperCase()}
          </span>
        )}
        {room.isChatDisabled && (
          <span className="absolute -bottom-0.5 -right-0.5 flex h-4 w-4 items-center justify-center rounded-full border-2 border-white bg-ink-400">
            <Lock className="h-2 w-2 text-white" strokeWidth={3} />
          </span>
        )}
      </span>
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-bold text-ink-900">{room.examName}</p>
        <p className="flex items-center gap-1 text-xs text-ink-400">
          {room.memberCount} member{room.memberCount === 1 ? "" : "s"}
          {room.onlineCount > 0 && (
            <span className="flex items-center gap-1 text-mint-600">
              <span className="mx-0.5">·</span>
              <span className="h-1.5 w-1.5 rounded-full bg-mint-500" />
              {room.onlineCount} online
            </span>
          )}
        </p>
      </div>
    </>
  );

  if (room.isBanned) {
    return (
      <div className="flex items-center gap-3 rounded-xl2 px-2 py-2 opacity-60">
        {content}
        <span className="shrink-0 text-[11px] font-semibold text-red-500">Removed</span>
      </div>
    );
  }

  if (room.isMember) {
    return (
      <button type="button" onClick={() => onOpenRoom(room)} className="flex items-center gap-3 rounded-xl2 px-2 py-2 text-left hover:bg-primary-50">
        {content}
      </button>
    );
  }

  return (
    <div className="flex items-center gap-3 rounded-xl2 px-2 py-2 hover:bg-primary-50">
      {content}
      <button
        type="button"
        onClick={() => onJoin(room)}
        disabled={joining}
        className="shrink-0 rounded-lg bg-primary-50 px-3 py-1.5 text-xs font-semibold text-primary-600 hover:bg-primary-100 disabled:opacity-60"
      >
        {joining ? "Joining…" : "Join"}
      </button>
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
  const [onlineUsers, setOnlineUsers] = useState([]);
  const [showOnlineList, setShowOnlineList] = useState(false);
  const [dismissedNoticeId, setDismissedNoticeId] = useState(null);
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

  // GROUP CHAT -- "Online user list". Fetch the current snapshot once on open (REST), then the hub's
  // JoinRoomGroup call below both registers this connection's presence AND -- via the "PresenceUpdated"
  // listener further down -- keeps the list live from then on, including other students joining/
  // leaving the screen while this one stays open.
  useEffect(() => {
    getOnlineUsers(room.id).then(setOnlineUsers).catch(() => {});
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
    const onPresenceUpdated = (data) => {
      if (data.roomId === room.id) setOnlineUsers(data.onlineUsers);
    };

    connection.on("ReceiveMessage", onMessage);
    connection.on("MessageDeleted", onDeleted);
    connection.on("PollUpdated", onPollUpdated);
    connection.on("ChatLockChanged", onLockChanged);
    connection.on("MemberRemoved", onMemberRemoved);
    connection.on("PresenceUpdated", onPresenceUpdated);

    return () => {
      connection.off("ReceiveMessage", onMessage);
      connection.off("MessageDeleted", onDeleted);
      connection.off("PollUpdated", onPollUpdated);
      connection.off("ChatLockChanged", onLockChanged);
      connection.off("MemberRemoved", onMemberRemoved);
      connection.off("PresenceUpdated", onPresenceUpdated);
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

  // PREMIUM UI -- the most recent admin Notice becomes a dismissible sticky banner just below the
  // header, in addition to appearing in its normal chronological spot in the message list -- so it
  // stays visible without scrolling back through history to find it. Dismissal is session-only
  // (component state, not persisted) since there's no backend "seen this notice" tracking.
  const activeNotices = messages.filter((m) => m.messageType === "Notice" && !m.isDeleted);
  const latestNotice = activeNotices[activeNotices.length - 1];
  const showNoticeBanner = latestNotice && latestNotice.id !== dismissedNoticeId;

  if (wasKicked) {
    return (
      <div className="flex flex-col items-center gap-3 px-6 py-20 text-center">
        <p className="text-sm font-medium text-red-600">You've been removed from this group by an admin.</p>
        <button type="button" onClick={onBack} className="text-sm font-semibold text-secondary-500">Back to Group Chat</button>
      </div>
    );
  }

  return (
    <div className="flex h-[calc(100vh-64px)] flex-col lg:h-full">
      <div className="flex items-center gap-3 border-b border-primary-100 bg-white px-4 py-3">
        <button type="button" onClick={onBack} className="text-ink-400 hover:text-ink-600">
          <ArrowLeft className="h-5 w-5" strokeWidth={2.25} />
        </button>
        {room.iconUrl ? (
          <img src={fileSrc(room.iconUrl)} alt="" className="h-9 w-9 rounded-full object-cover" />
        ) : (
          <span className="flex h-9 w-9 items-center justify-center rounded-full bg-secondary-50 text-xs font-bold text-secondary-500">
            {room.examName.slice(0, 2).toUpperCase()}
          </span>
        )}
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-bold text-ink-900">{room.examName}</p>
          <p className="text-xs text-ink-400">{room.memberCount} members {isDisabled && "· chat disabled"}</p>
        </div>
        {onlineUsers.length > 0 && (
          <button
            type="button"
            onClick={() => setShowOnlineList((s) => !s)}
            className="flex shrink-0 items-center gap-1.5 rounded-full bg-mint-50 px-2.5 py-1.5 text-xs font-semibold text-mint-600 xl:hidden"
          >
            <span className="h-1.5 w-1.5 rounded-full bg-mint-500" />
            {onlineUsers.length} online
          </button>
        )}
        <button type="button" onClick={handleLeave} className="text-xs font-semibold text-ink-400 hover:text-red-600">Leave</button>
      </div>

      {showOnlineList && onlineUsers.length > 0 && (
        <div className="border-b border-primary-100 bg-white px-4 py-2.5 xl:hidden">
          <div className="flex flex-wrap gap-2">
            {onlineUsers.map((u) => (
              <span key={u.id} className="flex items-center gap-1.5 rounded-full bg-surface py-1 pl-1 pr-2.5">
                <Avatar photoUrl={u.photoUrl} name={u.fullName} size="h-5 w-5" />
                <span className="text-xs font-medium text-ink-600">{u.fullName}</span>
              </span>
            ))}
          </div>
        </div>
      )}

      <div ref={scrollRef} className="flex-1 overflow-y-auto bg-surface px-3 py-3 sm:px-4">
        {showNoticeBanner && (
          <div className="mb-3 flex items-start gap-2.5 rounded-xl2 border border-accent-100 bg-accent-50 px-3.5 py-3">
            <Megaphone className="mt-0.5 h-4 w-4 shrink-0 text-accent-600" strokeWidth={2.25} />
            <div className="min-w-0 flex-1">
              <p className="text-xs font-bold text-accent-700">Announcement</p>
              <p className="mt-0.5 text-sm text-ink-900">{latestNotice.messageText}</p>
            </div>
            <button type="button" onClick={() => setDismissedNoticeId(latestNotice.id)} className="shrink-0 text-accent-500 hover:text-accent-700">
              <X className="h-4 w-4" strokeWidth={2.25} />
            </button>
          </div>
        )}
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

function photoSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

function initialsFor(name) {
  if (!name) return "?";
  const parts = name.trim().split(/\s+/);
  return ((parts[0]?.[0] || "") + (parts[1]?.[0] || "")).toUpperCase() || name[0].toUpperCase();
}

// GROUP CHAT FIX -- profile pic shows wherever a sender's identity appears, not just DMs. Small
// (28px) avatar used next to non-own message bubbles and in the mention autocomplete list.
function Avatar({ photoUrl, name, size = "h-7 w-7" }) {
  return photoUrl ? (
    <img src={photoSrc(photoUrl)} alt={name} className={`${size} shrink-0 rounded-full object-cover`} />
  ) : (
    <span className={`flex ${size} shrink-0 items-center justify-center rounded-full bg-primary-600 text-[10px] font-bold text-white`}>
      {initialsFor(name)}
    </span>
  );
}

const URL_PATTERN = /(https?:\/\/[^\s]+)/g;

// GROUP CHAT FIX -- auto-linkify any URL typed into a plain-text message. Applies to every message
// (not just Scoram question shares, which get their own dedicated card -- see QuestionShareCard
// below) since the ask was "links clickable honi chahiye" in general, not just Scoram's own links.
function Linkify({ text, className }) {
  const parts = text.split(URL_PATTERN);
  return (
    <p className={className}>
      {parts.map((part, i) =>
        URL_PATTERN.test(part) ? (
          <a key={i} href={part} target="_blank" rel="noreferrer" className="underline underline-offset-2 hover:opacity-80">
            {part}
          </a>
        ) : (
          <span key={i}>{part}</span>
        )
      )}
    </p>
  );
}

function QuestionShareCard({ message, isOwn }) {
  const content = (
    <div className={`flex items-start gap-2 rounded-lg p-2.5 ${isOwn ? "bg-white/10" : "bg-primary-50"}`}>
      <Share2 className={`mt-0.5 h-4 w-4 shrink-0 ${isOwn ? "text-white/80" : "text-primary-500"}`} strokeWidth={2.25} />
      <div className="min-w-0">
        {message.sharedQuestionExamName && (
          <p className={`text-[10px] font-bold uppercase tracking-wide ${isOwn ? "text-white/70" : "text-primary-500"}`}>
            {message.sharedQuestionExamName}
          </p>
        )}
        <p className="mt-0.5 line-clamp-3 text-sm">{message.messageText}</p>
        {!message.questionExists && (
          <p className={`mt-1 text-[11px] italic ${isOwn ? "text-white/60" : "text-ink-400"}`}>This question is no longer available.</p>
        )}
      </div>
    </div>
  );

  return message.questionExists ? (
    <a href={`/question-bank/${message.sharedQuestionId}`} target="_blank" rel="noreferrer" className="block hover:opacity-90">
      {content}
    </a>
  ) : (
    content
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
      <div className="mx-auto flex max-w-md items-start gap-2 rounded-xl2 border border-accent-100 bg-accent-50 px-3 py-2.5">
        <Megaphone className="mt-0.5 h-4 w-4 shrink-0 text-accent-600" strokeWidth={2.25} />
        <div>
          <p className="text-xs font-bold text-accent-700">{message.senderName} · Notice</p>
          <p className="mt-0.5 text-sm text-ink-900">{message.messageText}</p>
        </div>
      </div>
    );
  }

  return (
    <div className={`flex items-end gap-2 ${isOwn ? "justify-end" : "justify-start"}`}>
      {!isOwn && <Avatar photoUrl={message.senderPhotoUrl} name={message.senderName} />}
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
        ) : message.messageType === "QuestionShare" ? (
          <QuestionShareCard message={message} isOwn={isOwn} />
        ) : (
          <>
            {message.messageText && <Linkify text={message.messageText} className="whitespace-pre-wrap text-sm" />}
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
              <Avatar photoUrl={u.photoUrl} name={u.fullName} size="h-6 w-6" />
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
