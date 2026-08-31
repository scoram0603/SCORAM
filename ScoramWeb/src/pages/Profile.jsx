import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  LogOut, Mail, User as UserIcon, Users, MessageCircle, Loader2, Smartphone, Camera, X, Flame, Gift,
  ChevronRight, SlidersHorizontal, Phone, AtSign, Pencil, Check, CheckCircle2, XCircle, GraduationCap,
} from "lucide-react";
import { useAuth } from "../context/AuthContext";
import { getGamificationSummary } from "../api/gamification";
import { checkUsername } from "../api/auth";
import { enablePushNotifications, disablePushNotifications, getPushSubscriptionStatus, isPushSupported } from "../utils/push";
import { API_BASE_URL } from "../api/client";
import ImageCropModal from "../components/profile/ImageCropModal";

const USERNAME_PATTERN = /^[a-z0-9._]+$/;

function photoSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

function initialsFor(fullName) {
  if (!fullName) return "?";
  const parts = fullName.trim().split(/\s+/);
  return ((parts[0]?.[0] || "") + (parts[1]?.[0] || "")).toUpperCase() || fullName[0].toUpperCase();
}

export default function Profile() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  return (
    <div className="flex flex-col items-center px-6 py-10 sm:py-16">
      <ProfilePhoto />

      <h1 className="mt-4 text-xl font-extrabold text-ink-900">{user.fullName}</h1>

      <div className="mt-6 w-full max-w-sm">
        <ProfileInfoCard />
      </div>

      <NotificationSettings />

      <GamificationSnapshot />

      <button
        type="button"
        onClick={() => navigate("/my-exams")}
        className="mt-6 flex w-full max-w-sm items-center gap-3 rounded-xl2 border border-primary-100 bg-white p-4 text-left shadow-card transition-colors hover:border-primary-300"
      >
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary-50 text-primary-600">
          <GraduationCap className="h-4 w-4" strokeWidth={2} />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block text-sm font-bold text-ink-900">My Exams</span>
          <span className="block text-xs text-ink-400">Choose which exams PYP, Question Bank, and Tests default to</span>
        </span>
        <ChevronRight className="h-4 w-4 shrink-0 text-ink-300" strokeWidth={2.5} />
      </button>

      <button
        type="button"
        onClick={() => navigate("/settings")}
        className="mt-3 flex w-full max-w-sm items-center gap-3 rounded-xl2 border border-primary-100 bg-white p-4 text-left shadow-card transition-colors hover:border-primary-300"
      >
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary-50 text-primary-600">
          <SlidersHorizontal className="h-4 w-4" strokeWidth={2} />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block text-sm font-bold text-ink-900">Account & Security</span>
          <span className="block text-xs text-ink-400">Change your password, email, or phone number</span>
        </span>
        <ChevronRight className="h-4 w-4 shrink-0 text-ink-300" strokeWidth={2.5} />
      </button>

      <button
        type="button"
        onClick={logout}
        className="mt-6 flex items-center gap-1.5 rounded-xl2 border border-primary-100 bg-white px-4 py-2.5 text-sm font-semibold text-ink-600 shadow-card transition-colors hover:bg-red-50 hover:text-red-600"
      >
        <LogOut className="h-4 w-4" strokeWidth={2.25} />
        Log Out
      </button>
    </div>
  );
}

// Full Name + Username are directly editable here (no password gate -- see the endpoint's own
// comment in AuthController.cs). Email + Phone stay read-only display here with a "Change" link
// through to Settings, since those DO require the current-password confirmation built there.
function ProfileInfoCard() {
  const { user, updateBasicProfile } = useAuth();
  const navigate = useNavigate();
  const [editing, setEditing] = useState(false);
  const [fullName, setFullName] = useState(user.fullName);
  const [username, setUsername] = useState(user.username);
  const [usernameStatus, setUsernameStatus] = useState(null); // null | "checking" | "available" | "taken" | "invalid"
  const [usernameReason, setUsernameReason] = useState(null);
  const usernameCheckRef = useRef(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  useEffect(() => {
    if (!editing) return;
    const normalized = username.trim().toLowerCase();

    if (usernameCheckRef.current) clearTimeout(usernameCheckRef.current);

    // Unchanged from the account's current username -- no need to ask the server (and it would
    // wrongly say "taken", since it's taken by this same account).
    if (normalized === user.username) {
      setUsernameStatus(null);
      setUsernameReason(null);
      return;
    }
    if (!normalized) {
      setUsernameStatus("invalid");
      setUsernameReason("Username can't be empty.");
      return;
    }
    if (normalized.length < 3 || !USERNAME_PATTERN.test(normalized)) {
      setUsernameStatus("invalid");
      setUsernameReason(normalized.length < 3 ? "At least 3 characters" : "Only lowercase letters, numbers, dots, and underscores");
      return;
    }

    setUsernameStatus("checking");
    usernameCheckRef.current = setTimeout(async () => {
      try {
        const res = await checkUsername(normalized);
        setUsernameStatus(res.available ? "available" : "taken");
        setUsernameReason(res.reason || null);
      } catch {
        setUsernameStatus(null);
      }
    }, 400);

    return () => clearTimeout(usernameCheckRef.current);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [username, editing]);

  function handleEditStart() {
    setFullName(user.fullName);
    setUsername(user.username);
    setUsernameStatus(null);
    setError(null);
    setEditing(true);
  }

  function handleCancel() {
    setEditing(false);
    setError(null);
  }

  async function handleSave() {
    if (!fullName.trim()) {
      setError("Full name can't be empty.");
      return;
    }
    if (usernameStatus === "taken" || usernameStatus === "invalid") return;

    setSaving(true);
    setError(null);
    try {
      await updateBasicProfile({ fullName: fullName.trim(), username: username.trim().toLowerCase() });
      setEditing(false);
    } catch (err) {
      setError(err.message || "Couldn't save your changes. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
      <div className="flex items-center justify-between">
        <p className="text-sm font-bold text-ink-900">Profile Info</p>
        {!editing && (
          <button type="button" onClick={handleEditStart} className="flex items-center gap-1 text-xs font-semibold text-primary-600 hover:text-primary-700">
            <Pencil className="h-3.5 w-3.5" strokeWidth={2.25} />
            Edit
          </button>
        )}
      </div>

      {editing ? (
        <div className="mt-3 flex flex-col gap-3">
          <label className="block">
            <span className="mb-1 block text-xs font-semibold text-ink-600">Full name</span>
            <span className="flex items-center gap-2.5 rounded-xl2 border border-primary-100 bg-white px-3.5 py-3 focus-within:border-secondary-500">
              <UserIcon className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
              <input
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                maxLength={100}
                className="w-full bg-transparent text-sm text-ink-900 focus:outline-none"
              />
            </span>
          </label>

          <label className="block">
            <span className="mb-1 block text-xs font-semibold text-ink-600">Username</span>
            <span className="flex items-center gap-2.5 rounded-xl2 border border-primary-100 bg-white px-3.5 py-3 focus-within:border-secondary-500">
              <AtSign className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
              <input
                value={username}
                onChange={(e) => setUsername(e.target.value.toLowerCase())}
                maxLength={30}
                className="w-full bg-transparent text-sm text-ink-900 focus:outline-none"
              />
              <UsernameStatusIcon status={usernameStatus} />
            </span>
            {usernameReason && (usernameStatus === "taken" || usernameStatus === "invalid") && (
              <span className="mt-1 block text-xs text-red-600">{usernameReason}</span>
            )}
          </label>

          {error && <p className="text-xs text-red-600">{error}</p>}

          <div className="flex gap-2">
            <button
              type="button"
              onClick={handleCancel}
              disabled={saving}
              className="flex flex-1 items-center justify-center gap-1.5 rounded-xl2 border border-primary-100 px-4 py-2.5 text-sm font-semibold text-ink-600 disabled:opacity-60"
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={handleSave}
              disabled={saving || usernameStatus === "checking" || usernameStatus === "taken" || usernameStatus === "invalid"}
              className="flex flex-1 items-center justify-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
            >
              {saving ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} /> : <Check className="h-4 w-4" strokeWidth={2.5} />}
              Save
            </button>
          </div>
        </div>
      ) : (
        <div className="mt-1">
          <InfoRow icon={UserIcon} value={user.fullName} />
          <InfoRow icon={AtSign} value={`@${user.username}`} />
          <InfoRow icon={Mail} value={user.email} action={<ChangeLink onClick={() => navigate("/settings")} />} />
          <InfoRow icon={Phone} value={user.phoneNumber || "Not added"} action={<ChangeLink onClick={() => navigate("/settings")} />} />
        </div>
      )}
    </div>
  );
}

function InfoRow({ icon: Icon, value, action }) {
  return (
    <div className="flex items-center gap-3 border-t border-primary-50 py-2.5 first:border-t-0 first:pt-2">
      <Icon className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
      <span className="min-w-0 flex-1 truncate text-sm text-ink-600">{value}</span>
      {action}
    </div>
  );
}

function ChangeLink({ onClick }) {
  return (
    <button type="button" onClick={onClick} className="shrink-0 text-xs font-semibold text-primary-600 hover:text-primary-700">
      Change
    </button>
  );
}

function UsernameStatusIcon({ status }) {
  if (status === "checking") return <Loader2 className="h-4 w-4 shrink-0 animate-spin text-ink-400" strokeWidth={2.25} />;
  if (status === "available") return <CheckCircle2 className="h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.25} />;
  if (status === "taken" || status === "invalid") return <XCircle className="h-4 w-4 shrink-0 text-red-500" strokeWidth={2.25} />;
  return null;
}

// Live -- wired to GET /api/gamification/me. Small snapshot only (streak + XP + level); the full
// breakdown with badges lives on the "My Progress" page this links to, and referrals get their own
// page too since it's really a separate feature (invite code, share, reward tracking) rather than a
// gamification stat.
function GamificationSnapshot() {
  const navigate = useNavigate();
  const [summary, setSummary] = useState(null);
  const [status, setStatus] = useState("loading");

  useEffect(() => {
    getGamificationSummary()
      .then((res) => {
        setSummary(res);
        setStatus("success");
      })
      .catch(() => setStatus("error"));
  }, []);

  return (
    <div className="mt-6 w-full max-w-sm rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
      {status === "loading" && (
        <div className="flex justify-center py-3 text-ink-400">
          <Loader2 className="h-5 w-5 animate-spin" strokeWidth={2.25} />
        </div>
      )}

      {status === "error" && <p className="py-2 text-center text-xs text-ink-400">Couldn't load your progress.</p>}

      {status === "success" && (
        <button
          type="button"
          onClick={() => navigate("/progress")}
          className="flex w-full items-center gap-3 text-left"
        >
          <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-accent-50 text-accent-500">
            <Flame className="h-5 w-5" strokeWidth={2.25} />
          </span>
          <span className="min-w-0 flex-1">
            <span className="block text-sm font-bold text-ink-900">
              {summary.currentStreak}-day streak · {summary.totalXP} XP
            </span>
            <span className="block text-xs text-ink-400">{summary.currentLevel} level · View full progress</span>
          </span>
          <ChevronRight className="h-4 w-4 shrink-0 text-ink-300" strokeWidth={2.5} />
        </button>
      )}

      <div className="my-3 border-t border-primary-50" />

      <button
        type="button"
        onClick={() => navigate("/referrals")}
        className="flex w-full items-center gap-3 text-left"
      >
        <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-mint-50 text-mint-500">
          <Gift className="h-5 w-5" strokeWidth={2.25} />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block text-sm font-bold text-ink-900">Refer & Earn</span>
          <span className="block text-xs text-ink-400">Invite friends for bonus XP + attempts</span>
        </span>
        <ChevronRight className="h-4 w-4 shrink-0 text-ink-300" strokeWidth={2.5} />
      </button>
    </div>
  );
}

function ProfilePhoto() {
  const { user, updateProfilePhoto, removeProfilePhoto } = useAuth();
  const fileInputRef = useRef(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [pendingFile, setPendingFile] = useState(null); // file awaiting crop, or null

  function handleFileChange(e) {
    const file = e.target.files?.[0];
    e.target.value = ""; // allow re-selecting the same file next time
    if (!file) return;
    setError(null);
    setPendingFile(file); // opens the crop modal -- upload happens after Save there
  }

  async function handleCropped(croppedFile) {
    setPendingFile(null);
    setBusy(true);
    setError(null);
    try {
      await updateProfilePhoto(croppedFile);
    } catch (err) {
      setError(err.message || "Couldn't upload that photo.");
    } finally {
      setBusy(false);
    }
  }

  async function handleRemove() {
    setBusy(true);
    setError(null);
    try {
      await removeProfilePhoto();
    } catch (err) {
      setError(err.message || "Couldn't remove your photo.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col items-center">
      <div className="relative">
        {user.photoUrl ? (
          <img src={photoSrc(user.photoUrl)} alt={user.fullName} className="h-20 w-20 rounded-full object-cover" />
        ) : (
          <span className="flex h-20 w-20 items-center justify-center rounded-full bg-primary-600 text-2xl font-bold text-white">
            {initialsFor(user.fullName)}
          </span>
        )}

        <button
          type="button"
          onClick={() => fileInputRef.current?.click()}
          disabled={busy}
          aria-label="Change profile photo"
          className="absolute -bottom-1 -right-1 flex h-8 w-8 items-center justify-center rounded-full bg-primary-600 text-white shadow-card transition-colors hover:bg-primary-700 disabled:opacity-60"
        >
          {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.5} /> : <Camera className="h-3.5 w-3.5" strokeWidth={2.25} />}
        </button>

        <input ref={fileInputRef} type="file" accept="image/*" className="hidden" onChange={handleFileChange} />
      </div>

      {user.photoUrl && !busy && (
        <button
          type="button"
          onClick={handleRemove}
          className="mt-2 flex items-center gap-1 text-xs font-semibold text-ink-400 hover:text-red-500"
        >
          <X className="h-3 w-3" strokeWidth={2.5} />
          Remove photo
        </button>
      )}

      {error && <p className="mt-2 max-w-xs text-center text-xs text-red-600">{error}</p>}

      {pendingFile && (
        <ImageCropModal file={pendingFile} onCancel={() => setPendingFile(null)} onCropped={handleCropped} />
      )}
    </div>
  );
}

function NotificationSettings() {
  const { user, updateNotificationPreferences } = useAuth();
  // !== false rather than === true -- a user who logged in before this feature shipped has
  // these fields as undefined in their stored session until they next log in. Defaulting
  // undefined to "on" matches the backend's actual default and avoids silently muting them.
  const [groupOn, setGroupOn] = useState(user.notifyOnGroupMessages !== false);
  const [dmOn, setDmOn] = useState(user.notifyOnDirectMessages !== false);
  const [saving, setSaving] = useState(null); // "group" | "dm" | null

  const [pushStatus, setPushStatus] = useState("checking"); // checking | subscribed | not-subscribed | denied | unsupported
  const [pushLoading, setPushLoading] = useState(false);
  const [pushError, setPushError] = useState(null);

  useEffect(() => {
    getPushSubscriptionStatus().then(setPushStatus);
  }, []);

  async function handlePushToggle() {
    setPushError(null);
    setPushLoading(true);
    try {
      if (pushStatus === "subscribed") {
        await disablePushNotifications();
        setPushStatus("not-subscribed");
      } else {
        await enablePushNotifications();
        setPushStatus("subscribed");
      }
    } catch (err) {
      setPushError(err.message);
      setPushStatus(await getPushSubscriptionStatus());
    } finally {
      setPushLoading(false);
    }
  }

  async function toggle(which) {
    const nextGroupOn = which === "group" ? !groupOn : groupOn;
    const nextDmOn = which === "dm" ? !dmOn : dmOn;
    setSaving(which);
    try {
      await updateNotificationPreferences({ notifyOnGroupMessages: nextGroupOn, notifyOnDirectMessages: nextDmOn });
      setGroupOn(nextGroupOn);
      setDmOn(nextDmOn);
    } catch {
      // leave the switches as they were -- the request failing means nothing changed server-side
    } finally {
      setSaving(null);
    }
  }

  return (
    <div className="mt-6 w-full max-w-sm rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
      <p className="mb-3 text-sm font-bold text-ink-900">Notifications</p>

      <ToggleRow
        icon={Users}
        label="Group chat"
        description="Exam room messages and mentions"
        checked={groupOn}
        loading={saving === "group"}
        onChange={() => toggle("group")}
      />
      <div className="my-1 border-t border-primary-50" />
      <ToggleRow
        icon={MessageCircle}
        label="Personal messages"
        description="Direct messages from other students"
        checked={dmOn}
        loading={saving === "dm"}
        onChange={() => toggle("dm")}
      />

      {isPushSupported() && pushStatus !== "denied" && (
        <>
          <div className="my-1 border-t border-primary-50" />
          <ToggleRow
            icon={Smartphone}
            label="Push notifications"
            description="Get notified on this device, even in another tab"
            checked={pushStatus === "subscribed"}
            loading={pushLoading || pushStatus === "checking"}
            onChange={handlePushToggle}
          />
          {pushError && <p className="mt-1 text-xs text-red-600">{pushError}</p>}
        </>
      )}
      {pushStatus === "denied" && (
        <p className="mt-3 text-xs text-ink-400">
          Push notifications are blocked for this site in your browser settings. Enable them there to turn this on.
        </p>
      )}
    </div>
  );
}

function ToggleRow({ icon: Icon, label, description, checked, loading, onChange }) {
  return (
    <div className="flex items-center gap-3 py-2.5">
      <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary-50 text-primary-600">
        <Icon className="h-4 w-4" strokeWidth={2} />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block text-sm font-semibold text-ink-900">{label}</span>
        <span className="block text-xs text-ink-400">{description}</span>
      </span>
      {loading ? (
        <Loader2 className="h-4 w-4 shrink-0 animate-spin text-ink-400" strokeWidth={2.25} />
      ) : (
        <button
          type="button"
          role="switch"
          aria-checked={checked}
          aria-label={`${label} notifications`}
          onClick={onChange}
          className={`relative h-6 w-11 shrink-0 rounded-full transition-colors ${checked ? "bg-primary-600" : "bg-primary-100"}`}
        >
          <span
            className={`absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-transform ${
              checked ? "translate-x-[22px]" : "translate-x-0.5"
            }`}
          />
        </button>
      )}
    </div>
  );
}
