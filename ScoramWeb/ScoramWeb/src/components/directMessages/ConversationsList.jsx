import { useEffect, useRef, useState } from "react";
import { Search, Loader2, MessageCircleOff, X } from "lucide-react";
import { listConversations, searchUsers, startConversation } from "../../api/directMessages";
import { API_BASE_URL } from "../../api/client";
import { timeAgo } from "../../utils/format";

function photoSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

function initialsFor(fullName) {
  if (!fullName) return "?";
  const parts = fullName.trim().split(/\s+/);
  return ((parts[0]?.[0] || "") + (parts[1]?.[0] || "")).toUpperCase() || fullName[0].toUpperCase();
}

export default function ConversationsList({ onOpenConversation, refreshSignal }) {
  const [conversations, setConversations] = useState([]);
  const [status, setStatus] = useState("loading");
  const [query, setQuery] = useState("");
  const [searchResults, setSearchResults] = useState([]);
  const [searching, setSearching] = useState(false);
  const [startingUserId, setStartingUserId] = useState(null);
  const debounceRef = useRef(null);

  useEffect(() => {
    refresh();
    // refreshSignal bumps whenever a new message arrives elsewhere in the app, so the list
    // (previews, unread counts, ordering) stays live without the user having to navigate away and back.
  }, [refreshSignal]);

  function refresh() {
    setStatus("loading");
    listConversations()
      .then((data) => {
        setConversations(data);
        setStatus("ready");
      })
      .catch(() => setStatus("error"));
  }

  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    if (query.trim().length < 2) {
      setSearchResults([]);
      setSearching(false);
      return;
    }
    setSearching(true);
    const controller = new AbortController();
    debounceRef.current = setTimeout(() => {
      searchUsers(query.trim(), { signal: controller.signal })
        .then((data) => {
          setSearchResults(data);
          setSearching(false);
        })
        .catch((err) => {
          if (err.name !== "AbortError") setSearching(false);
        });
    }, 300);
    return () => {
      clearTimeout(debounceRef.current);
      controller.abort();
    };
  }, [query]);

  async function handleStartConversation(user) {
    setStartingUserId(user.id);
    try {
      const conversation = await startConversation(user.id);
      setQuery("");
      setSearchResults([]);
      onOpenConversation(conversation);
    } finally {
      setStartingUserId(null);
    }
  }

  const isSearchMode = query.trim().length >= 2;

  return (
    <div className="px-4 pb-8 pt-5 sm:px-6 lg:px-8">
      <label className="relative block">
        <span className="sr-only">Search by username to start a new message</span>
        <Search className="pointer-events-none absolute left-3.5 top-1/2 h-[18px] w-[18px] -translate-y-1/2 text-ink-400" strokeWidth={2} />
        <input
          type="search"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Search by username to message someone new..."
          className="h-12 w-full rounded-xl2 border border-primary-100 bg-white pl-10 pr-9 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500"
        />
        {query && (
          <button
            type="button"
            onClick={() => setQuery("")}
            aria-label="Clear search"
            className="absolute right-3 top-1/2 -translate-y-1/2 text-ink-400 hover:text-ink-600"
          >
            <X className="h-4 w-4" strokeWidth={2.25} />
          </button>
        )}
      </label>

      {isSearchMode ? (
        <div className="mt-3 flex flex-col gap-1.5">
          {searching && (
            <div className="flex justify-center py-8 text-ink-400">
              <Loader2 className="h-5 w-5 animate-spin" strokeWidth={2.25} />
            </div>
          )}
          {!searching && searchResults.length === 0 && (
            <p className="py-8 text-center text-sm text-ink-400">No students found with that username.</p>
          )}
          {!searching &&
            searchResults.map((u) => (
              <button
                key={u.id}
                type="button"
                disabled={startingUserId === u.id}
                onClick={() => handleStartConversation(u)}
                className="flex items-center gap-3 rounded-xl2 border border-primary-100 bg-white p-3 text-left shadow-card transition-colors hover:bg-primary-50/40 disabled:opacity-60"
              >
                <Avatar photoUrl={u.photoUrl} fullName={u.fullName} />
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-bold text-ink-900">{u.fullName}</span>
                  <span className="block truncate text-xs text-ink-400">@{u.username}</span>
                </span>
                {startingUserId === u.id && <Loader2 className="h-4 w-4 shrink-0 animate-spin text-ink-400" strokeWidth={2.25} />}
              </button>
            ))}
        </div>
      ) : (
        <div className="mt-5">
          {status === "loading" && (
            <div className="flex justify-center py-16">
              <Loader2 className="h-6 w-6 animate-spin text-ink-400" strokeWidth={2.25} />
            </div>
          )}
          {status === "error" && <p className="py-16 text-center text-sm text-red-600">Couldn't load your messages right now.</p>}
          {status === "ready" && conversations.length === 0 && (
            <div className="flex flex-col items-center gap-2 py-16 text-center">
              <MessageCircleOff className="h-8 w-8 text-ink-300" strokeWidth={1.5} />
              <p className="text-sm text-ink-400">No conversations yet. Search a username above to start one.</p>
            </div>
          )}
          {status === "ready" && conversations.length > 0 && (
            <div className="flex flex-col gap-1.5">
              {conversations.map((c) => (
                <button
                  key={c.id}
                  type="button"
                  onClick={() => onOpenConversation(c)}
                  className="flex items-center gap-3 rounded-xl2 border border-primary-100 bg-white p-3 text-left shadow-card transition-colors hover:bg-primary-50/40"
                >
                  <Avatar photoUrl={c.otherPhotoUrl} fullName={c.otherFullName} />
                  <span className="min-w-0 flex-1">
                    <span className="flex items-center justify-between gap-2">
                      <span className="truncate text-sm font-bold text-ink-900">{c.otherFullName}</span>
                      <span className="shrink-0 text-[11px] text-ink-400">{timeAgo(c.lastMessageAt)}</span>
                    </span>
                    <span className={`block truncate text-xs ${c.unreadCount > 0 ? "font-semibold text-ink-900" : "text-ink-400"}`}>
                      {c.lastMessagePreview || "Say hello 👋"}
                    </span>
                  </span>
                  {c.unreadCount > 0 && (
                    <span className="flex h-5 min-w-5 shrink-0 items-center justify-center rounded-full bg-secondary-500 px-1.5 text-[11px] font-bold text-white">
                      {c.unreadCount}
                    </span>
                  )}
                </button>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export function Avatar({ photoUrl, fullName, size = "h-11 w-11" }) {
  return photoSrc(photoUrl) ? (
    <img src={photoSrc(photoUrl)} alt="" className={`${size} shrink-0 rounded-full object-cover`} />
  ) : (
    <span className={`flex ${size} shrink-0 items-center justify-center rounded-full bg-secondary-50 text-sm font-bold text-secondary-500`}>
      {initialsFor(fullName)}
    </span>
  );
}
