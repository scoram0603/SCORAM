import { useEffect, useState } from "react";
import {
  Lock, Unlock, Users, Megaphone, BarChart3, Flag, Ban, RefreshCw, Plus, X, Pencil, Trash2, Settings,
} from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  listAdminChatRooms, toggleChatLock, listRoomMembers, removeRoomMember, postNotice, createPoll,
  closePoll, listChatReports, resolveChatReport, listBannedWords, addBannedWord, removeBannedWord,
  syncChatRooms, createChatRoom, updateChatRoom, deleteChatRoom, updateChatRoomIcon,
} from "../api/chat";
import { API_BASE_URL } from "../../api/client";
import { PageHeader, Card, Button, FormField, TextInput, TextArea, Select, Alert, friendlyError } from "../components/AdminUI";

function logoSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

const SECTIONS = [
  { key: "rooms", label: "Rooms" },
  { key: "reports", label: "Reports" },
  { key: "banned-words", label: "Banned Words" },
];

export default function ChatModeration() {
  const { token, isSuperAdmin, hasPermission } = useAdminAuth();
  const [section, setSection] = useState("rooms");
  const [selectedRoom, setSelectedRoom] = useState(null);
  const [syncing, setSyncing] = useState(false);
  const [syncResult, setSyncResult] = useState(null);
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [roomsRefreshKey, setRoomsRefreshKey] = useState(0);

  async function handleSync() {
    setSyncing(true);
    setSyncResult(null);
    try {
      const res = await syncChatRooms(token);
      setSyncResult(res.message);
    } catch (err) {
      setSyncResult(friendlyError(err));
    } finally {
      setSyncing(false);
    }
  }

  return (
    <div>
      <PageHeader
        title="Group Chat"
        subtitle="Exam rooms are created automatically. Standalone groups (not tied to an exam) are created here."
        action={
          <div className="flex gap-2">
            {hasPermission("ManageChatRooms") && (
              <Button variant="secondary" onClick={() => setShowCreateForm((s) => !s)}>
                <Plus className="h-4 w-4" strokeWidth={2.5} />
                Create Group
              </Button>
            )}
            {isSuperAdmin && (
              <Button variant="secondary" isLoading={syncing} onClick={handleSync}>
                <RefreshCw className="h-4 w-4" strokeWidth={2.25} />
                Sync rooms for all exams
              </Button>
            )}
          </div>
        }
      />

      <div className="p-6">
        {syncResult && <div className="mb-4"><Alert type="success">{syncResult}</Alert></div>}

        {showCreateForm && (
          <CreateRoomForm
            token={token}
            onDone={() => {
              setShowCreateForm(false);
              setRoomsRefreshKey((k) => k + 1);
            }}
            onCancel={() => setShowCreateForm(false)}
          />
        )}

        {selectedRoom ? (
          <RoomDetail room={selectedRoom} onBack={() => setSelectedRoom(null)} token={token} hasPermission={hasPermission} />
        ) : (
          <>
            <div className="mb-4 flex gap-2">
              {SECTIONS.map((s) => (
                <button
                  key={s.key}
                  type="button"
                  onClick={() => setSection(s.key)}
                  className={`rounded-xl2 px-4 py-2 text-sm font-semibold transition-colors ${section === s.key ? "bg-primary-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"}`}
                >
                  {s.label}
                </button>
              ))}
            </div>

            {section === "rooms" && <RoomsSection token={token} onOpenRoom={setSelectedRoom} refreshKey={roomsRefreshKey} />}
            {section === "reports" && <ReportsSection token={token} hasPermission={hasPermission} />}
            {section === "banned-words" && <BannedWordsSection token={token} hasPermission={hasPermission} />}
          </>
        )}
      </div>
    </div>
  );
}

function CreateRoomForm({ token, onDone, onCancel }) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [isFeatured, setIsFeatured] = useState(true);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    setSending(true);
    setError(null);
    try {
      await createChatRoom(token, { name: name.trim(), description: description.trim() || null, isFeatured });
      onDone();
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setSending(false);
    }
  }

  return (
    <Card className="mx-auto mb-4 max-w-lg">
      <form onSubmit={handleSubmit} className="flex flex-col gap-3">
        <FormField label="Group name">
          <TextInput required value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Daily Doubt Room" autoFocus />
        </FormField>
        <FormField label="Description (optional)">
          <TextArea rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
        </FormField>
        <label className="flex items-center gap-2 text-sm text-ink-600">
          <input type="checkbox" checked={isFeatured} onChange={(e) => setIsFeatured(e.target.checked)} className="h-4 w-4 accent-primary-600" />
          Show in students' default room list (unchecked = only findable by search)
        </label>
        {error && <Alert>{error}</Alert>}
        <div className="flex gap-2">
          <Button type="submit" isLoading={sending}>Create</Button>
          <Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button>
        </div>
      </form>
    </Card>
  );
}

function RoomsSection({ token, onOpenRoom, refreshKey }) {
  const [rooms, setRooms] = useState([]);
  const [isLoading, setIsLoading] = useState(true);
  const [editingRoom, setEditingRoom] = useState(null);
  const { hasPermission } = useAdminAuth();

  useEffect(() => {
    refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refreshKey]);

  function refresh() {
    setIsLoading(true);
    listAdminChatRooms(token).then(setRooms).finally(() => setIsLoading(false));
  }

  async function handleToggleLock(room) {
    setRooms((prev) => prev.map((r) => (r.id === room.id ? { ...r, isChatDisabled: !r.isChatDisabled } : r)));
    try {
      await toggleChatLock(token, room.id, !room.isChatDisabled);
    } catch {
      refresh();
    }
  }

  async function handleDelete(room) {
    if (!window.confirm(`Delete "${room.examName}"? This also deletes its message history.`)) return;
    try {
      await deleteChatRoom(token, room.id);
      setRooms((prev) => prev.filter((r) => r.id !== room.id));
    } catch {
      refresh();
    }
  }

  if (isLoading) return <p className="text-sm text-ink-400">Loading rooms…</p>;

  return (
    <div className="flex flex-col gap-3">
      {editingRoom && (
        <EditRoomForm
          token={token}
          room={editingRoom}
          onDone={(updated) => {
            setRooms((prev) => prev.map((r) => (r.id === updated.id ? updated : r)));
            setEditingRoom(null);
          }}
          onCancel={() => setEditingRoom(null)}
        />
      )}

      {rooms.map((room) => (
        <Card key={room.id} className="flex flex-wrap items-center justify-between gap-4">
          <button type="button" onClick={() => onOpenRoom(room)} className="flex min-w-0 flex-1 items-center gap-3 text-left">
            {logoSrc(room.iconUrl) ? (
              <img src={logoSrc(room.iconUrl)} alt="" className="h-10 w-10 rounded-full object-cover" />
            ) : (
              <span className="flex h-10 w-10 items-center justify-center rounded-full bg-primary-50 text-xs font-bold text-primary-600">
                {room.examName.slice(0, 2).toUpperCase()}
              </span>
            )}
            <span>
              <span className="flex items-center gap-1.5">
                <span className="block text-sm font-bold text-ink-900">{room.examName}</span>
                {!room.isFeatured && (
                  <span className="rounded-full bg-primary-50 px-2 py-0.5 text-[10px] font-semibold text-primary-600">Search only</span>
                )}
              </span>
              <span className="flex items-center gap-1 text-xs text-ink-400">
                <Users className="h-3 w-3" strokeWidth={2.25} />
                {room.memberCount} members
              </span>
            </span>
          </button>

          <div className="flex gap-2">
            {hasPermission("ManageChatRooms") && !room.examId && (
              <>
                <Button variant="ghost" onClick={() => setEditingRoom(room)}>
                  <Pencil className="h-4 w-4" strokeWidth={2.25} />
                </Button>
                <Button variant="danger" onClick={() => handleDelete(room)}>
                  <Trash2 className="h-4 w-4" strokeWidth={2.25} />
                </Button>
              </>
            )}
            {hasPermission("ToggleChatLock") && (
              <Button variant={room.isChatDisabled ? "secondary" : "danger"} onClick={() => handleToggleLock(room)}>
                {room.isChatDisabled ? <Unlock className="h-4 w-4" strokeWidth={2.25} /> : <Lock className="h-4 w-4" strokeWidth={2.25} />}
                {room.isChatDisabled ? "Unlock" : "Lock"}
              </Button>
            )}
          </div>
        </Card>
      ))}
    </div>
  );
}

function EditRoomForm({ token, room, onDone, onCancel }) {
  const [name, setName] = useState(room.examName);
  const [description, setDescription] = useState(room.description || "");
  const [isFeatured, setIsFeatured] = useState(room.isFeatured);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    setSending(true);
    setError(null);
    try {
      const updated = await updateChatRoom(token, room.id, { name: name.trim(), description: description.trim() || null, isFeatured });
      onDone({ ...room, examName: updated.examName, description: updated.description, isFeatured: updated.isFeatured });
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setSending(false);
    }
  }

  return (
    <Card className="mx-auto max-w-lg">
      <form onSubmit={handleSubmit} className="flex flex-col gap-3">
        <FormField label="Group name">
          <TextInput required value={name} onChange={(e) => setName(e.target.value)} autoFocus />
        </FormField>
        <FormField label="Description">
          <TextArea rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
        </FormField>
        <label className="flex items-center gap-2 text-sm text-ink-600">
          <input type="checkbox" checked={isFeatured} onChange={(e) => setIsFeatured(e.target.checked)} className="h-4 w-4 accent-primary-600" />
          Show in students' default room list
        </label>
        {error && <Alert>{error}</Alert>}
        <div className="flex gap-2">
          <Button type="submit" isLoading={sending}>Save</Button>
          <Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button>
        </div>
      </form>
    </Card>
  );
}

// ADMIN GROUP SETTINGS -- room picture (standalone rooms only) + "who can send messages". Reuses
// the same PATCH used by EditRoomForm for permission, plus a dedicated icon upload endpoint since
// that's a file, not JSON.
function RoomSettingsForm({ token, room, onDone, onCancel }) {
  const [postPermission, setPostPermission] = useState(room.postPermission);
  const [language, setLanguage] = useState(room.language || "");
  const [rules, setRules] = useState(room.rules || "");
  const [iconFile, setIconFile] = useState(null);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    setSending(true);
    setError(null);
    try {
      let updated = room;
      if (iconFile && !room.examId) updated = await updateChatRoomIcon(token, room.id, iconFile);
      if (postPermission !== room.postPermission || language !== (room.language || "") || rules !== (room.rules || "")) {
        updated = await updateChatRoom(token, room.id, { postPermission, language: language || null, rules: rules || null });
      }
      onDone(updated);
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setSending(false);
    }
  }

  return (
    <Card className="mx-auto mt-3 max-w-lg">
      <form onSubmit={handleSubmit} className="flex flex-col gap-3">
        <h3 className="text-sm font-bold text-ink-900">Group Settings</h3>

        {room.examId ? (
          <p className="text-xs text-ink-400">
            This room is linked to an exam -- its picture and name come from the exam (Manage Exams). Only posting
            permission can be changed here.
          </p>
        ) : (
          <FormField label="Group picture" hint={room.iconUrl ? "Leave empty to keep the current picture." : undefined}>
            <input type="file" accept="image/*" onChange={(e) => setIconFile(e.target.files?.[0] || null)} className="text-sm" />
          </FormField>
        )}

        <FormField label="Who can send messages">
          <Select value={postPermission} onChange={(e) => setPostPermission(e.target.value)}>
            <option value="AllMembers">All members</option>
            <option value="AdminOnly">Admins only (announcement room)</option>
          </Select>
        </FormField>

        <FormField label="Language (optional)" hint="Shown on the room's About panel">
          <TextInput value={language} onChange={(e) => setLanguage(e.target.value)} placeholder="e.g. Hindi / English" />
        </FormField>
        <FormField label="Rules (optional)" hint="Shown on the room's About panel">
          <TextArea rows={3} value={rules} onChange={(e) => setRules(e.target.value)} placeholder="e.g. Be respectful & helpful. No spam." />
        </FormField>

        {error && <Alert>{error}</Alert>}
        <div className="flex gap-2">
          <Button type="submit" isLoading={sending}>Save</Button>
          <Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button>
        </div>
      </form>
    </Card>
  );
}

function RoomDetail({ room, onBack, token, hasPermission }) {
  const [members, setMembers] = useState([]);
  const [loadingMembers, setLoadingMembers] = useState(true);
  const [showNoticeForm, setShowNoticeForm] = useState(false);
  const [showPollForm, setShowPollForm] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  const [currentRoom, setCurrentRoom] = useState(room);

  useEffect(() => {
    listRoomMembers(token, room.id).then(setMembers).finally(() => setLoadingMembers(false));
  }, [room.id]);

  async function handleRemove(userId) {
    if (!window.confirm("Remove (ban) this student from the group? They won't be able to rejoin themselves.")) return;
    await removeRoomMember(token, room.id, userId);
    setMembers((prev) => prev.map((m) => (m.userId === userId ? { ...m, isBanned: true } : m)));
  }

  return (
    <div>
      <button type="button" onClick={onBack} className="mb-4 text-sm font-semibold text-secondary-500">&larr; Back to rooms</button>

      <h2 className="text-lg font-bold text-ink-900">{currentRoom.examName}</h2>

      <div className="mt-3 flex flex-wrap gap-2">
        {hasPermission("PostNotices") && (
          <Button variant="secondary" onClick={() => setShowNoticeForm((s) => !s)}>
            <Megaphone className="h-4 w-4" strokeWidth={2.25} />
            Post Notice
          </Button>
        )}
        {hasPermission("CreatePolls") && (
          <Button variant="secondary" onClick={() => setShowPollForm((s) => !s)}>
            <BarChart3 className="h-4 w-4" strokeWidth={2.25} />
            Create Poll
          </Button>
        )}
        {hasPermission("ManageChatRooms") && (
          <Button variant="secondary" onClick={() => setShowSettings((s) => !s)}>
            <Settings className="h-4 w-4" strokeWidth={2.25} />
            Group Settings
          </Button>
        )}
      </div>

      {showNoticeForm && <NoticeForm token={token} roomId={room.id} onDone={() => setShowNoticeForm(false)} />}
      {showPollForm && <PollForm token={token} roomId={room.id} onDone={() => setShowPollForm(false)} />}
      {showSettings && (
        <RoomSettingsForm
          token={token}
          room={currentRoom}
          onDone={(updated) => {
            setCurrentRoom(updated);
            setShowSettings(false);
          }}
          onCancel={() => setShowSettings(false)}
        />
      )}

      <h3 className="mt-6 text-sm font-bold text-ink-900">Members</h3>
      {loadingMembers ? (
        <p className="mt-2 text-sm text-ink-400">Loading…</p>
      ) : (
        <div className="mt-2 flex flex-col gap-2">
          {members.map((m) => (
            <Card key={m.userId} className="flex items-center justify-between gap-3">
              <span>
                <span className="block text-sm font-semibold text-ink-900">@{m.username}</span>
                <span className="block text-xs text-ink-400">{m.fullName}</span>
              </span>
              {m.isBanned ? (
                <span className="rounded-full bg-red-50 px-2.5 py-1 text-xs font-semibold text-red-600">Banned</span>
              ) : (
                hasPermission("RemoveGroupMembers") && (
                  <Button variant="danger" onClick={() => handleRemove(m.userId)}>
                    <Ban className="h-4 w-4" strokeWidth={2.25} />
                    Remove
                  </Button>
                )
              )}
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}

function NoticeForm({ token, roomId, onDone }) {
  const [text, setText] = useState("");
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    setSending(true);
    setError(null);
    try {
      await postNotice(token, roomId, text);
      onDone();
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setSending(false);
    }
  }

  return (
    <Card className="mx-auto mt-3 max-w-lg">
      <form onSubmit={handleSubmit} className="flex flex-col gap-3">
        <FormField label="Notice text">
          <TextArea required rows={3} value={text} onChange={(e) => setText(e.target.value)} autoFocus />
        </FormField>
        {error && <Alert>{error}</Alert>}
        <div className="flex gap-2">
          <Button type="submit" isLoading={sending}>Post</Button>
          <Button type="button" variant="ghost" onClick={onDone}>Cancel</Button>
        </div>
      </form>
    </Card>
  );
}

function PollForm({ token, roomId, onDone }) {
  const [question, setQuestion] = useState("");
  const [options, setOptions] = useState(["", ""]);
  const [allowMultiple, setAllowMultiple] = useState(false);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(null);

  function updateOption(i, value) {
    setOptions((prev) => prev.map((o, idx) => (idx === i ? value : o)));
  }

  async function handleSubmit(e) {
    e.preventDefault();
    setSending(true);
    setError(null);
    try {
      await createPoll(token, roomId, { question, options: options.filter((o) => o.trim()), allowMultipleChoices: allowMultiple });
      onDone();
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setSending(false);
    }
  }

  return (
    <Card className="mx-auto mt-3 max-w-lg">
      <form onSubmit={handleSubmit} className="flex flex-col gap-3">
        <FormField label="Question">
          <TextInput required value={question} onChange={(e) => setQuestion(e.target.value)} autoFocus />
        </FormField>
        <FormField label="Options">
          <div className="flex flex-col gap-2">
            {options.map((opt, i) => (
              <div key={i} className="flex gap-2">
                <TextInput required={i < 2} value={opt} onChange={(e) => updateOption(i, e.target.value)} placeholder={`Option ${i + 1}`} />
                {options.length > 2 && (
                  <button type="button" onClick={() => setOptions((prev) => prev.filter((_, idx) => idx !== i))} className="text-ink-400 hover:text-red-500">
                    <X className="h-4 w-4" strokeWidth={2.25} />
                  </button>
                )}
              </div>
            ))}
          </div>
          <button type="button" onClick={() => setOptions((prev) => [...prev, ""])} className="mt-2 flex items-center gap-1 text-xs font-semibold text-secondary-500">
            <Plus className="h-3.5 w-3.5" strokeWidth={2.5} />
            Add option
          </button>
        </FormField>
        <label className="flex items-center gap-2 text-sm text-ink-600">
          <input type="checkbox" checked={allowMultiple} onChange={(e) => setAllowMultiple(e.target.checked)} className="h-4 w-4 accent-primary-600" />
          Allow multiple choices
        </label>
        {error && <Alert>{error}</Alert>}
        <div className="flex gap-2">
          <Button type="submit" isLoading={sending}>Create Poll</Button>
          <Button type="button" variant="ghost" onClick={onDone}>Cancel</Button>
        </div>
      </form>
    </Card>
  );
}

function ReportsSection({ token, hasPermission }) {
  const [reports, setReports] = useState([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    refresh();
  }, []);

  function refresh() {
    setIsLoading(true);
    setError(null);
    listChatReports(token)
      .then(setReports)
      .catch((err) => setError(friendlyError(err)))
      .finally(() => setIsLoading(false));
  }

  async function handleResolve(reportId, status, deleteMessage) {
    await resolveChatReport(token, reportId, { status, deleteMessage });
    setReports((prev) => prev.filter((r) => r.id !== reportId));
  }

  if (!hasPermission("HandleChatReports")) {
    return <p className="text-sm text-ink-400">You don't have permission to handle chat reports.</p>;
  }
  if (isLoading) return <p className="text-sm text-ink-400">Loading…</p>;
  if (error) return <Alert>{error}</Alert>;
  if (reports.length === 0) return <p className="text-sm text-ink-400">No pending reports.</p>;

  return (
    <div className="flex flex-col gap-3">
      {reports.map((r) => (
        <Card key={r.id}>
          <p className="text-xs font-semibold text-ink-400">{r.roomName} · reported by @{r.reportedByUsername}</p>
          <p className="mt-1 text-sm text-ink-900">"{r.messageTextPreview || "(attachment)"}"</p>
          <p className="mt-1 text-xs text-ink-600"><span className="font-semibold">Reason:</span> {r.reason}</p>
          <div className="mt-3 flex gap-2">
            <Button variant="danger" onClick={() => handleResolve(r.id, "ActionTaken", true)}>
              <Flag className="h-4 w-4" strokeWidth={2.25} />
              Delete message
            </Button>
            <Button variant="ghost" onClick={() => handleResolve(r.id, "Dismissed", false)}>Dismiss</Button>
          </div>
        </Card>
      ))}
    </div>
  );
}

function BannedWordsSection({ token, hasPermission }) {
  const [words, setWords] = useState([]);
  const [newWord, setNewWord] = useState("");
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    refresh();
  }, []);

  function refresh() {
    setIsLoading(true);
    listBannedWords(token).then(setWords).catch((err) => setError(friendlyError(err))).finally(() => setIsLoading(false));
  }

  async function handleAdd(e) {
    e.preventDefault();
    if (!newWord.trim()) return;
    setError(null);
    try {
      await addBannedWord(token, newWord.trim());
      setNewWord("");
      refresh();
    } catch (err) {
      setError(friendlyError(err));
    }
  }

  async function handleRemove(id) {
    await removeBannedWord(token, id);
    setWords((prev) => prev.filter((w) => w.id !== id));
  }

  if (!hasPermission("ManageBannedWords")) {
    return <p className="text-sm text-ink-400">You don't have permission to manage banned words.</p>;
  }

  return (
    <div className="mx-auto max-w-lg">
      <form onSubmit={handleAdd} className="flex gap-2">
        <TextInput value={newWord} onChange={(e) => setNewWord(e.target.value)} placeholder="Word or phrase to restrict" />
        <Button type="submit"><Plus className="h-4 w-4" strokeWidth={2.5} />Add</Button>
      </form>
      {error && <div className="mt-2"><Alert>{error}</Alert></div>}

      {isLoading ? (
        <p className="mt-3 text-sm text-ink-400">Loading…</p>
      ) : (
        <div className="mt-3 flex flex-wrap gap-2">
          {words.map((w) => (
            <span key={w.id} className="flex items-center gap-1.5 rounded-full bg-red-50 px-3 py-1.5 text-sm font-medium text-red-600">
              {w.word}
              <button type="button" onClick={() => handleRemove(w.id)}><X className="h-3.5 w-3.5" strokeWidth={2.5} /></button>
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
