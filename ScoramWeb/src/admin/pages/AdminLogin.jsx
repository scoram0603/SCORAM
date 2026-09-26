import { useEffect, useState } from "react";
import { Mail, Lock, ArrowRight, Loader2, AlertCircle, CheckCircle2, ShieldCheck, KeyRound } from "lucide-react";
import { useNavigate, useSearchParams } from "react-router-dom";
import logo from "../../assets/scoram-logo-horizontal.png";
import { useAdminAuth } from "../context/AdminAuthContext";

export default function AdminLogin() {
  const { login, verifyMfaLogin, isAuthenticated, admin, isLoading, error, clearError, sessionExpired } = useAdminAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const passwordChanged = searchParams.get("passwordChanged") === "1";
  const [form, setForm] = useState({ email: "", password: "" });
  // Set once Login comes back with mfaRequired: true -- switches the form below from
  // email/password to a single code field. See handleSubmit/handleVerifyMfa.
  const [mfaChallengeToken, setMfaChallengeToken] = useState(null);
  const [mfaCode, setMfaCode] = useState("");

  // Covers a logged-in admin navigating straight to /admin/login by URL.
  useEffect(() => {
    if (isAuthenticated) navigate(admin?.mustChangePassword ? "/admin/change-password" : "/admin", { replace: true });
  }, [isAuthenticated, admin, navigate]);

  function updateField(key, value) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function handleSubmit(e) {
    e.preventDefault();
    try {
      const res = await login(form);
      if (res.mfaRequired) {
        setMfaChallengeToken(res.mfaChallengeToken);
        return;
      }
      navigate(res.mustChangePassword ? "/admin/change-password" : "/admin", { replace: true });
    } catch {
      // error is already captured in AdminAuthContext state and rendered below
    }
  }

  async function handleVerifyMfa(e) {
    e.preventDefault();
    try {
      const res = await verifyMfaLogin({ mfaChallengeToken, code: mfaCode });
      navigate(res.mustChangePassword ? "/admin/change-password" : "/admin", { replace: true });
    } catch {
      // error is already captured in AdminAuthContext state and rendered below -- stay on this
      // step so the admin can retry the code without re-entering their password.
    }
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-primary-900 px-6 py-10">
      {/* scoram-logo-horizontal.png has an opaque white background baked in (not transparent), so
          brightness-0/invert used to turn the WHOLE image -- logo and background alike -- into one
          solid white rectangle that vanished against this same dark navy page background. A white
          card behind the logo shows it in its real colors instead of fighting the asset. */}
      <div className="rounded-xl2 bg-white px-4 py-2.5 shadow-card">
        <img src={logo} alt="Scoram" className="h-9 w-auto object-contain" />
      </div>

      <div className="mt-8 w-full max-w-sm rounded-xl2 bg-white p-6 shadow-card">
        <div className="flex items-center gap-2">
          <ShieldCheck className="h-5 w-5 text-primary-600" strokeWidth={2.25} />
          <h1 className="text-lg font-extrabold text-ink-900">Admin Login</h1>
        </div>
        <p className="mt-1 text-sm text-ink-400">Restricted access — Scoram staff only.</p>

        {passwordChanged && (
          <div className="mt-4 flex items-start gap-2 rounded-xl2 bg-mint-50 p-3 text-xs font-medium text-mint-600">
            <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
            <span>Password updated. Log in with your new password.</span>
          </div>
        )}

        {sessionExpired && (
          <div className="mt-4 flex items-start gap-2 rounded-xl2 bg-accent-50 p-3 text-xs font-medium text-accent-600">
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
            <span>Your session expired. Please log in again.</span>
          </div>
        )}

        {mfaChallengeToken ? (
          <form onSubmit={handleVerifyMfa} className="mt-6 flex flex-col gap-3">
            <p className="text-sm text-ink-600">Enter the 6-digit code from your authenticator app, or one of your backup codes.</p>
            <Field icon={KeyRound} label="Code">
              <input
                type="text"
                inputMode="numeric"
                autoFocus
                required
                value={mfaCode}
                onChange={(e) => {
                  clearError();
                  setMfaCode(e.target.value);
                }}
                placeholder="123456"
                className="w-full bg-transparent text-sm tracking-widest text-ink-900 placeholder:text-ink-400 placeholder:tracking-normal focus:outline-none"
              />
            </Field>

            {error && (
              <div className="flex items-start gap-2 rounded-xl2 bg-red-50 p-3 text-xs font-medium text-red-600">
                <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
                <span>{error}</span>
              </div>
            )}

            <button
              type="submit"
              disabled={isLoading}
              className="mt-2 flex items-center justify-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-3 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
            >
              {isLoading ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} /> : (<>Verify<ArrowRight className="h-4 w-4" strokeWidth={2.5} /></>)}
            </button>
            <button
              type="button"
              onClick={() => { setMfaChallengeToken(null); setMfaCode(""); clearError(); }}
              className="text-center text-xs font-medium text-ink-400 hover:text-ink-600"
            >
              Back to login
            </button>
          </form>
        ) : (
        <form onSubmit={handleSubmit} className="mt-6 flex flex-col gap-3">
          <Field icon={Mail} label="Email">
            <input
              type="email"
              required
              value={form.email}
              onChange={(e) => {
                clearError();
                updateField("email", e.target.value);
              }}
              placeholder="you@scoram.com"
              className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
            />
          </Field>

          <Field icon={Lock} label="Password">
            <input
              type="password"
              required
              value={form.password}
              onChange={(e) => {
                clearError();
                updateField("password", e.target.value);
              }}
              placeholder="••••••••"
              className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
            />
          </Field>

          {error && (
            <div className="flex items-start gap-2 rounded-xl2 bg-red-50 p-3 text-xs font-medium text-red-600">
              <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
              <span>{error}</span>
            </div>
          )}

          <button
            type="submit"
            disabled={isLoading}
            className="mt-2 flex items-center justify-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-3 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
          >
            {isLoading ? (
              <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />
            ) : (
              <>
                Log In
                <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
              </>
            )}
          </button>
        </form>
        )}
      </div>

      <p className="mt-6 max-w-sm text-center text-xs text-primary-100">
        This is a separate login from the student account system — admin accounts are created by a
        Super Admin, not self-registered.
      </p>
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
