import { useEffect, useRef, useState } from "react";
import {
  ArrowLeft, Send, Paperclip, Mic, X, FileText, ImageIcon, Loader2, ChevronUp, Play, Pause, Trash2,
} from "lucide-react";
import { useAuth } from "../../context/AuthContext";
import { useChatConnection } from "../../context/ChatConnectionContext";
import { getDirectMessages, sendDirectMessage, markConversationRead, deleteDirectMessage } from "../../api/directMessages";
import { API_BASE_URL } from "../../api/client";
import { Avatar } from "./ConversationsList";
import AudioRecorder from "./AudioRecorder";

function fileSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

export default function ConversationThread({ conversation, onBack, onMessageSent }) {
  const { user } = useAuth();
  const { connection } = useChatConnection();
  const [messages, setMessages] = useState([]);
  const [status, setStatus] = useState("loading");
  const [hasMore, setHasMore] = useState(true);
  const scrollRef = useRef(null);

  useEffect(() => {
    getDirectMessages(conversation.id, { pageSize: 30 })
      .then((data) => {
        setMessages(data);
        setHasMore(data.length === 30);
        setStatus("ready");
        requestAnimationFrame(scrollToBottom);
      })
      .catch(() => setStatus("error"));

    markConversationRead(conversation.id).catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [conversation.id]);

  useEffect(() => {
    if (!connection) return;

    const onReceive = (msg) => {
      if (msg.conversationId !== conversation.id) return;
      setMessages((prev) => (prev.some((m) => m.id === msg.id) ? prev : [...prev, msg]));
      requestAnimationFrame(scrollToBottom);
      if (msg.senderId !== user.userId) markConversationRead(conversation.id).catch(() => {});
    };

    connection.on("ReceiveDirectMessage", onReceive);

    const onDeleted = ({ messageId, conversationId }) => {
      if (conversationId !== conversation.id) return;
      setMessages((prev) => prev.map((m) => (m.id === messageId ? { ...m, isDeleted: true, messageText: null, attachmentUrl: null } : m)));
    };
    connection.on("DirectMessageDeleted", onDeleted);

    return () => {
      connection.off("ReceiveDirectMessage", onReceive);
      connection.off("DirectMessageDeleted", onDeleted);
    };
  }, [connection, conversation.id, user.userId]);

  async function handleDelete(messageId) {
    // Optimistic -- the sender's own view updates instantly, the hub event above handles the
    // other participant (and any of the sender's other open tabs/devices).
    setMessages((prev) => prev.map((m) => (m.id === messageId ? { ...m, isDeleted: true, messageText: null, attachmentUrl: null } : m)));
    try {
      await deleteDirectMessage(messageId);
    } catch {
      // Not worth reverting the optimistic update over -- worst case a refresh corrects it.
    }
  }

  function scrollToBottom() {
    if (scrollRef.current) scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
  }

  async function loadMore() {
    if (messages.length === 0) return;
    const oldest = messages[0].sentAt;
    const prevHeight = scrollRef.current?.scrollHeight || 0;
    const older = await getDirectMessages(conversation.id, { before: oldest, pageSize: 30 });
    setMessages((prev) => [...older, ...prev]);
    setHasMore(older.length === 30);
    requestAnimationFrame(() => {
      if (scrollRef.current) scrollRef.current.scrollTop = scrollRef.current.scrollHeight - prevHeight;
    });
  }

  function handleSent(msg) {
    // The REST response is the fastest path to showing your own message -- don't wait on the
    // SignalR echo (still arrives too, but onReceive's dedupe-by-id above makes that a no-op).
    setMessages((prev) => (prev.some((m) => m.id === msg.id) ? prev : [...prev, msg]));
    requestAnimationFrame(scrollToBottom);
    onMessageSent?.();
  }

  return (
    <div className="flex h-[calc(100vh-64px)] flex-col lg:h-[calc(100vh-32px)]">
      <div className="flex items-center gap-3 border-b border-primary-100 bg-white px-4 py-3">
        <button type="button" onClick={onBack} className="text-ink-400 hover:text-ink-600">
          <ArrowLeft className="h-5 w-5" strokeWidth={2.25} />
        </button>
        <Avatar photoUrl={conversation.otherPhotoUrl} fullName={conversation.otherFullName} size="h-9 w-9" />
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-bold text-ink-900">{conversation.otherFullName}</p>
          <p className="text-xs text-ink-400">@{conversation.otherUsername}</p>
        </div>
      </div>

      <div ref={scrollRef} className="flex-1 overflow-y-auto bg-surface px-3 py-3 sm:px-4">
        {status === "loading" && (
          <div className="flex justify-center py-10">
            <Loader2 className="h-5 w-5 animate-spin text-ink-400" strokeWidth={2.25} />
          </div>
        )}
        {status === "error" && <p className="py-10 text-center text-sm text-red-600">Couldn't load this conversation.</p>}
        {hasMore && status === "ready" && (
          <button type="button" onClick={loadMore} className="mx-auto mb-3 flex items-center gap-1 text-xs font-semibold text-secondary-500">
            <ChevronUp className="h-3.5 w-3.5" strokeWidth={2.5} />
            Load earlier messages
          </button>
        )}
        <div className="flex flex-col gap-2">
          {messages.map((m) => (
            <MessageBubble key={m.id} message={m} isOwn={m.senderId === user.userId} onDelete={handleDelete} />
          ))}
        </div>
      </div>

      <Composer conversationId={conversation.id} onSent={handleSent} />
    </div>
  );
}

function MessageBubble({ message, isOwn, onDelete }) {
  const [confirming, setConfirming] = useState(false);

  return (
    <div className={`group flex items-end gap-1.5 ${isOwn ? "justify-end" : "justify-start"}`}>
      {isOwn && !message.isDeleted && (
        <span className="mb-1 opacity-0 transition-opacity group-hover:opacity-100">
          {confirming ? (
            <span className="flex items-center gap-1">
              <button
                type="button"
                onClick={() => {
                  onDelete(message.id);
                  setConfirming(false);
                }}
                className="rounded-md bg-red-500 px-1.5 py-1 text-[10px] font-bold text-white"
              >
                Unsend
              </button>
              <button type="button" onClick={() => setConfirming(false)} className="text-ink-400 hover:text-ink-600">
                <X className="h-3.5 w-3.5" strokeWidth={2.25} />
              </button>
            </span>
          ) : (
            <button
              type="button"
              onClick={() => setConfirming(true)}
              aria-label="Delete message"
              className="text-ink-300 hover:text-red-500"
            >
              <Trash2 className="h-3.5 w-3.5" strokeWidth={2} />
            </button>
          )}
        </span>
      )}
      <div className={`max-w-[85%] rounded-xl2 px-3 py-2 sm:max-w-[70%] ${isOwn ? "bg-primary-600 text-white" : "bg-white text-ink-900 shadow-card"}`}>
        {message.isDeleted ? (
          <p className={`text-sm italic ${isOwn ? "text-white/70" : "text-ink-400"}`}>Message deleted</p>
        ) : (
          <>
            {message.messageText && <p className="whitespace-pre-wrap text-sm">{message.messageText}</p>}
            {message.messageType === "Image" && message.attachmentUrl && (
              <img src={fileSrc(message.attachmentUrl)} alt="" className="mt-1.5 max-h-60 rounded-lg" />
            )}
            {message.messageType === "Document" && message.attachmentUrl && (
              <a
                href={fileSrc(message.attachmentUrl)}
                target="_blank"
                rel="noreferrer"
                className={`mt-1.5 flex items-center gap-2 rounded-lg p-2 text-xs font-semibold ${isOwn ? "bg-white/10" : "bg-primary-50"}`}
              >
                <FileText className="h-4 w-4 shrink-0" strokeWidth={2} />
                Open document
              </a>
            )}
            {message.messageType === "Audio" && message.attachmentUrl && (
              <VoiceNoteBubble url={fileSrc(message.attachmentUrl)} durationSeconds={message.attachmentDurationSeconds} isOwn={isOwn} />
            )}
          </>
        )}
        <p className={`mt-1 text-[10px] ${isOwn ? "text-white/70" : "text-ink-400"}`}>
          {new Date(message.sentAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
        </p>
      </div>
    </div>
  );
}

function VoiceNoteBubble({ url, durationSeconds, isOwn }) {
  const audioRef = useRef(null);
  const [playing, setPlaying] = useState(false);

  function toggle() {
    if (!audioRef.current) return;
    if (playing) audioRef.current.pause();
    else audioRef.current.play();
  }

  const m = Math.floor((durationSeconds || 0) / 60);
  const s = (durationSeconds || 0) % 60;

  return (
    <div className="mt-1 flex min-w-[180px] items-center gap-2.5">
      <button
        type="button"
        onClick={toggle}
        aria-label={playing ? "Pause voice note" : "Play voice note"}
        className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full ${isOwn ? "bg-white/15" : "bg-primary-50"}`}
      >
        {playing ? <Pause className="h-3.5 w-3.5" strokeWidth={2.5} /> : <Play className="h-3.5 w-3.5 pl-0.5" strokeWidth={2.5} />}
      </button>
      <span className="flex-1 text-xs font-medium tabular-nums">
        {m}:{String(s).padStart(2, "0")}
      </span>
      <audio
        ref={audioRef}
        src={url}
        onPlay={() => setPlaying(true)}
        onPause={() => setPlaying(false)}
        onEnded={() => setPlaying(false)}
        className="hidden"
      />
    </div>
  );
}

function Composer({ conversationId, onSent }) {
  const [text, setText] = useState("");
  const [attachment, setAttachment] = useState(null);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(null);
  const [recording, setRecording] = useState(false);
  const fileInputRef = useRef(null);

  async function handleSend(e) {
    e.preventDefault();
    if (!text.trim() && !attachment) return;
    setSending(true);
    setError(null);
    try {
      const msg = await sendDirectMessage(conversationId, { messageText: text.trim() || undefined, attachment });
      setText("");
      setAttachment(null);
      if (fileInputRef.current) fileInputRef.current.value = "";
      onSent(msg);
    } catch (err) {
      setError(err.message);
    } finally {
      setSending(false);
    }
  }

  async function handleVoiceNote(file, durationSeconds) {
    setError(null);
    try {
      const msg = await sendDirectMessage(conversationId, { attachment: file, attachmentDurationSeconds: durationSeconds });
      setRecording(false);
      onSent(msg);
    } catch (err) {
      setError(err.message);
      setRecording(false);
    }
  }

  if (recording) {
    return (
      <div className="border-t border-primary-100 bg-white p-3">
        <AudioRecorder onSend={handleVoiceNote} onClose={() => setRecording(false)} />
      </div>
    );
  }

  return (
    <form onSubmit={handleSend} className="border-t border-primary-100 bg-white p-3">
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
          onChange={(e) => setText(e.target.value)}
          placeholder="Message..."
          className="h-10 min-w-0 flex-1 rounded-xl2 border border-primary-100 px-3 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500"
        />
        {text.trim() || attachment ? (
          <button
            type="submit"
            disabled={sending}
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl2 bg-primary-600 text-white disabled:opacity-50"
          >
            {sending ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.25} /> : <Send className="h-4 w-4" strokeWidth={2.25} />}
          </button>
        ) : (
          <button
            type="button"
            onClick={() => setRecording(true)}
            aria-label="Record a voice note"
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl2 bg-primary-50 text-primary-600 hover:bg-primary-100"
          >
            <Mic className="h-[18px] w-[18px]" strokeWidth={2.25} />
          </button>
        )}
      </div>
    </form>
  );
}
