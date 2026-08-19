import { useEffect, useState } from "react";
import { X, Share2, Loader2, CheckCircle2 } from "lucide-react";
import { listChatRooms, shareQuestionToChat } from "../../api/chat";
import { API_BASE_URL } from "../../api/client";

function fileSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

// GROUP CHAT -- "share/reshare Scoram questions in a group" from a question's own page. Only rooms
// the student has actually joined show up here (posting requires membership -- see
// ChatController.ShareQuestion's IsActiveMember check), fetched with no search term so this reuses
// the same "featured + already-joined" list as the Rooms tab (see ChatController.ListRooms) and then
// filtered client-side to isMember, since a featured-but-not-joined room can't be posted into either.
export default function ShareToChatModal({ questionId, open, onClose }) {
  const [rooms, setRooms] = useState([]);
  const [status, setStatus] = useState("loading");
  const [sharingRoomId, setSharingRoomId] = useState(null);
  const [sharedRoomId, setSharedRoomId] = useState(null);

  useEffect(() => {
    if (!open) return;
    setStatus("loading");
    setSharedRoomId(null);
    listChatRooms()
      .then((data) => {
        setRooms(data.filter((r) => r.isMember));
        setStatus("ready");
      })
      .catch(() => setStatus("error"));
  }, [open]);

  if (!open) return null;

  async function handleShare(roomId) {
    setSharingRoomId(roomId);
    try {
      await shareQuestionToChat(roomId, questionId);
      setSharedRoomId(roomId);
    } catch {
      // room stays selectable -- the student can just try again
    } finally {
      setSharingRoomId(null);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/40 sm:items-center" onClick={onClose}>
      <div
        className="max-h-[70vh] w-full max-w-sm overflow-y-auto rounded-t-2xl bg-white p-4 sm:rounded-xl2"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between">
          <h3 className="flex items-center gap-1.5 text-sm font-bold text-ink-900">
            <Share2 className="h-4 w-4" strokeWidth={2.25} />
            Share to a group
          </h3>
          <button type="button" onClick={onClose} className="text-ink-400 hover:text-ink-600">
            <X className="h-5 w-5" strokeWidth={2.25} />
          </button>
        </div>

        <div className="mt-3">
          {status === "loading" && (
            <div className="flex justify-center py-8">
              <Loader2 className="h-5 w-5 animate-spin text-ink-400" strokeWidth={2.25} />
            </div>
          )}

          {status === "error" && <p className="py-6 text-center text-sm text-red-600">Couldn't load your groups right now.</p>}

          {status === "ready" && rooms.length === 0 && (
            <p className="py-6 text-center text-sm text-ink-400">
              Join a group from the Chat tab first, then you can share questions into it.
            </p>
          )}

          {status === "ready" && rooms.length > 0 && (
            <div className="flex flex-col gap-1.5">
              {rooms.map((room) => (
                <button
                  key={room.id}
                  type="button"
                  onClick={() => handleShare(room.id)}
                  disabled={sharingRoomId === room.id || sharedRoomId === room.id}
                  className="flex items-center gap-3 rounded-lg px-2.5 py-2 text-left hover:bg-primary-50 disabled:opacity-70"
                >
                  {room.examLogoUrl ? (
                    <img src={fileSrc(room.examLogoUrl)} alt="" className="h-9 w-9 rounded-full object-cover" />
                  ) : (
                    <span className="flex h-9 w-9 items-center justify-center rounded-full bg-secondary-50 text-xs font-bold text-secondary-500">
                      {room.examName.slice(0, 2).toUpperCase()}
                    </span>
                  )}
                  <span className="min-w-0 flex-1 truncate text-sm font-semibold text-ink-900">{room.examName}</span>
                  {sharedRoomId === room.id ? (
                    <CheckCircle2 className="h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.25} />
                  ) : sharingRoomId === room.id ? (
                    <Loader2 className="h-4 w-4 shrink-0 animate-spin text-ink-400" strokeWidth={2.25} />
                  ) : null}
                </button>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
