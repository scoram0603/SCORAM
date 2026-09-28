import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Lock, Shield, CheckCircle2, AlertCircle, Eye, EyeOff, Loader2 } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { changePassword } from "../api/adminAuth";
import { friendlyError } from "../components/AdminUI";

// Password requirements that must match PasswordPolicy.Validate() on the backend
// (ScoramAPI/Extensions/PasswordPolicy.cs).  The backend is the real enforcement boundary --
// this list is only a UX aid so the admin knows what to type before submitting.
const POLICY_RULES = [
  { label: "At least 12 characters", test: (p) => p.length >= 12 },
  { label: "At least one uppercase letter", test: (p) => /[A-Z]/.test(p) },
  { label: "At least one lowercase letter", test: (p) => /[a-z]/.test(p) },
  { label: "At least one number", test: (p) => /[0-9]/.test(p) },
  { label: "At least one special character", test: (p) => /[^A-Za-z0-9]/.test(p) },
];

// Voluntary password change -- reachable from the sidebar at /admin/security.
// Distinct from AdminChangePassword.jsx, which is the forced gate (mustChangePassword flow).
// Both eventually call the same backend endpoint (PATCH /api/admin/auth/change-password), but
// the UX and post-success behaviour differ:
//  - This page stays mounted after a non-fatal error so the admin can retry.
//  - On success the session is invalidated server-side (SecurityStamp rotated); we log out locally
//    to match rather than leaving a broken session, same as the forced-change page.
export default function AdminSecuritySettings() {
  const { token, logout } = useAdminAuth();
  const navigate = useNavigate();

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword]         = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showCurrent, setShowCurrent]         = useState(false);
  const [showNew, setShowNew]                 = useState(false);
  const [showConfirm, setShowConfirm]         = useState(false);

  const [submitting, setSubmitting] = useState(false);
  const [success, setSuccess]       = useState(false);
  const [error, setError]           = useState(null);

  // Derived validation state -- shown live as the admin types, never blocking submission alone
  // (the backend re-validates everything server-side regardless of what happens here).
  const confirmMismatch = confirmPassword.length > 0 && newPassword !== confirmPassword;
  const policyMet       = POLICY_RULES.every((r) => r.test(newPassword));

  async function handleSubmit(e) {
    e.preventDefault();

    // Client-side quick checks (backend enforces independently)
    if (newPassword !== confirmPassword) {
      setError("New password and confirmation do not match.");
      return;
    }
    if (!policyMet) {
      setError("New password does not meet the requirements listed below.");
      return;
    }

    setError(null);
    setSubmitting(true);

    try {
      await changePassword(token, {
        currentPassword,
        newPassword,
        confirmNewPassword: confirmPassword,
      });
      // The backend has rotated the SecurityStamp and revoked all refresh tokens, so this session
      // is already dead server-side.  Log out locally and send the admin to login to get a fresh
      // session with the new password -- same behaviour as the forced-change page.
      setSuccess(true);
      // Brief delay so the success state is visible before the redirect.
      setTimeout(() => {
        logout();
        navigate("/admin/login?passwordChanged=1", { replace: true });
      }, 1800);
    } catch (err) {
      setError(friendlyError(err));
      setSubmitting(false);
    }
  }

  function clearForm() {
    setCurrentPassword("");
    setNewPassword("");
    setConfirmPassword("");
    setError(null);
  }

  return (
    <div className="min-h-screen bg-surface p-6">
      <div className="mx-auto max-w-lg">
        {/* Page header */}
        <div className="mb-6 flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary-100">
            <Shield className="h-5 w-5 text-primary-700" strokeWidth={2.25} />
          </div>
          <div>
            <h1 className="text-xl font-extrabold text-ink-900">Security</h1>
            <p className="text-sm text-ink-400">Manage your account password</p>
          </div>
        </div>

        {/* Change Password card */}
        <div className="rounded-xl2 border border-primary-100 bg-white shadow-card">
          <div className="border-b border-primary-100 px-6 py-4">
            <h2 className="text-base font-bold text-ink-900">Change Password</h2>
            <p className="mt-0.5 text-sm text-ink-400">
              After a successful change you'll be logged out and asked to sign in again.
            </p>
          </div>

          <form onSubmit={handleSubmit} className="flex flex-col gap-4 px-6 py-5">
            {/* Current password */}
            <PasswordField
              label="Current password"
              value={currentPassword}
              onChange={setCurrentPassword}
              show={showCurrent}
              onToggleShow={() => setShowCurrent((s) => !s)}
              placeholder="Your current password"
              autoComplete="current-password"
            />

            {/* New password */}
            <PasswordField
              label="New password"
              value={newPassword}
              onChange={setNewPassword}
              show={showNew}
              onToggleShow={() => setShowNew((s) => !s)}
              placeholder="At least 12 characters"
              autoComplete="new-password"
              minLength={12}
            />

            {/* Policy checklist -- visible once the admin starts typing */}
            {newPassword.length > 0 && (
              <ul className="-mt-2 space-y-1 pl-1">
                {POLICY_RULES.map((rule) => {
                  const met = rule.test(newPassword);
                  return (
                    <li key={rule.label} className={`flex items-center gap-1.5 text-xs font-medium ${met ? "text-green-600" : "text-ink-400"}`}>
                      <CheckCircle2
                        className={`h-3.5 w-3.5 shrink-0 ${met ? "text-green-500" : "text-ink-300"}`}
                        strokeWidth={2.5}
                      />
                      {rule.label}
                    </li>
                  );
                })}
              </ul>
            )}

            {/* Confirm password */}
            <PasswordField
              label="Confirm new password"
              value={confirmPassword}
              onChange={setConfirmPassword}
              show={showConfirm}
              onToggleShow={() => setShowConfirm((s) => !s)}
              placeholder="Re-enter your new password"
              autoComplete="new-password"
              minLength={12}
            />
            {confirmMismatch && (
              <p className="-mt-3 pl-1 text-xs font-medium text-red-600">Passwords don't match.</p>
            )}

            {/* Error banner */}
            {error && (
              <div className="flex items-start gap-2 rounded-xl2 bg-red-50 px-3 py-2.5 text-xs font-medium text-red-600">
                <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
                <span>{error}</span>
              </div>
            )}

            {/* Success banner */}
            {success && (
              <div className="flex items-start gap-2 rounded-xl2 bg-green-50 px-3 py-2.5 text-xs font-medium text-green-700">
                <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
                <span>Password updated — redirecting you to log in again…</span>
              </div>
            )}

            {/* Actions */}
            <div className="mt-1 flex items-center gap-3">
              <button
                type="submit"
                disabled={submitting || success}
                className="flex items-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
              >
                {submitting ? (
                  <>
                    <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />
                    Updating…
                  </>
                ) : (
                  "Update password"
                )}
              </button>

              <button
                type="button"
                onClick={clearForm}
                disabled={submitting || success}
                className="rounded-xl2 px-4 py-2.5 text-sm font-medium text-ink-500 transition-colors hover:bg-surface disabled:opacity-40"
              >
                Clear
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  );
}

// --------------------------------------------------------------------------
// Internal components
// --------------------------------------------------------------------------

function PasswordField({
  label,
  value,
  onChange,
  show,
  onToggleShow,
  placeholder,
  autoComplete,
  minLength,
}) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-semibold text-ink-600">{label}</span>
      <span className="flex items-center gap-2.5 rounded-xl2 border border-primary-100 bg-white px-3.5 py-3 focus-within:border-secondary-500">
        <Lock className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
        <input
          type={show ? "text" : "password"}
          required
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          autoComplete={autoComplete}
          minLength={minLength}
          className="min-w-0 flex-1 bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
        />
        <button
          type="button"
          onClick={onToggleShow}
          className="text-ink-400 hover:text-ink-600"
          tabIndex={-1}
          aria-label={show ? "Hide password" : "Show password"}
        >
          {show
            ? <EyeOff className="h-4 w-4" strokeWidth={2} />
            : <Eye    className="h-4 w-4" strokeWidth={2} />
          }
        </button>
      </span>
    </label>
  );
}
