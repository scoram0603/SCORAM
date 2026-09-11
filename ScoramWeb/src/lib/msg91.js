// MSG91 OTP widget ("Web SDK for custom UI" -- see MSG91 dashboard -> Client Side Integration).
// Loads MSG91's own script and initializes the widget with exposeMethods: true, which attaches
// window.sendOtp/verifyOtp/retryOtp and (crucially) suppresses MSG91's own popup UI entirely. Our
// own OtpEntryBox component (see components/auth/OtpEntryBox.jsx) calls the three functions below
// to drive that widget from Scoram's own styled inline UI instead. The final access-token is not
// proof of anything on its own -- every caller must still send it to the backend (see api/auth.js's
// otpAccessToken/accessToken params) for server-side re-verification before it's trusted for
// anything. See Msg91Service.cs's own comment on why -- nothing there changes with this file.

const SCRIPT_URLS = ["https://verify.msg91.com/otp-provider.js", "https://verify.phone91.com/otp-provider.js"];

const WIDGET_ID = import.meta.env.VITE_MSG91_WIDGET_ID;
const TOKEN_AUTH = import.meta.env.VITE_MSG91_TOKEN_AUTH;

// Cached across calls -- the script only needs loading once per page load, and every subsequent
// initOtpWidget call (Register, Login-OTP, Settings' Change-Phone, a retry after failure, etc.)
// reuses the same load.
let scriptLoadPromise = null;

function loadScript() {
  if (scriptLoadPromise) return scriptLoadPromise;

  scriptLoadPromise = new Promise((resolve, reject) => {
    let i = 0;
    function attempt() {
      const script = document.createElement("script");
      script.src = SCRIPT_URLS[i];
      script.async = true;
      script.onload = () => {
        if (typeof window.initSendOTP === "function") resolve();
        else reject(new Error("Couldn't load the phone verification widget. Please try again."));
      };
      script.onerror = () => {
        i += 1;
        if (i < SCRIPT_URLS.length) attempt();
        else reject(new Error("Couldn't load the phone verification widget. Please check your connection and try again."));
      };
      document.head.appendChild(script);
    }
    attempt();
  });

  return scriptLoadPromise;
}

// Initializes the widget in headless/custom-UI mode, rendering its reCAPTCHA into the element whose
// id is `captchaRenderId` (must already be in the DOM -- OtpEntryBox renders it before calling this).
// Resolves once window.sendOtp/verifyOtp/retryOtp are attached and ready to call. Re-runs
// window.initSendOTP fresh every call (safe/cheap -- the popup-mode version did the same on every
// verify), so a fresh widgetId/captcha pairing is set up each time OtpEntryBox mounts.
export function initOtpWidget(captchaRenderId) {
  if (!WIDGET_ID || !TOKEN_AUTH) {
    return Promise.reject(new Error("Phone verification isn't set up yet. Please contact support."));
  }

  return loadScript().then(
    () =>
      new Promise((resolve, reject) => {
        window.initSendOTP({
          widgetId: WIDGET_ID,
          tokenAuth: TOKEN_AUTH,
          exposeMethods: true,
          captchaRenderId,
          // Custom-UI mode: OtpEntryBox listens to sendOtp/verifyOtp/retryOtp's own per-call
          // callbacks instead (see sendOtpTo/verifyOtpCode/retryOtpCode below) -- MSG91's docs call
          // this out as the supported pattern once exposeMethods is on. These two are just a
          // safety net for anything that slips through uncaught rather than the real handlers.
          success: () => {},
          failure: () => {},
        });

        // window.initSendOTP attaches sendOtp/verifyOtp/retryOtp synchronously in every version
        // we've tested, but poll briefly rather than assume -- cheap insurance against a future
        // MSG91 script update that defers it (e.g. behind the reCAPTCHA script's own load).
        let attempts = 0;
        const check = () => {
          if (typeof window.sendOtp === "function" && typeof window.verifyOtp === "function") {
            resolve();
          } else if (attempts++ < 30) {
            setTimeout(check, 100);
          } else {
            reject(new Error("Couldn't load the phone verification widget. Please try again."));
          }
        };
        check();
      })
  );
}

// Sends the OTP to `phoneNumber` (a 10-digit Indian mobile number, no country code -- matches what
// the rest of the app stores/displays). Must be called after initOtpWidget resolves. Unlike the old
// popup's `identifier` field, MSG91's exposed sendOtp() wants the country code WITHOUT a leading "+"
// -- this app is India-only (see Msg91Service.cs's identical assumption server-side), so it's always
// prefixed with a bare "91".
export function sendOtpTo(phoneNumber) {
  return new Promise((resolve, reject) => {
    window.sendOtp(
      `91${phoneNumber}`,
      () => resolve(),
      (error) => {
        const message = typeof error === "string" ? error : error?.message;
        reject(new Error(message || "Couldn't send the OTP. Please try again."));
      }
    );
  });
}

// Verifies the OTP the person typed and resolves with the access-token on success -- same
// server-re-verified-token contract the old popup flow produced.
export function verifyOtpCode(otp) {
  return new Promise((resolve, reject) => {
    window.verifyOtp(
      Number(otp),
      (data) => {
        // MSG91's own docs/SDKs are inconsistent about whether this is the bare token string or an
        // object with it under "message" -- see Msg91Service.cs's identical defensive parsing
        // server-side for the full reasoning; this mirrors it client-side.
        const token = typeof data === "string" ? data : data?.message;
        if (token) resolve(token);
        else reject(new Error("Phone verification didn't return a valid token. Please try again."));
      },
      (error) => {
        const message = typeof error === "string" ? error : error?.message;
        reject(new Error(message || "Invalid OTP. Please try again."));
      }
    );
  });
}

// Requests a fresh OTP on the same widget session started by sendOtpTo. `null` channel means "use
// whatever channel the widget is configured with in the MSG91 dashboard" -- there's no per-call
// channel picker in Scoram's UI today.
export function retryOtpCode() {
  return new Promise((resolve, reject) => {
    window.retryOtp(
      null,
      () => resolve(),
      (error) => {
        const message = typeof error === "string" ? error : error?.message;
        reject(new Error(message || "Couldn't resend the OTP. Please try again."));
      }
    );
  });
}
