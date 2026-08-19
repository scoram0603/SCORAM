import { useEffect, useState } from "react";
import { Info, CalendarDays, Languages, ScrollText, Users, UserPlus, ChevronRight } from "lucide-react";
import { Link } from "react-router-dom";
import { getOnlineUsers } from "../../api/chat";
import { useChatConnection } from "../../context/ChatConnectionContext";
import { API_BASE_URL } from "../../api/client";

function photoSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

function initialsFor(name) {
  if (!name) return "?";
  const parts = name.trim().split(/\s+/);
  return ((parts[0]?.[0] || "") + (parts[1]?.[0] || "")).toUpperCase() || name[0].toUpperCase();
}

function formatDate(iso) {
  return new Date(iso).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });
}

const ONLINE_PREVIEW_COUNT = 8;

// PREMIUM UI -- "About This Community" + "Online Members", shown on xl+ screens alongside an open
// room (see GroupChat.jsx). Self-contained: fetches its own online-users snapshot and subscribes to
// the same "PresenceUpdated" event RoomChatView does, rather than having state lifted/prop-drilled
// from there -- keeps this panel addable/removable without touching the already-working chat view.
// Every field is real: Description/Language/Rules only render when an admin has actually set them
// (see ChatRoom.Language/Rules -- both null by default, no fabricated placeholder text), member and
// online counts come straight from the room object / presence service.
export default function RoomInfoPanel({ room }) {
  const { connection } = useChatConnection();
  const [onlineUsers, setOnlineUsers] = useState([]);
  const [showAllOnline, setShowAllOnline] = useState(false);

  useEffect(() => {
    getOnlineUsers(room.id).then(setOnlineUsers).catch(() => {});
    setShowAllOnline(false);
  }, [room.id]);

  useEffect(() => {
    if (!connection) return;
    const onPresenceUpdated = (data) => {
      if (data.roomId === room.id) setOnlineUsers(data.onlineUsers);
    };
    connection.on("PresenceUpdated", onPresenceUpdated);
    return () => connection.off("PresenceUpdated", onPresenceUpdated);
  }, [connection, room.id]);

  const visibleOnline = showAllOnline ? onlineUsers : onlineUsers.slice(0, ONLINE_PREVIEW_COUNT);

  return (
    <aside className="hidden xl:flex xl:h-[calc(100vh-32px)] xl:w-80 xl:shrink-0 xl:flex-col xl:gap-4 xl:overflow-y-auto xl:border-l xl:border-primary-100 xl:bg-white xl:p-4">
      <div>
        <h3 className="flex items-center gap-1.5 text-sm font-bold text-ink-900">
          <Info className="h-4 w-4 text-primary-600" strokeWidth={2.25} />
          About This Community
        </h3>

        {room.description && <p className="mt-2 text-sm leading-snug text-ink-600">{room.description}</p>}

        <div className="mt-3 flex flex-col gap-2.5">
          <div className="flex items-start gap-2 text-sm">
            <CalendarDays className="mt-0.5 h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
            <div>
              <p className="text-ink-900">{formatDate(room.createdAt)}</p>
              <p className="text-xs text-ink-400">Created on</p>
            </div>
          </div>

          {room.language && (
            <div className="flex items-start gap-2 text-sm">
              <Languages className="mt-0.5 h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
              <div>
                <p className="text-ink-900">{room.language}</p>
                <p className="text-xs text-ink-400">Language</p>
              </div>
            </div>
          )}

          <div className="flex items-start gap-2 text-sm">
            <Users className="mt-0.5 h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
            <div>
              <p className="text-ink-900">
                {room.memberCount} member{room.memberCount === 1 ? "" : "s"} · {onlineUsers.length} online
              </p>
              <p className="text-xs text-ink-400">Members</p>
            </div>
          </div>

          {room.rules && (
            <div className="flex items-start gap-2 text-sm">
              <ScrollText className="mt-0.5 h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
              <div>
                <p className="text-ink-900">{room.rules}</p>
                <p className="text-xs text-ink-400">Rules</p>
              </div>
            </div>
          )}
        </div>

        <Link
          to="/referrals"
          className="mt-4 flex w-full items-center justify-center gap-1.5 rounded-lg bg-primary-50 py-2 text-xs font-semibold text-primary-600 hover:bg-primary-100"
        >
          <UserPlus className="h-3.5 w-3.5" strokeWidth={2.25} />
          Invite Friends
        </Link>
      </div>

      <div className="border-t border-primary-100 pt-4">
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-bold text-ink-900">Online Members ({onlineUsers.length})</h3>
          {onlineUsers.length > ONLINE_PREVIEW_COUNT && (
            <button
              type="button"
              onClick={() => setShowAllOnline((s) => !s)}
              className="flex items-center text-xs font-semibold text-secondary-500 hover:text-secondary-600"
            >
              {showAllOnline ? "Show less" : "See all"}
              <ChevronRight className="h-3 w-3" strokeWidth={2.5} />
            </button>
          )}
        </div>

        {onlineUsers.length === 0 ? (
          <p className="mt-2 text-xs text-ink-400">No one else is here right now.</p>
        ) : (
          <div className="mt-2.5 flex flex-col gap-2">
            {visibleOnline.map((u) => (
              <div key={u.id} className="flex items-center gap-2.5">
                <span className="relative shrink-0">
                  {photoSrc(u.photoUrl) ? (
                    <img src={photoSrc(u.photoUrl)} alt="" className="h-8 w-8 rounded-full object-cover" />
                  ) : (
                    <span className="flex h-8 w-8 items-center justify-center rounded-full bg-primary-600 text-xs font-bold text-white">
                      {initialsFor(u.fullName)}
                    </span>
                  )}
                  <span className="absolute -bottom-0.5 -right-0.5 h-2.5 w-2.5 rounded-full border-2 border-white bg-mint-500" />
                </span>
                <span className="truncate text-sm text-ink-700">{u.fullName}</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </aside>
  );
}
