import { useState } from "react";
import { Link } from "react-router-dom";
import {
  Lock, Phone, ArrowRight, ArrowLeft, Loader2, AlertCircle, CheckCircle2, Eye, EyeOff, ShieldCheck,
} from "lucide-react";
import logo from "../assets/scoram-logo-horizontal.png";
import { resetPassword } from "../api/auth";
import OtpEntryBox from "../components/auth/OtpEntryBox";

function isValidPhone(value) {
  return /^\d{10}$/.test(value.trim());
}

// Forgot password (logged-out). SCORAM has no email provider, so ownership is proven the same way
// OTP login does it: an MSG91 OTP on the phone number registered to the account. Steps:
//   1. enter the registered phone number and verify it with OtpEntryBox
//   2. choose a new password -> POST /api/auth/reset-password (the backend re-verifies the OTP token
//      and finds the account from the number MSG91 confirms)
//   3. success -> back to Log in
export default function ForgotPassword() {
  const [phone, setPhone] = useState("");
  const [otpAccessToken, setOtpAccessToken] = useState(null);
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showNew, setShowNew] = useState(false);
  const [showConfirm, setShowConfirm] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);
  const [done, setDone] = useState(false);

  async function handleSubmit(e) {
    e.preventDefault();
    setError(null);

    if (!otpAccessToken) {
      setError("Please verify your phone number with OTP first.");
      return;
    }
    if (newPassword !== confirmPassword) {
      setError("Passwords don't match.");
      return;
    }

    setSaving(true);
    try {
      await resetPassword({ otpAccessToken, newPassword });
      setDone(true);
    } catch (err) {
      setError(err.message || "Couldn't reset your password. Please try again.");
      // An OTP token is single-use: if the reset failed, ask for a fresh verification.
      setOtpAccessToken(null);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="flex min-h-full flex-col items-center justify-center px-6 py-10 sm:py-16">
      <img src={logo} alt="Scoram — Learn, Discuss, Score" className="h-10 w-auto object-contain" />

      <div className="mt-8 w-full max-w-sm">
        {done ? (
          <div className="text-center">
            <CheckCircle2 className="mx-auto h-12 w-12 text-mint-500" strokeWidth={1.75} />
            <h1 className="mt-4 text-xl font-extrabold text-ink-900">Password reset</h1>
            <p className="mt-1 text-sm text-ink-400">Your password has been changed. Log in with your new password.</p>
            <Link
              to="/login"
              className="mt-6 flex items-center justify-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-3 text-sm font-semibold text-white transition-colors hover:bg-primary-700"
            >
              Go to Log In
              <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
            </Link>
          </div>
        ) : (
          <>
            <h1 className="text-xl font-extrabold text-ink-900">Forgot password?</h1>
            <p className="mt-1 text-sm text-ink-400">
              Verify the phone number registered with your account, then choose a new password.
            </p>

            <form onSubmit={handleSubmit} className="mt-6 flex flex-col gap-3">
              <Field icon={Phone} label="Registered phone number">
                <input
                  type="tel"
                  required
                  maxLength={10}
                  inputMode="numeric"
                  readOnly={Boolean(otpAccessToken)}
                  value={phone}
                  onChange={(e) => {
                    setError(null);
                    setPhone(e.target.value.replace(/\D/g, ""));
                  }}
                  placeholder="98765 43210"
                  className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
                />
                {otpAccessToken && (
                  <>
                    <CheckCircle2 className="h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.25} />
                    <button
                      type="button"
                      onClick={() => setOtpAccessToken(null)}
                      className="shrink-0 text-xs font-semibold text-secondary-500 hover:underline"
                      tabIndex={-1}
                    >
                      Change
                    </button>
                  </>
                )}
              </Field>

              {isValidPhone(phone) && !otpAccessToken && (
                <OtpEntryBox
                  key={phone.trim()}
                  phoneNumber={phone.trim()}
                  onVerified={(accessToken) => setOtpAccessToken(accessToken)}
                />
              )}
              {!isValidPhone(phone) && !otpAccessToken && (
                <p className="-mt-2 flex items-center gap-1.5 pl-1 text-xs text-ink-400">
                  <ShieldCheck className="h-3.5 w-3.5 shrink-0" strokeWidth={2} />
                  We'll send an OTP to this number to confirm it's you.
                </p>
              )}

              {otpAccessToken && (
                <>
                  <Field icon={Lock} label="New password">
                    <input
                      type={showNew ? "text" : "password"}
                      required
                      minLength={6}
                      value={newPassword}
                      onChange={(e) => setNewPassword(e.target.value)}
                      placeholder="At least 6 characters"
                      autoComplete="new-password"
                      className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
                    />
                    <button type="button" onClick={() => setShowNew((s) => !s)} className="text-ink-400 hover:text-ink-600" tabIndex={-1}>
                      {showNew ? <EyeOff className="h-4 w-4" strokeWidth={2} /> : <Eye className="h-4 w-4" strokeWidth={2} />}
                    </button>
                  </Field>
                  <Field icon={Lock} label="Confirm new password">
                    <input
                      type={showConfirm ? "text" : "password"}
                      required
                      minLength={6}
                      value={confirmPassword}
                      onChange={(e) => setConfirmPassword(e.target.value)}
                      placeholder="Re-enter your new password"
                      autoComplete="new-password"
                      className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
                    />
                    <button type="button" onClick={() => setShowConfirm((s) => !s)} className="text-ink-400 hover:text-ink-600" tabIndex={-1}>
                      {showConfirm ? <EyeOff className="h-4 w-4" strokeWidth={2} /> : <Eye className="h-4 w-4" strokeWidth={2} />}
                    </button>
                  </Field>
                </>
              )}

              {error && (
                <div className="flex items-start gap-2 rounded-xl2 bg-red-50 p-3 text-xs font-medium text-red-600">
                  <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
                  <span>{error}</span>
                </div>
              )}

              <button
                type="submit"
                disabled={saving || !otpAccessToken}
                className="mt-2 flex items-center justify-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-3 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
              >
                {saving ? (
                  <>
                    <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />
                    Please wait…
                  </>
                ) : (
                  <>
                    {otpAccessToken ? "Reset Password" : "Verify phone number first"}
                    <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
                  </>
                )}
              </button>
            </form>

            <p className="mt-5 text-center text-sm text-ink-400">
              <Link to="/login" className="inline-flex items-center gap-1 font-semibold text-secondary-500 hover:underline">
                <ArrowLeft className="h-3.5 w-3.5" strokeWidth={2.5} />
                Back to Log in
              </Link>
            </p>
          </>
        )}
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
