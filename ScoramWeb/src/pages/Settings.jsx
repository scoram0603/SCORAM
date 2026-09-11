import { useState } from "react";
import { Link } from "react-router-dom";
import { Lock, Mail, Phone, Eye, EyeOff, AlertCircle, CheckCircle2, Loader2, FileText, Shield } from "lucide-react";
import { useAuth } from "../context/AuthContext";
import OtpEntryBox from "../components/auth/OtpEntryBox";

function isValidPhone(value) {
  return /^\d{10}$/.test(value.trim());
}

export default function Settings() {
  return (
    <div className="flex flex-col items-center px-6 py-10 sm:py-16">
      <h1 className="text-xl font-extrabold text-ink-900">Settings</h1>
      <p className="mt-1 text-sm text-ink-400">Manage your account and security.</p>

      <ChangePasswordCard />
      <ChangeEmailCard />
      <ChangePhoneCard />
      <LegalCard />
    </div>
  );
}

// Required reading, not a form -- just two links out to the standalone legal pages (see
// LegalPage.jsx). Same card shell as the settings forms above for visual consistency.
function LegalCard() {
  return (
    <SettingsCard icon={Shield} title="Legal">
      <div className="mt-3 flex flex-col gap-1">
        <Link
          to="/terms"
          target="_blank"
          rel="noopener noreferrer"
          className="flex items-center gap-2.5 rounded-xl2 px-1 py-2 text-sm font-medium text-ink-600 hover:text-primary-600"
        >
          <FileText className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
          Terms of Service
        </Link>
        <Link
          to="/privacy-policy"
          target="_blank"
          rel="noopener noreferrer"
          className="flex items-center gap-2.5 rounded-xl2 px-1 py-2 text-sm font-medium text-ink-600 hover:text-primary-600"
        >
          <Shield className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
          Privacy Policy
        </Link>
      </div>
    </SettingsCard>
  );
}

// Shared shape every card below follows: a section title, one or two "new value" fields, a current
// password confirmation field, a Save button, and inline success/error feedback -- same
// success-in-mint / error-in-red convention Login.jsx already uses for this kind of form.
function SettingsCard({ icon: Icon, title, description, children }) {
  return (
    <div className="mt-6 w-full max-w-sm rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
      <div className="flex items-center gap-3">
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary-50 text-primary-600">
          <Icon className="h-4 w-4" strokeWidth={2} />
        </span>
        <div className="min-w-0">
          <p className="text-sm font-bold text-ink-900">{title}</p>
          {description && <p className="text-xs text-ink-400">{description}</p>}
        </div>
      </div>
      {children}
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

function PasswordInput({ value, onChange, placeholder, autoComplete }) {
  const [show, setShow] = useState(false);
  return (
    <>
      <input
        type={show ? "text" : "password"}
        required
        minLength={6}
        value={value}
        onChange={onChange}
        placeholder={placeholder}
        autoComplete={autoComplete}
        className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
      />
      <button type="button" onClick={() => setShow((s) => !s)} className="text-ink-400 hover:text-ink-600" tabIndex={-1}>
        {show ? <EyeOff className="h-4 w-4" strokeWidth={2} /> : <Eye className="h-4 w-4" strokeWidth={2} />}
      </button>
    </>
  );
}

function FeedbackMessage({ error, success }) {
  if (error) {
    return (
      <div className="mt-3 flex items-start gap-2 rounded-xl2 bg-red-50 p-3 text-xs font-medium text-red-600">
        <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
        <span>{error}</span>
      </div>
    );
  }
  if (success) {
    return (
      <div className="mt-3 flex items-start gap-2 rounded-xl2 bg-mint-50 p-3 text-xs font-medium text-mint-600">
        <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
        <span>{success}</span>
      </div>
    );
  }
  return null;
}

// `disabled`/`label` are optional -- ChangePasswordCard/ChangeEmailCard don't pass them and keep
// behaving exactly as before; ChangePhoneCard uses them to gate saving on OTP verification below.
function SaveButton({ saving, disabled = false, label = "Save" }) {
  return (
    <button
      type="submit"
      disabled={saving || disabled}
      className="mt-3 flex w-full items-center justify-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
    >
      {saving && <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />}
      {label}
    </button>
  );
}

function ChangePasswordCard() {
  const { updatePassword } = useAuth();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);
  const [success, setSuccess] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    setError(null);
    setSuccess(null);

    if (newPassword !== confirmPassword) {
      setError("New passwords don't match.");
      return;
    }

    setSaving(true);
    try {
      await updatePassword({ currentPassword, newPassword });
      setSuccess("Password updated successfully.");
      setCurrentPassword("");
      setNewPassword("");
      setConfirmPassword("");
    } catch (err) {
      setError(err.message || "Couldn't update your password. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <SettingsCard icon={Lock} title="Change Password">
      <form onSubmit={handleSubmit} className="mt-3 flex flex-col gap-3">
        <Field icon={Lock} label="Current password">
          <PasswordInput value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} placeholder="Your current password" autoComplete="current-password" />
        </Field>
        <Field icon={Lock} label="New password">
          <PasswordInput value={newPassword} onChange={(e) => setNewPassword(e.target.value)} placeholder="At least 6 characters" autoComplete="new-password" />
        </Field>
        <Field icon={Lock} label="Confirm new password">
          <PasswordInput value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)} placeholder="Re-enter your new password" autoComplete="new-password" />
        </Field>
        <FeedbackMessage error={error} success={success} />
        <SaveButton saving={saving} />
      </form>
    </SettingsCard>
  );
}

function ChangeEmailCard() {
  const { user, updateEmail } = useAuth();
  const [newEmail, setNewEmail] = useState("");
  const [currentPassword, setCurrentPassword] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);
  const [success, setSuccess] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    setError(null);
    setSuccess(null);
    setSaving(true);
    try {
      await updateEmail({ currentPassword, newEmail });
      setSuccess("Email updated successfully.");
      setNewEmail("");
      setCurrentPassword("");
    } catch (err) {
      setError(err.message || "Couldn't update your email. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <SettingsCard icon={Mail} title="Change Email" description={`Current: ${user.email}`}>
      <form onSubmit={handleSubmit} className="mt-3 flex flex-col gap-3">
        <Field icon={Mail} label="New email">
          <input
            type="email"
            required
            value={newEmail}
            onChange={(e) => setNewEmail(e.target.value)}
            placeholder="you@example.com"
            className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
          />
        </Field>
        <Field icon={Lock} label="Current password">
          <PasswordInput value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} placeholder="Confirm it's you" autoComplete="current-password" />
        </Field>
        <FeedbackMessage error={error} success={success} />
        <SaveButton saving={saving} />
      </form>
    </SettingsCard>
  );
}

function ChangePhoneCard() {
  const { user, updatePhoneNumber } = useAuth();
  const [newPhoneNumber, setNewPhoneNumber] = useState("");
  const [currentPassword, setCurrentPassword] = useState("");
  // The OTP access-token OtpEntryBox hands back once newPhoneNumber has been verified (see
  // OtpEntryBox.jsx / lib/msg91.js). Saving is disabled until this is set -- see
  // ChangePhoneDto.OtpAccessToken's own comment on the backend for why it's re-checked there too.
  const [otpAccessToken, setOtpAccessToken] = useState(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);
  const [success, setSuccess] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    setError(null);
    setSuccess(null);
    if (!otpAccessToken) {
      setError("Please verify the new phone number first.");
      return;
    }
    setSaving(true);
    try {
      await updatePhoneNumber({ currentPassword, newPhoneNumber, otpAccessToken });
      setSuccess("Phone number updated and verified successfully.");
      setNewPhoneNumber("");
      setCurrentPassword("");
      setOtpAccessToken(null);
    } catch (err) {
      setError(err.message || "Couldn't update your phone number. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  const currentPhoneDescription = user.phoneNumber
    ? `Current: ${user.phoneNumber}${user.phoneVerified ? " (verified)" : " (not verified)"}`
    : undefined;

  return (
    <SettingsCard icon={Phone} title="Change Phone Number" description={currentPhoneDescription}>
      <form onSubmit={handleSubmit} className="mt-3 flex flex-col gap-3">
        <Field icon={Phone} label="New phone number">
          <input
            type="tel"
            required
            maxLength={10}
            inputMode="numeric"
            readOnly={Boolean(otpAccessToken)}
            value={newPhoneNumber}
            onChange={(e) => setNewPhoneNumber(e.target.value.replace(/\D/g, ""))}
            placeholder="98765 43210"
            className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none disabled:opacity-60"
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

        {isValidPhone(newPhoneNumber) && !otpAccessToken && (
          <OtpEntryBox
            key={newPhoneNumber.trim()}
            phoneNumber={newPhoneNumber.trim()}
            onVerified={(accessToken) => setOtpAccessToken(accessToken)}
          />
        )}

        <Field icon={Lock} label="Current password">
          <PasswordInput value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} placeholder="Confirm it's you" autoComplete="current-password" />
        </Field>
        {!isValidPhone(newPhoneNumber) && !otpAccessToken && (
          <p className="-mt-1 pl-1 text-xs text-ink-400">We'll send an OTP to the new number to verify it before saving.</p>
        )}
        <FeedbackMessage error={error} success={success} />
        <SaveButton saving={saving} disabled={!otpAccessToken} label={otpAccessToken ? "Save" : "Verify phone number first"} />
      </form>
    </SettingsCard>
  );
}
