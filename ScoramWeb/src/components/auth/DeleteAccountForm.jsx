import { useState } from "react";
import { Link } from "react-router-dom";
import { AlertTriangle, AlertCircle, CheckCircle2, Eye, EyeOff, Loader2, Lock, Phone } from "lucide-react";
import { useAuth } from "../../context/AuthContext";
import OtpEntryBox from "./OtpEntryBox";
import { LEGAL } from "../../config/legal";

// Permanent account deletion. Used by Settings and by the public /delete-account page (when the
// visitor is signed in). Re-authentication is REQUIRED: the account's current password, or -- for
// people who sign in by OTP and don't remember a password -- a fresh OTP on the account's OWN
// phone number (the backend re-verifies the OTP token and checks it matches the stored number).
// See AuthController.DeleteAccount / AccountDeletionService for exactly what gets erased.
export default function DeleteAccountForm() {
  const { user, deleteAccount } = useAuth();
  const [method, setMethod] = useState("password"); // "password" | "otp"
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [otpAccessToken, setOtpAccessToken] = useState(null);
  const [confirmation, setConfirmation] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const confirmed = confirmation.trim() === "DELETE";
  const credentialReady = method === "password" ? password.length > 0 : Boolean(otpAccessToken);
  const canSubmit = confirmed && credentialReady && !busy;

  async function handleSubmit(e) {
    e.preventDefault();
    if (!canSubmit) return;
    setError(null);
    setBusy(true);
    try {
      await deleteAccount({
        currentPassword: method === "password" ? password : undefined,
        otpAccessToken: method === "otp" ? otpAccessToken : undefined,
        confirmation: confirmation.trim(),
      });
      // Full navigation (not router navigate): the session is already cleared, and this keeps a
      // ProtectedRoute redirect-to-login from racing us when this form is rendered inside Settings.
      window.location.replace(`${LEGAL.paths.deleteAccount}?deleted=1`);
    } catch (err) {
      setError(err.message || "We couldn't delete your account. Please try again.");
      setBusy(false);
    }
  }

  function switchMethod(next) {
    setMethod(next);
    setError(null);
    setOtpAccessToken(null);
    setPassword("");
  }

  return (
    <form onSubmit={handleSubmit} className="rounded-xl2 border border-red-200 bg-white p-5" noValidate>
      <div className="flex items-start gap-3">
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-red-50 text-red-600">
          <AlertTriangle className="h-4 w-4" strokeWidth={2.25} />
        </span>
        <div className="min-w-0">
          <p className="text-sm font-bold text-ink-900">Permanently delete your account</p>
          <p className="mt-1 text-sm leading-relaxed text-ink-600">
            This cannot be undone. Your attempts, progress, XP, bookmarks, My Exams, feedback, notifications, devices,
            Study Partner data and the content of your direct messages are erased. Your discussion comments, solutions
            and group chat text stay but are shown as &ldquo;Deleted User&rdquo;.{" "}
            <Link to={`${LEGAL.paths.privacy}#account-deletion`} className="font-semibold text-secondary-500 hover:underline">
              Read exactly what happens
            </Link>
            .
          </p>
        </div>
      </div>

      <fieldset className="mt-5">
        <legend className="mb-2 text-xs font-semibold text-ink-600">Confirm it&apos;s you</legend>
        <div className="inline-flex rounded-xl2 border border-primary-100 p-0.5 text-xs font-semibold" role="radiogroup" aria-label="Verification method">
          {[
            { id: "password", label: "Password" },
            { id: "otp", label: "Phone OTP" },
          ].map((opt) => (
            <button
              key={opt.id}
              type="button"
              role="radio"
              aria-checked={method === opt.id}
              onClick={() => switchMethod(opt.id)}
              className={`rounded-[14px] px-3.5 py-1.5 transition-colors ${
                method === opt.id ? "bg-primary-600 text-white" : "text-ink-600 hover:text-primary-600"
              }`}
            >
              {opt.label}
            </button>
          ))}
        </div>

        {method === "password" ? (
          <label className="mt-3 block">
            <span className="mb-1 block text-xs font-semibold text-ink-600">Current password</span>
            <span className="flex items-center gap-2.5 rounded-xl2 border border-primary-100 bg-white px-3.5 py-3 focus-within:border-secondary-500">
              <Lock className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
              <input
                type={showPassword ? "text" : "password"}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
                placeholder="Your current password"
                className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
              />
              <button
                type="button"
                onClick={() => setShowPassword((s) => !s)}
                aria-label={showPassword ? "Hide password" : "Show password"}
                className="text-ink-400 hover:text-ink-600"
              >
                {showPassword ? <EyeOff className="h-4 w-4" strokeWidth={2} /> : <Eye className="h-4 w-4" strokeWidth={2} />}
              </button>
            </span>
          </label>
        ) : (
          <div className="mt-3 flex flex-col gap-3">
            <p className="flex items-center gap-2 text-sm text-ink-600">
              <Phone className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
              We&apos;ll text a code to your registered number
              {user?.phoneNumber ? <strong className="font-semibold text-ink-900">{user.phoneNumber}</strong> : null}
              {otpAccessToken && <CheckCircle2 className="h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.25} />}
            </p>
            {!otpAccessToken && user?.phoneNumber && (
              <OtpEntryBox key={user.phoneNumber} phoneNumber={user.phoneNumber} onVerified={setOtpAccessToken} />
            )}
          </div>
        )}
      </fieldset>

      <label className="mt-5 block">
        <span className="mb-1 block text-xs font-semibold text-ink-600">
          Type <span className="font-bold text-ink-900">DELETE</span> to confirm
        </span>
        <input
          type="text"
          value={confirmation}
          onChange={(e) => setConfirmation(e.target.value)}
          autoComplete="off"
          autoCapitalize="characters"
          spellCheck={false}
          placeholder="DELETE"
          className="w-full rounded-xl2 border border-primary-100 bg-white px-3.5 py-3 text-sm text-ink-900 placeholder:text-ink-400 focus:border-secondary-500 focus:outline-none"
        />
      </label>

      {error && (
        <div role="alert" className="mt-4 flex items-start gap-2 rounded-xl2 bg-red-50 p-3 text-xs font-medium text-red-600">
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
          <span>{error}</span>
        </div>
      )}

      <button
        type="submit"
        disabled={!canSubmit}
        className="mt-5 flex w-full items-center justify-center gap-1.5 rounded-xl2 bg-red-600 px-4 py-3 text-sm font-semibold text-white transition-colors hover:bg-red-700 disabled:cursor-not-allowed disabled:opacity-50"
      >
        {busy && <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />}
        {busy ? "Deleting…" : "Delete my account permanently"}
      </button>
    </form>
  );
}
