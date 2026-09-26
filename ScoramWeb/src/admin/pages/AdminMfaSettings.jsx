import { useState } from "react";
import { ShieldCheck, ShieldOff, KeyRound, Lock, Loader2, AlertCircle, CheckCircle2, Copy } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import { setupMfa, enableMfa, disableMfa } from "../api/adminAuth";
import { friendlyError } from "../components/AdminUI";

// Opt-in per admin -- there's no "require MFA for everyone" switch (see the backend's own note on
// Admin.TotpEnabled for why forcing it on existing admins isn't done here). Each admin manages
// their own account's MFA from this page.
export default function AdminMfaSettings() {
  const { token, admin } = useAdminAuth();
  // Local-only flag for "we know MFA is on" -- the real source of truth is the JWT's own
  // mustChangePassword-style claim enforced server-side; this page doesn't have a live read of
  // admin.totpEnabled from context (login doesn't return it), so it starts by assuming MFA is off
  // and flips to "on" once this session successfully enables it. A page reload after enabling
  // elsewhere would show the setup flow again harmlessly -- clicking "Set up" or "Enable" when MFA
  // is already on just replaces it with a fresh secret, which is a supported, intentional reset path
  // (see SetupMfa's own backend comment).
  const [enabled, setEnabled] = useState(false);

  const [setupData, setSetupData] = useState(null); // { secret, provisioningUri, backupCodes }
  const [settingUp, setSettingUp] = useState(false);
  const [enableCode, setEnableCode] = useState("");
  const [enabling, setEnabling] = useState(false);
  const [copiedSecret, setCopiedSecret] = useState(false);

  const [disablePassword, setDisablePassword] = useState("");
  const [disableCode, setDisableCode] = useState("");
  const [disabling, setDisabling] = useState(false);
  const [showDisableForm, setShowDisableForm] = useState(false);

  const [error, setError] = useState(null);
  const [message, setMessage] = useState(null);

  async function handleStartSetup() {
    setError(null);
    setMessage(null);
    setSettingUp(true);
    try {
      const res = await setupMfa(token);
      setSetupData(res);
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setSettingUp(false);
    }
  }

  async function handleEnable(e) {
    e.preventDefault();
    setError(null);
    setEnabling(true);
    try {
      await enableMfa(token, enableCode);
      setEnabled(true);
      setSetupData(null);
      setEnableCode("");
      setMessage("MFA is now enabled. You'll be asked for a code the next time you log in.");
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setEnabling(false);
    }
  }

  async function handleDisable(e) {
    e.preventDefault();
    setError(null);
    setDisabling(true);
    try {
      await disableMfa(token, { currentPassword: disablePassword, code: disableCode });
      setEnabled(false);
      setShowDisableForm(false);
      setDisablePassword("");
      setDisableCode("");
      setMessage("MFA has been disabled for your account.");
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setDisabling(false);
    }
  }

  function copySecret() {
    navigator.clipboard?.writeText(setupData.secret).then(() => {
      setCopiedSecret(true);
      setTimeout(() => setCopiedSecret(false), 2000);
    });
  }

  return (
    <div className="mx-auto max-w-xl px-4 py-6">
      <div className="flex items-center gap-2">
        <ShieldCheck className="h-5 w-5 text-primary-600" strokeWidth={2.25} />
        <h1 className="text-lg font-extrabold text-ink-900">Two-factor authentication</h1>
      </div>
      <p className="mt-1 text-sm text-ink-400">
        Signed in as {admin?.email}. Adds a second step to login using an authenticator app
        (Google Authenticator, Authy, 1Password, etc.) alongside your password.
      </p>

      {message && (
        <div className="mt-4 flex items-start gap-2 rounded-xl2 bg-mint-50 p-3 text-xs font-medium text-mint-600">
          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
          <span>{message}</span>
        </div>
      )}
      {error && (
        <div className="mt-4 flex items-start gap-2 rounded-xl2 bg-red-50 p-3 text-xs font-medium text-red-600">
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
          <span>{error}</span>
        </div>
      )}

      {!setupData && !enabled && (
        <div className="mt-5 rounded-xl2 border border-primary-100 bg-white p-4">
          <p className="text-sm text-ink-600">Two-factor authentication is currently <strong>off</strong> for your account.</p>
          <button
            onClick={handleStartSetup}
            disabled={settingUp}
            className="mt-3 flex items-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
          >
            {settingUp ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} /> : <KeyRound className="h-4 w-4" strokeWidth={2.25} />}
            Set up two-factor authentication
          </button>
        </div>
      )}

      {setupData && (
        <div className="mt-5 rounded-xl2 border border-primary-100 bg-white p-4">
          <h2 className="text-sm font-bold text-ink-900">1. Add this account to your authenticator app</h2>
          <p className="mt-1 text-xs text-ink-400">
            Most apps let you paste a setup link directly, or enter the key below by hand if yours
            only supports scanning a QR code (this page doesn't render one yet).
          </p>

          <div className="mt-3 space-y-2">
            <div>
              <span className="mb-1 block text-xs font-semibold text-ink-600">Setup key</span>
              <div className="flex items-center gap-2">
                <code className="flex-1 break-all rounded-lg bg-primary-50 px-3 py-2 text-xs text-ink-900">{setupData.secret}</code>
                <button type="button" onClick={copySecret} className="shrink-0 rounded-lg border border-primary-100 p-2 text-ink-400 hover:text-ink-600" title="Copy">
                  <Copy className="h-4 w-4" strokeWidth={2} />
                </button>
              </div>
              {copiedSecret && <span className="mt-1 block text-xs text-mint-600">Copied.</span>}
            </div>
            <div>
              <span className="mb-1 block text-xs font-semibold text-ink-600">Setup link</span>
              <code className="block break-all rounded-lg bg-primary-50 px-3 py-2 text-xs text-ink-900">{setupData.provisioningUri}</code>
            </div>
          </div>

          <h2 className="mt-4 text-sm font-bold text-ink-900">2. Save your backup codes</h2>
          <p className="mt-1 text-xs text-ink-400">
            Each code works once, if you ever lose access to your authenticator app. Save these
            somewhere safe -- they won't be shown again after you leave this page.
          </p>
          <div className="mt-2 grid grid-cols-2 gap-1.5">
            {setupData.backupCodes.map((code) => (
              <code key={code} className="rounded-lg bg-primary-50 px-3 py-1.5 text-center text-xs text-ink-900">{code}</code>
            ))}
          </div>

          <h2 className="mt-4 text-sm font-bold text-ink-900">3. Confirm it's working</h2>
          <form onSubmit={handleEnable} className="mt-2 flex items-end gap-2">
            <label className="flex-1">
              <span className="mb-1 block text-xs font-semibold text-ink-600">Enter the 6-digit code from your app</span>
              <input
                type="text"
                inputMode="numeric"
                required
                value={enableCode}
                onChange={(e) => setEnableCode(e.target.value)}
                placeholder="123456"
                className="w-full rounded-xl2 border border-primary-100 px-3.5 py-2.5 text-sm tracking-widest text-ink-900 placeholder:text-ink-400 placeholder:tracking-normal focus:border-secondary-500 focus:outline-none"
              />
            </label>
            <button
              type="submit"
              disabled={enabling}
              className="flex items-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
            >
              {enabling ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} /> : "Confirm"}
            </button>
          </form>
        </div>
      )}

      {enabled && !setupData && (
        <div className="mt-5 rounded-xl2 border border-primary-100 bg-white p-4">
          <p className="text-sm text-ink-600">Two-factor authentication is currently <strong className="text-mint-600">on</strong> for your account.</p>

          {!showDisableForm ? (
            <button
              onClick={() => setShowDisableForm(true)}
              className="mt-3 flex items-center gap-1.5 rounded-xl2 border border-red-200 px-4 py-2.5 text-sm font-semibold text-red-600 transition-colors hover:bg-red-50"
            >
              <ShieldOff className="h-4 w-4" strokeWidth={2.25} />
              Turn off two-factor authentication
            </button>
          ) : (
            <form onSubmit={handleDisable} className="mt-3 flex flex-col gap-3">
              <label>
                <span className="mb-1 flex items-center gap-1.5 text-xs font-semibold text-ink-600"><Lock className="h-3.5 w-3.5" strokeWidth={2.25} /> Current password</span>
                <input
                  type="password"
                  required
                  value={disablePassword}
                  onChange={(e) => setDisablePassword(e.target.value)}
                  className="w-full rounded-xl2 border border-primary-100 px-3.5 py-2.5 text-sm text-ink-900 focus:border-secondary-500 focus:outline-none"
                />
              </label>
              <label>
                <span className="mb-1 block text-xs font-semibold text-ink-600">Code from your authenticator app (or a backup code)</span>
                <input
                  type="text"
                  required
                  value={disableCode}
                  onChange={(e) => setDisableCode(e.target.value)}
                  className="w-full rounded-xl2 border border-primary-100 px-3.5 py-2.5 text-sm tracking-widest text-ink-900 focus:border-secondary-500 focus:outline-none"
                />
              </label>
              <div className="flex gap-2">
                <button
                  type="submit"
                  disabled={disabling}
                  className="flex items-center gap-1.5 rounded-xl2 bg-red-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-red-700 disabled:opacity-60"
                >
                  {disabling ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} /> : "Confirm turn-off"}
                </button>
                <button type="button" onClick={() => setShowDisableForm(false)} className="rounded-xl2 px-4 py-2.5 text-sm font-semibold text-ink-400 hover:text-ink-600">
                  Cancel
                </button>
              </div>
            </form>
          )}
        </div>
      )}
    </div>
  );
}
