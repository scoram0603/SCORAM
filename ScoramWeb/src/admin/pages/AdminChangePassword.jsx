import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Lock, ArrowRight, Loader2, AlertCircle, ShieldAlert, Eye, EyeOff } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { changePassword } from "../api/adminAuth";
import { friendlyError } from "../components/AdminUI";

// Reached only when admin.mustChangePassword is true (see AdminProtectedRoute, which redirects
// here and here alone until this succeeds) -- the backend's own MustChangePasswordFilter blocks
// every other admin endpoint in this state regardless of what the frontend does, so this page
// isn't the security boundary, just the UX for it.
export default function AdminChangePassword() {
  const { token, logout } = useAdminAuth();
  const navigate = useNavigate();

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showPasswords, setShowPasswords] = useState(false);
  const [confirmMismatch, setConfirmMismatch] = useState(false);
  const [error, setError] = useState(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e) {
    e.preventDefault();
    if (newPassword !== confirmPassword) {
      setConfirmMismatch(true);
      return;
    }
    setConfirmMismatch(false);
    setError(null);
    setSubmitting(true);
    try {
      await changePassword(token, { currentPassword, newPassword, confirmNewPassword: confirmPassword });
      // The backend has already invalidated this session server-side (see changePassword's own
      // comment) -- log out locally to match, and send the admin to log back in with the new
      // password rather than leaving them on a page whose every other action would now 401/403.
      logout();
      navigate("/admin/login?passwordChanged=1", { replace: true });
    } catch (err) {
      setError(friendlyError(err));
      setSubmitting(false);
    }
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-primary-900 px-6 py-10">
      <div className="w-full max-w-sm rounded-xl2 bg-white p-6 shadow-card">
        <div className="flex items-center gap-2">
          <ShieldAlert className="h-5 w-5 text-accent-600" strokeWidth={2.25} />
          <h1 className="text-lg font-extrabold text-ink-900">Change your password</h1>
        </div>
        <p className="mt-1 text-sm text-ink-400">
          Your account was set up with a temporary password. Choose a new one before continuing.
        </p>

        <form onSubmit={handleSubmit} className="mt-6 flex flex-col gap-3">
          <Field icon={Lock} label="Current (temporary) password">
            <input
              type={showPasswords ? "text" : "password"}
              required
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              placeholder="••••••••"
              className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
            />
          </Field>

          <Field icon={Lock} label="New password">
            <input
              type={showPasswords ? "text" : "password"}
              required
              minLength={12}
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              placeholder="At least 12 characters"
              className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
            />
          </Field>

          <Field icon={Lock} label="Confirm new password">
            <input
              type={showPasswords ? "text" : "password"}
              required
              minLength={12}
              value={confirmPassword}
              onChange={(e) => {
                setConfirmPassword(e.target.value);
                setConfirmMismatch(false);
              }}
              placeholder="Re-enter the new password"
              className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
            />
            <button
              type="button"
              onClick={() => setShowPasswords((s) => !s)}
              className="text-ink-400 hover:text-ink-600"
              tabIndex={-1}
            >
              {showPasswords ? <EyeOff className="h-4 w-4" strokeWidth={2} /> : <Eye className="h-4 w-4" strokeWidth={2} />}
            </button>
          </Field>
          {confirmMismatch && (
            <p className="-mt-2 pl-1 text-xs font-medium text-red-600">Passwords don't match.</p>
          )}

          {error && (
            <div className="flex items-start gap-2 rounded-xl2 bg-red-50 p-3 text-xs font-medium text-red-600">
              <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
              <span>{error}</span>
            </div>
          )}

          <button
            type="submit"
            disabled={submitting}
            className="mt-2 flex items-center justify-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-3 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
          >
            {submitting ? (
              <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />
            ) : (
              <>
                Update password
                <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
              </>
            )}
          </button>
        </form>
      </div>
    </div>
  );
}

function Field({ icon: Icon, label, children }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-semibold text-ink-600">{label}</span>
      <span className="flex items-center gap-2.5 rounded-xl2 border border-primary-100 bg-white px-3.5 py-3 focus-within:border-secondary-500">
        <Icon className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
        {children}
      </span>
    </label>
  );
}
