import { useEffect, useRef, useState } from "react";
import { AlertCircle, Loader2, RotateCw, ShieldCheck } from "lucide-react";
import { initOtpWidget, retryOtpCode, sendOtpTo, verifyOtpCode } from "../../lib/msg91";

const OTP_LENGTH = 4; // Scoram's MSG91 widget is configured to send 4-digit OTPs.
const RESEND_COOLDOWN_SECONDS = 30;

// Drives MSG91's OTP widget from Scoram's own styling (Web SDK "Custom UI" mode -- see msg91.js's
// own comment) instead of MSG91's default popup. Mounted by the parent form once the person has
// entered a phone number and asked to verify it; calls onVerified(accessToken) once MSG91 confirms
// the OTP was correct. The parent should remount this (e.g. via a `key={phoneNumber}` prop) if the
// phone number itself changes -- this component's own widget session is tied to whatever
// `phoneNumber` it was mounted with.
export default function OtpEntryBox({ phoneNumber, onVerified, onChangeNumber }) {
  // Unique per mount so more than one OtpEntryBox could in theory exist on the page at once without
  // fighting over the same captcha container -- doesn't happen today (each form only shows one at a
  // time), but costs nothing to guard against.
  const captchaContainerId = useRef(`msg91-captcha-${Math.random().toString(36).slice(2)}`).current;

  // "initializing" (loading script + rendering the reCAPTCHA) -> "ready" (waiting for the person to
  // press Send OTP) -> "sending" -> "awaiting" (OTP boxes visible) -> "verifying".
  const [phase, setPhase] = useState("initializing");
  const [sentOnce, setSentOnce] = useState(false);
  const [error, setError] = useState(null);
  const [digits, setDigits] = useState(() => Array(OTP_LENGTH).fill(""));
  const [cooldown, setCooldown] = useState(0);
  const inputRefs = useRef([]);
  const initStartedRef = useRef(false); // StrictMode-safe guard so we don't init the widget twice

  useEffect(() => {
    if (initStartedRef.current) return;
    initStartedRef.current = true;

    let cancelled = false;
    initOtpWidget(captchaContainerId)
      .then(() => {
        if (!cancelled) setPhase("ready");
      })
      .catch((err) => {
        if (!cancelled) {
          setError(err.message);
          setPhase("ready");
        }
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (cooldown <= 0) return;
    const t = setTimeout(() => setCooldown((c) => c - 1), 1000);
    return () => clearTimeout(t);
  }, [cooldown]);

  async function handleSendOtp() {
    setError(null);
    setPhase("sending");
    try {
      await sendOtpTo(phoneNumber);
      setSentOnce(true);
      setPhase("awaiting");
      setCooldown(RESEND_COOLDOWN_SECONDS);
      setTimeout(() => inputRefs.current[0]?.focus(), 0);
    } catch (err) {
      setError(err.message);
      setPhase("ready");
    }
  }

  async function submitOtp(code) {
    setPhase("verifying");
    setError(null);
    try {
      const accessToken = await verifyOtpCode(code);
      onVerified(accessToken);
    } catch (err) {
      setError(err.message);
      setDigits(Array(OTP_LENGTH).fill(""));
      setTimeout(() => inputRefs.current[0]?.focus(), 0);
      setPhase("awaiting");
    }
  }

  function updateDigit(index, rawValue) {
    const value = rawValue.replace(/\D/g, "").slice(-1);
    const next = [...digits];
    next[index] = value;
    setDigits(next);

    if (value && index < OTP_LENGTH - 1) {
      inputRefs.current[index + 1]?.focus();
    }
    if (next.every((d) => d !== "")) {
      submitOtp(next.join(""));
    }
  }

  function handleKeyDown(index, e) {
    if (e.key === "Backspace" && !digits[index] && index > 0) {
      inputRefs.current[index - 1]?.focus();
    }
  }

  function handlePaste(e) {
    const pasted = e.clipboardData.getData("text").replace(/\D/g, "").slice(0, OTP_LENGTH);
    if (!pasted) return;
    e.preventDefault();
    const next = Array(OTP_LENGTH).fill("");
    for (let i = 0; i < pasted.length; i++) next[i] = pasted[i];
    setDigits(next);
    inputRefs.current[Math.min(pasted.length, OTP_LENGTH - 1)]?.focus();
    if (pasted.length === OTP_LENGTH) submitOtp(pasted);
  }

  async function handleResend() {
    if (cooldown > 0) return;
    setError(null);
    setDigits(Array(OTP_LENGTH).fill(""));
    try {
      await retryOtpCode();
      setCooldown(RESEND_COOLDOWN_SECONDS);
      setTimeout(() => inputRefs.current[0]?.focus(), 0);
    } catch (err) {
      setError(err.message);
    }
  }

  const initializing = phase === "initializing";
  const busy = phase === "sending" || phase === "verifying";

  return (
    <div className="flex flex-col gap-2.5 rounded-xl2 border border-primary-100 bg-primary-50/40 p-3">
      <div className="flex items-center gap-1.5 text-xs font-semibold text-ink-600">
        <ShieldCheck className="h-3.5 w-3.5 shrink-0 text-primary-600" strokeWidth={2} />
        <span>{sentOnce ? `Enter the OTP sent to ${phoneNumber}` : `Verify ${phoneNumber}`}</span>
        {onChangeNumber && (
          <button type="button" onClick={onChangeNumber} className="ml-auto font-semibold text-secondary-500 hover:underline">
            Change number
          </button>
        )}
      </div>

      {/* MSG91's own reCAPTCHA renders into this element once the widget initializes -- its look
         can't be restyled beyond MSG91's dashboard theme options, but it now sits inline in
         Scoram's own form instead of inside a popup. */}
      <div id={captchaContainerId} />

      {!sentOnce ? (
        <button
          type="button"
          onClick={handleSendOtp}
          disabled={initializing || phase === "sending"}
          className="flex items-center justify-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
        >
          {initializing || phase === "sending" ? (
            <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} />
          ) : (
            <ShieldCheck className="h-4 w-4" strokeWidth={2.5} />
          )}
          {initializing ? "Loading verification…" : phase === "sending" ? "Sending OTP…" : "Send OTP"}
        </button>
      ) : (
        <>
          <div className="flex items-center gap-2">
            {digits.map((digit, i) => (
              <input
                key={i}
                ref={(el) => {
                  inputRefs.current[i] = el;
                }}
                type="text"
                inputMode="numeric"
                maxLength={1}
                value={digit}
                disabled={busy}
                onChange={(e) => updateDigit(i, e.target.value)}
                onKeyDown={(e) => handleKeyDown(i, e)}
                onPaste={handlePaste}
                className="h-11 w-11 rounded-xl2 border border-primary-100 bg-white text-center text-lg font-bold text-ink-900 focus:border-secondary-500 focus:outline-none disabled:opacity-60"
              />
            ))}
            {phase === "verifying" && <Loader2 className="h-4 w-4 shrink-0 animate-spin text-primary-600" strokeWidth={2.5} />}
          </div>

          <button
            type="button"
            onClick={handleResend}
            disabled={cooldown > 0 || busy}
            className="flex items-center gap-1 self-start text-xs font-semibold text-secondary-500 hover:underline disabled:cursor-not-allowed disabled:text-ink-400 disabled:no-underline"
          >
            <RotateCw className="h-3 w-3" strokeWidth={2.5} />
            {cooldown > 0 ? `Resend OTP in ${cooldown}s` : "Resend OTP"}
          </button>
        </>
      )}

      {error && (
        <div className="flex items-start gap-2 rounded-xl2 bg-red-50 p-2.5 text-xs font-medium text-red-600">
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.25} />
          <span>{error}</span>
        </div>
      )}
    </div>
  );
}
