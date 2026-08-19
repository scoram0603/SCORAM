import { useEffect, useRef, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import {
  Mail, Lock, User as UserIcon, Phone, AtSign, ArrowRight, Loader2,
  AlertCircle, CheckCircle2, XCircle, Eye, EyeOff,
} from "lucide-react";
import logo from "../assets/scoram-logo-horizontal.png";
import { useAuth } from "../context/AuthContext";
import { checkUsername } from "../api/auth";

const USERNAME_PATTERN = /^[a-z0-9._]+$/;

export default function Login() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const redirectTo = searchParams.get("redirect") || "/";
  const [mode, setMode] = useState("login"); // "login" | "register"
  const { login, register, isLoading, error, clearError, sessionExpired } = useAuth();

  const [form, setForm] = useState({
    username: "",
    fullName: "",
    email: "",
    phoneNumber: "",
    password: "",
    confirmPassword: "",
  });
  const [identifier, setIdentifier] = useState(""); // login-mode only: email or username
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);
  const [confirmMismatch, setConfirmMismatch] = useState(false);

  const [usernameStatus, setUsernameStatus] = useState(null); // null | "checking" | "available" | "taken"
  const [usernameReason, setUsernameReason] = useState(null);
  const usernameCheckRef = useRef(null);

  function updateField(key, value) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  // Debounced live username availability check, Instagram-style.
  useEffect(() => {
    if (mode !== "register") return;
    const username = form.username.trim().toLowerCase();

    if (usernameCheckRef.current) clearTimeout(usernameCheckRef.current);

    if (!username) {
      setUsernameStatus(null);
      setUsernameReason(null);
      return;
    }
    if (username.length < 3 || !USERNAME_PATTERN.test(username)) {
      setUsernameStatus("invalid");
      setUsernameReason(
        username.length < 3
          ? "At least 3 characters"
          : "Only lowercase letters, numbers, dots, and underscores"
      );
      return;
    }

    setUsernameStatus("checking");
    usernameCheckRef.current = setTimeout(async () => {
      try {
        const res = await checkUsername(username);
        setUsernameStatus(res.available ? "available" : "taken");
        setUsernameReason(res.reason || null);
      } catch {
        setUsernameStatus(null);
      }
    }, 400);

    return () => clearTimeout(usernameCheckRef.current);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form.username, mode]);

  async function handleSubmit(e) {
    e.preventDefault();

    if (mode === "register") {
      if (form.password !== form.confirmPassword) {
        setConfirmMismatch(true);
        return;
      }
      if (usernameStatus !== "available") return;
    }
    setConfirmMismatch(false);

    try {
      if (mode === "login") {
        await login({ identifier, password: form.password });
      } else {
        await register({ ...form, username: form.username.trim().toLowerCase() });
      }
      navigate(redirectTo, { replace: true });
    } catch {
      // error is already captured in AuthContext state and rendered below
    }
  }

  return (
    <div className="flex min-h-full flex-col items-center justify-center px-6 py-10 sm:py-16">
      <img src={logo} alt="Scoram — Learn, Discuss, Score" className="h-10 w-auto object-contain" />

      <div className="mt-8 w-full max-w-sm">
        {sessionExpired && (
          <div className="mb-4 flex items-start gap-2 rounded-xl2 border border-accent-100 bg-accent-50 p-3 text-sm text-accent-600">
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
            Your session expired. Please log in again.
          </div>
        )}

        <h1 className="text-xl font-extrabold text-ink-900">
          {mode === "login" ? "Welcome back" : "Create your account"}
        </h1>
        <p className="mt-1 text-sm text-ink-400">
          {mode === "login"
            ? "Log in to continue your practice."
            : "Join thousands of aspirants preparing with Scoram."}
        </p>

        <form onSubmit={handleSubmit} className="mt-6 flex flex-col gap-3">
          {mode === "login" && (
            <Field icon={AtSign} label="Email or Username">
              <input
                type="text"
                required
                value={identifier}
                onChange={(e) => {
                  clearError();
                  setIdentifier(e.target.value);
                }}
                placeholder="you@example.com or username"
                className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
              />
            </Field>
          )}

          {mode === "register" && (
            <>
              <Field icon={AtSign} label="Username">
                <input
                  type="text"
                  required
                  value={form.username}
                  onChange={(e) => updateField("username", e.target.value)}
                  placeholder="e.g. durgesh_k07"
                  autoCapitalize="none"
                  className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
                />
                <UsernameStatusIcon status={usernameStatus} />
              </Field>
              {usernameStatus && usernameStatus !== "available" && usernameReason && (
                <p className="-mt-2 pl-1 text-xs font-medium text-red-600">{usernameReason}</p>
              )}
              {usernameStatus === "available" && (
                <p className="-mt-2 pl-1 text-xs font-medium text-mint-500">Username is available</p>
              )}

              <Field icon={UserIcon} label="Full name">
                <input
                  type="text"
                  required
                  value={form.fullName}
                  onChange={(e) => updateField("fullName", e.target.value)}
                  placeholder="Durgesh Kumar"
                  className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
                />
              </Field>

              <Field icon={Mail} label="Email">
                <input
                  type="email"
                  required
                  value={form.email}
                  onChange={(e) => updateField("email", e.target.value)}
                  placeholder="you@example.com"
                  className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
                />
              </Field>

              <Field icon={Phone} label="Phone number">
                <input
                  type="tel"
                  required
                  value={form.phoneNumber}
                  onChange={(e) => updateField("phoneNumber", e.target.value)}
                  placeholder="98765 43210"
                  className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
                />
              </Field>
            </>
          )}

          <Field icon={Lock} label="Password">
            <input
              type={showPassword ? "text" : "password"}
              required
              minLength={6}
              value={form.password}
              onChange={(e) => updateField("password", e.target.value)}
              placeholder="At least 6 characters"
              className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
            />
            <button
              type="button"
              onClick={() => setShowPassword((s) => !s)}
              className="text-ink-400 hover:text-ink-600"
              tabIndex={-1}
            >
              {showPassword ? <EyeOff className="h-4 w-4" strokeWidth={2} /> : <Eye className="h-4 w-4" strokeWidth={2} />}
            </button>
          </Field>

          {mode === "register" && (
            <>
              <Field icon={Lock} label="Confirm password">
                <input
                  type={showConfirmPassword ? "text" : "password"}
                  required
                  minLength={6}
                  value={form.confirmPassword}
                  onChange={(e) => {
                    updateField("confirmPassword", e.target.value);
                    setConfirmMismatch(false);
                  }}
                  placeholder="Re-enter your password"
                  className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
                />
                <button
                  type="button"
                  onClick={() => setShowConfirmPassword((s) => !s)}
                  className="text-ink-400 hover:text-ink-600"
                  tabIndex={-1}
                >
                  {showConfirmPassword ? <EyeOff className="h-4 w-4" strokeWidth={2} /> : <Eye className="h-4 w-4" strokeWidth={2} />}
                </button>
              </Field>
              {confirmMismatch && (
                <p className="-mt-2 pl-1 text-xs font-medium text-red-600">Passwords don't match.</p>
              )}
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
            disabled={isLoading || (mode === "register" && usernameStatus !== "available")}
            className="mt-2 flex items-center justify-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-3 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
          >
            {isLoading ? (
              <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />
            ) : (
              <>
                {mode === "login" ? "Log In" : "Create Account"}
                <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
              </>
            )}
          </button>
        </form>

        <p className="mt-5 text-center text-sm text-ink-400">
          {mode === "login" ? "New to Scoram?" : "Already have an account?"}{" "}
          <button
            type="button"
            onClick={() => {
              clearError();
              setMode(mode === "login" ? "register" : "login");
            }}
            className="font-semibold text-secondary-500 hover:underline"
          >
            {mode === "login" ? "Create an account" : "Log in"}
          </button>
        </p>
      </div>
    </div>
  );
}

function UsernameStatusIcon({ status }) {
  if (status === "checking") return <Loader2 className="h-4 w-4 shrink-0 animate-spin text-ink-400" strokeWidth={2.25} />;
  if (status === "available") return <CheckCircle2 className="h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.25} />;
  if (status === "taken" || status === "invalid") return <XCircle className="h-4 w-4 shrink-0 text-red-500" strokeWidth={2.25} />;
  return null;
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
