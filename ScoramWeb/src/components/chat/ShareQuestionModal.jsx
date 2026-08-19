import { useEffect, useState } from "react";
import { X, Share2, Loader2, CheckCircle2, Search, Copy, Check, Users, MessageCircle, Link2 } from "lucide-react";
import { listChatRooms, shareQuestionToChat } from "../../api/chat";
import { searchUsers, shareQuestionToDm, startConversation } from "../../api/directMessages";
import { API_BASE_URL } from "../../api/client";

function fileSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

function initialsFor(name) {
  if (!name) return "?";
  const parts = name.trim().split(/\s+/);
  return ((parts[0]?.[0] || "") + (parts[1]?.[0] || "")).toUpperCase() || name[0].toUpperCase();
}

const TABS = [
  { key: "groups", label: "Group Chat", icon: Users },
  { key: "dm", label: "Message", icon: MessageCircle },
  { key: "link", label: "Copy Link", icon: Link2 },
];

// SHARING -- one modal, three ways to share a Question Bank question: into a joined group chat room,
// as a DM to another student, or a plain shareable link. Replaces the earlier group-only
// ShareToChatModal now that DM sharing (DirectMessagesController.ShareQuestion) exists too.
export default function ShareQuestionModal({ questionId, open, onClose }) {
  const [tab, setTab] = useState("groups");

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/40 sm:items-center" onClick={onClose}>
      <div
        className="max-h-[75vh] w-full max-w-sm overflow-hidden rounded-t-2xl bg-white sm:rounded-xl2"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between p-4 pb-0">
          <h3 className="flex items-center gap-1.5 text-sm font-bold text-ink-900">
            <Share2 className="h-4 w-4" strokeWidth={2.25} />
            Share Question
          </h3>
          <button type="button" onClick={onClose} className="text-ink-400 hover:text-ink-600">
            <X className="h-5 w-5" strokeWidth={2.25} />
          </button>
        </div>

        <div className="mt-3 flex gap-1.5 px-4">
          {TABS.map((t) => (
            <button
              key={t.key}
              type="button"
              onClick={() => setTab(t.key)}
              className={`flex flex-1 items-center justify-center gap-1.5 rounded-lg py-2 text-xs font-semibold transition-colors ${
                tab === t.key ? "bg-primary-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"
              }`}
            >
              <t.icon className="h-3.5 w-3.5" strokeWidth={2.25} />
              {t.label}
            </button>
          ))}
        </div>

        <div className="max-h-[55vh] overflow-y-auto p-4">
          {tab === "groups" && <ShareToGroupsTab questionId={questionId} />}
          {tab === "dm" && <ShareToDmTab questionId={questionId} />}
          {tab === "link" && <CopyLinkTab questionId={questionId} />}
        </div>
      </div>
    </div>
  );
}

function ShareToGroupsTab({ questionId }) {
  const [rooms, setRooms] = useState([]);
  const [status, setStatus] = useState("loading");
  const [sharingId, setSharingId] = useState(null);
  const [sharedId, setSharedId] = useState(null);

  useEffect(() => {
    listChatRooms()
      .then((data) => {
        setRooms(data.filter((r) => r.isMember));
        setStatus("ready");
      })
      .catch(() => setStatus("error"));
  }, []);

  async function handleShare(roomId) {
    setSharingId(roomId);
    try {
      await shareQuestionToChat(roomId, questionId);
      setSharedId(roomId);
    } catch {
      // room stays selectable -- the student can just try again
    } finally {
      setSharingId(null);
    }
  }

  if (status === "loading") return <div className="flex justify-center py-8"><Loader2 className="h-5 w-5 animate-spin text-ink-400" strokeWidth={2.25} /></div>;
  if (status === "error") return <p className="py-6 text-center text-sm text-red-600">Couldn't load your groups right now.</p>;
  if (rooms.length === 0) return <p className="py-6 text-center text-sm text-ink-400">Join a group from the Chat tab first, then you can share questions into it.</p>;

  return (
    <div className="flex flex-col gap-1.5">
      {rooms.map((room) => (
        <button
          key={room.id}
          type="button"
          onClick={() => handleShare(room.id)}
          disabled={sharingId === room.id || sharedId === room.id}
          className="flex items-center gap-3 rounded-lg px-2.5 py-2 text-left hover:bg-primary-50 disabled:opacity-70"
        >
          {room.iconUrl ? (
            <img src={fileSrc(room.iconUrl)} alt="" className="h-9 w-9 rounded-full object-cover" />
          ) : (
            <span className="flex h-9 w-9 items-center justify-center rounded-full bg-secondary-50 text-xs font-bold text-secondary-500">
              {room.examName.slice(0, 2).toUpperCase()}
            </span>
          )}
          <span className="min-w-0 flex-1 truncate text-sm font-semibold text-ink-900">{room.examName}</span>
          {sharedId === room.id ? (
            <CheckCircle2 className="h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.25} />
          ) : sharingId === room.id ? (
            <Loader2 className="h-4 w-4 shrink-0 animate-spin text-ink-400" strokeWidth={2.25} />
          ) : null}
        </button>
      ))}
    </div>
  );
}

function ShareToDmTab({ questionId }) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState([]);
  const [searching, setSearching] = useState(false);
  const [sharingId, setSharingId] = useState(null);
  const [sharedId, setSharedId] = useState(null);

  useEffect(() => {
    if (!query.trim()) {
      setResults([]);
      return;
    }
    const controller = new AbortController();
    setSearching(true);
    const timer = setTimeout(() => {
      searchUsers(query.trim(), { signal: controller.signal })
        .then(setResults)
        .catch(() => {})
        .finally(() => setSearching(false));
    }, 300);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [query]);

  async function handleShare(user) {
    setSharingId(user.id);
    try {
      const conversation = await startConversation(user.id);
      await shareQuestionToDm(conversation.id, questionId);
      setSharedId(user.id);
    } catch {
      // user stays selectable -- the student can just try again
    } finally {
      setSharingId(null);
    }
  }

  return (
    <div>
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-300" strokeWidth={2.25} />
        <input
          type="text"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Search by username or name..."
          autoFocus
          className="w-full rounded-lg border border-primary-100 bg-white py-2 pl-9 pr-3 text-sm text-ink-900 placeholder:text-ink-300 focus:border-primary-300 focus:outline-none"
        />
      </div>

      <div className="mt-3 flex flex-col gap-1.5">
        {searching && <div className="flex justify-center py-4"><Loader2 className="h-4 w-4 animate-spin text-ink-400" strokeWidth={2.25} /></div>}
        {!searching && query.trim() && results.length === 0 && (
          <p className="py-4 text-center text-sm text-ink-400">No students match "{query.trim()}".</p>
        )}
        {results.map((user) => (
          <button
            key={user.id}
            type="button"
            onClick={() => handleShare(user)}
            disabled={sharingId === user.id || sharedId === user.id}
            className="flex items-center gap-3 rounded-lg px-2.5 py-2 text-left hover:bg-primary-50 disabled:opacity-70"
          >
            {user.photoUrl ? (
              <img src={fileSrc(user.photoUrl)} alt="" className="h-9 w-9 rounded-full object-cover" />
            ) : (
              <span className="flex h-9 w-9 items-center justify-center rounded-full bg-primary-600 text-xs font-bold text-white">
                {initialsFor(user.fullName)}
              </span>
            )}
            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-semibold text-ink-900">{user.fullName}</span>
              <span className="block truncate text-xs text-ink-400">@{user.username}</span>
            </span>
            {sharedId === user.id ? (
              <CheckCircle2 className="h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.25} />
            ) : sharingId === user.id ? (
              <Loader2 className="h-4 w-4 shrink-0 animate-spin text-ink-400" strokeWidth={2.25} />
            ) : null}
          </button>
        ))}
      </div>
    </div>
  );
}

function CopyLinkTab({ questionId }) {
  const [copied, setCopied] = useState(false);
  const link = `${window.location.origin}/question-bank/${questionId}`;

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(link);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard API unavailable -- the link is still visible in the input to copy manually.
    }
  }

  return (
    <div className="py-2">
      <p className="text-xs text-ink-400">Anyone with this link can open the question directly.</p>
      <div className="mt-2 flex items-center gap-2">
        <input
          type="text"
          readOnly
          value={link}
          onFocus={(e) => e.target.select()}
          className="flex-1 truncate rounded-lg border border-primary-100 bg-surface px-3 py-2 text-xs text-ink-600"
        />
        <button
          type="button"
          onClick={handleCopy}
          className="flex shrink-0 items-center gap-1.5 rounded-lg bg-primary-600 px-3 py-2 text-xs font-semibold text-white hover:bg-primary-700"
        >
          {copied ? <Check className="h-3.5 w-3.5" strokeWidth={2.5} /> : <Copy className="h-3.5 w-3.5" strokeWidth={2.25} />}
          {copied ? "Copied" : "Copy"}
        </button>
      </div>
    </div>
  );
}
