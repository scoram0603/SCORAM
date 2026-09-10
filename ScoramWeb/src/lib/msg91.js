// MSG91 OTP widget ("Web SDK for default UI" -- see MSG91 dashboard -> Client Side Integration).
// Loads MSG91's own script, opens THEIR popup for entering/verifying an OTP, and resolves with the
// access-token it returns. That token is not proof of anything on its own -- every caller of
// verifyPhoneWithOtp must send it to the backend (see api/auth.js's otpAccessToken/accessToken
// params) for server-side re-verification before it's trusted for anything. See Msg91Service.cs's
// own comment on why.

const SCRIPT_URLS = ["https://verify.msg91.com/otp-provider.js", "https://verify.phone91.com/otp-provider.js"];

const WIDGET_ID = import.meta.env.VITE_MSG91_WIDGET_ID;
const TOKEN_AUTH = import.meta.env.VITE_MSG91_TOKEN_AUTH;

// Cached across calls -- the script only needs loading once per page load, and every subsequent
// verifyPhoneWithOtp call (retry after a failure, switching between Register/Login-OTP/Change-Phone
// in the same session, etc.) reuses the same load.
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

// Opens MSG91's own OTP popup pre-filled for `phoneNumber` (a 10-digit Indian mobile number, no
// country code -- matches what the rest of the app stores/displays). Resolves with the access-token
// once the person completes verification inside MSG91's popup; rejects with a readable message on
// failure or if they close the popup without finishing (MSG91 calls `failure` for both).
export function verifyPhoneWithOtp(phoneNumber) {
  if (!WIDGET_ID || !TOKEN_AUTH) {
    return Promise.reject(new Error("Phone verification isn't set up yet. Please contact support."));
  }

  return loadScript().then(
    () =>
      new Promise((resolve, reject) => {
        window.initSendOTP({
          widgetId: WIDGET_ID,
          tokenAuth: TOKEN_AUTH,
          identifier: phoneNumber,
          exposeMethods: false, // MSG91's own popup UI handles everything; we only need its final callback
          success: (data) => {
            // MSG91's own docs/SDKs are inconsistent about whether this is the bare token string or
            // an object with it under "message" -- see Msg91Service.cs's identical defensive parsing
            // server-side for the full reasoning; this mirrors it client-side.
            const token = typeof data === "string" ? data : data?.message;
            if (token) resolve(token);
            else reject(new Error("Phone verification didn't return a valid token. Please try again."));
          },
          failure: (error) => {
            const message = typeof error === "string" ? error : error?.message;
            reject(new Error(message || "Phone verification failed or was cancelled."));
          },
        });
      })
  );
}
