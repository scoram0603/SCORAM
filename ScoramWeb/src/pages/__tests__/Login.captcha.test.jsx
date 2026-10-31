// @vitest-environment jsdom
//
// End-to-end-ish tests for the Login/Register screen's CAPTCHA handling. Everything above `fetch`
// is real (Login.jsx -> AuthContext -> api/auth.js -> api/client.js), so what's asserted on is the
// actual HTTP payload the website sends -- the thing that was wrong: POST /api/auth/register used
// to omit captchaId/captchaAnswer and get an automatic 400 from the API's [Required] validation.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";

// The real box drives MSG91's third-party widget; here it just hands back a fake verified token.
vi.mock("../../components/auth/OtpEntryBox", () => ({
  default: ({ onVerified }) => (
    <button type="button" onClick={() => onVerified("otp-token-123")}>
      mock-verify-otp
    </button>
  ),
}));

import Login from "../Login";
import { AuthProvider } from "../../context/AuthContext";

const AUTH_OK = {
  token: "jwt",
  refreshToken: "refresh",
  userId: 1,
  username: "newuser",
  fullName: "New User",
  email: "new@example.com",
  phoneNumber: "9876543210",
  phoneVerified: true,
};

let captchaCounter;
let calls; // [{ url, method, body }]
let registerHandler;
let loginHandler;

function json(body, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

function installFetchMock() {
  captchaCounter = 0;
  calls = [];
  registerHandler = () => json(AUTH_OK);
  loginHandler = () => json(AUTH_OK);

  vi.stubGlobal(
    "fetch",
    vi.fn(async (url, init = {}) => {
      const path = String(url).replace(/^https?:\/\/[^/]+/, "");
      const body = init.body ? JSON.parse(init.body) : undefined;
      calls.push({ path, method: init.method || "GET", body });

      if (path.startsWith("/api/captcha/generate")) {
        captchaCounter += 1;
        return json({ captchaId: `cap-${captchaCounter}`, question: `${captchaCounter} + 1` });
      }
      if (path.startsWith("/api/auth/check-username")) return json({ available: true });
      if (path === "/api/auth/register") return registerHandler(body);
      if (path === "/api/auth/login") return loginHandler(body);
      return json({}); // /api/auth/me etc.
    })
  );
}

const callsTo = (path) => calls.filter((c) => c.path === path);

function renderLogin(initialEntry) {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route path="/" element={<div>HOME PAGE</div>} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>
  );
}

// Fills the whole registration form except the captcha answer and the OTP step.
async function fillRegisterForm(user) {
  await user.type(screen.getByLabelText("Username"), "newuser");
  await user.type(screen.getByLabelText("Full name"), "New User");
  await user.type(screen.getByLabelText("Email"), "new@example.com");
  await user.type(screen.getByLabelText("Phone number"), "9876543210");
  await user.type(screen.getByLabelText("Password"), "secret123");
  await user.type(screen.getByLabelText("Confirm password"), "secret123");
  await waitFor(() => expect(screen.getByText("Username is available")).toBeTruthy(), { timeout: 3000 });
}

async function verifyOtp(user) {
  await user.click(await screen.findByText("mock-verify-otp"));
}

const captchaInput = () => screen.getByLabelText("Security check answer");
const createButton = () => screen.getByRole("button", { name: /create account/i });

beforeEach(() => {
  localStorage.clear();
  installFetchMock();
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("registration CAPTCHA", () => {
  it("loads a captcha when the register form opens and shows the question", async () => {
    renderLogin("/login?mode=register");
    expect(await screen.findByText("1 + 1")).toBeTruthy();
    expect(callsTo("/api/captcha/generate")).toHaveLength(1);
  });

  it("valid form + OTP + captcha -> sends captchaId and numeric captchaAnswer and signs in", async () => {
    const user = userEvent.setup();
    renderLogin("/login?mode=register");
    await screen.findByText("1 + 1");
    await fillRegisterForm(user);
    await verifyOtp(user);
    await user.type(captchaInput(), "2");
    await user.click(createButton());

    expect(await screen.findByText("HOME PAGE")).toBeTruthy();
    const [register] = callsTo("/api/auth/register");
    expect(register.body).toMatchObject({
      username: "newuser",
      fullName: "New User",
      email: "new@example.com",
      phoneNumber: "9876543210",
      otpAccessToken: "otp-token-123",
      captchaId: "cap-1",
      captchaAnswer: 2, // a number, not "2" -- RegisterDto.CaptchaAnswer is an int
    });
    expect(register.body).not.toHaveProperty("confirmPassword");
  });

  it("keeps Create Account disabled until a captcha is loaded AND answered", async () => {
    const user = userEvent.setup();
    renderLogin("/login?mode=register");
    await screen.findByText("1 + 1");
    await fillRegisterForm(user);
    await verifyOtp(user);

    expect(createButton().disabled).toBe(true); // OTP done, captcha not answered yet
    await user.type(captchaInput(), "2");
    expect(createButton().disabled).toBe(false);
    await user.clear(captchaInput());
    expect(createButton().disabled).toBe(true);
    expect(callsTo("/api/auth/register")).toHaveLength(0); // never reached the API
  });

  it("never submits when the captcha could not be loaded", async () => {
    const user = userEvent.setup();
    // generate fails -> no challenge
    const base = globalThis.fetch;
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url, init) =>
        String(url).includes("/api/captcha/generate") ? json({ message: "down" }, 500) : base(url, init)
      )
    );
    renderLogin("/login?mode=register");
    expect(await screen.findByText("Couldn't load a question")).toBeTruthy();
    await fillRegisterForm(user);
    await verifyOtp(user);
    await user.type(captchaInput(), "2");
    expect(createButton().disabled).toBe(true);
  });

  it("wrong/expired captcha -> shows the API message, clears the answer and fetches a NEW captcha", async () => {
    const user = userEvent.setup();
    registerHandler = () => json({ message: "That captcha answer wasn't right. Please try again." }, 400);
    renderLogin("/login?mode=register");
    await screen.findByText("1 + 1");
    await fillRegisterForm(user);
    await verifyOtp(user);
    await user.type(captchaInput(), "9");
    await user.click(createButton());

    expect(await screen.findByText("That captcha answer wasn't right. Please try again.")).toBeTruthy();
    expect(await screen.findByText("2 + 1")).toBeTruthy(); // fresh challenge (single-use ids)
    expect(captchaInput().value).toBe("");
    expect(callsTo("/api/captcha/generate")).toHaveLength(2);

    // The retry must use the NEW id, never the spent one.
    registerHandler = () => json(AUTH_OK);
    await user.type(captchaInput(), "3");
    await user.click(createButton());
    await screen.findByText("HOME PAGE");
    const registers = callsTo("/api/auth/register");
    expect(registers.map((r) => r.body.captchaId)).toEqual(["cap-1", "cap-2"]);
  });

  it("a controlled API validation 400 is shown without crashing and refreshes the captcha", async () => {
    const user = userEvent.setup();
    registerHandler = () =>
      json({ title: "One or more validation errors occurred.", status: 400, errors: { CaptchaId: ["required"] } }, 400);
    renderLogin("/login?mode=register");
    await screen.findByText("1 + 1");
    await fillRegisterForm(user);
    await verifyOtp(user);
    await user.type(captchaInput(), "2");
    await user.click(createButton());

    expect(await screen.findByText("One or more validation errors occurred.")).toBeTruthy();
    expect(await screen.findByText("2 + 1")).toBeTruthy();
  });

  it("existing username/email/phone (409) keeps showing the API message and refreshes the captcha", async () => {
    const user = userEvent.setup();
    registerHandler = () => json({ message: "An account with this email already exists." }, 409);
    renderLogin("/login?mode=register");
    await screen.findByText("1 + 1");
    await fillRegisterForm(user);
    await verifyOtp(user);
    await user.type(captchaInput(), "2");
    await user.click(createButton());

    expect(await screen.findByText("An account with this email already exists.")).toBeTruthy();
    expect(await screen.findByText("2 + 1")).toBeTruthy();
  });

  it("double-click sends exactly one register request", async () => {
    const user = userEvent.setup();
    let release;
    registerHandler = () => new Promise((resolve) => (release = () => resolve(json(AUTH_OK))));
    renderLogin("/login?mode=register");
    await screen.findByText("1 + 1");
    await fillRegisterForm(user);
    await verifyOtp(user);
    await user.type(captchaInput(), "2");

    const btn = createButton();
    await user.dblClick(btn);
    expect(callsTo("/api/auth/register")).toHaveLength(1);

    release();
    expect(await screen.findByText("HOME PAGE")).toBeTruthy();
    expect(callsTo("/api/auth/register")).toHaveLength(1);
  });

  it("switching login <-> register never reuses the other screen's challenge", async () => {
    const user = userEvent.setup();
    renderLogin("/login"); // login (password) mode
    expect(await screen.findByText("1 + 1")).toBeTruthy(); // cap-1

    await user.click(screen.getByRole("button", { name: "Create an account" }));
    expect(await screen.findByText("2 + 1")).toBeTruthy(); // cap-2, fresh
    expect(screen.queryByText("1 + 1")).toBeNull();

    await user.click(screen.getByRole("button", { name: "Log in" }));
    expect(await screen.findByText("3 + 1")).toBeTruthy(); // cap-3, fresh again
    expect(callsTo("/api/captcha/generate")).toHaveLength(3);
  });
});

describe("login CAPTCHA (must not regress)", () => {
  it("still loads a captcha on the password tab and sends captchaId + numeric answer", async () => {
    const user = userEvent.setup();
    renderLogin("/login");
    await screen.findByText("1 + 1");

    await user.type(screen.getByLabelText("Email or Username"), "newuser");
    await user.type(screen.getByLabelText("Password"), "secret123");
    await user.type(captchaInput(), "2");
    await user.click(screen.getByRole("button", { name: /log in/i }));

    expect(await screen.findByText("HOME PAGE")).toBeTruthy();
    expect(callsTo("/api/auth/login")[0].body).toEqual({
      identifier: "newuser",
      password: "secret123",
      captchaId: "cap-1",
      captchaAnswer: 2,
    });
  });

  it("refreshes the captcha after a failed login", async () => {
    const user = userEvent.setup();
    loginHandler = () => json({ message: "Invalid email/username or password." }, 401);
    renderLogin("/login");
    await screen.findByText("1 + 1");
    await user.type(screen.getByLabelText("Email or Username"), "newuser");
    await user.type(screen.getByLabelText("Password"), "wrongpass");
    await user.type(captchaInput(), "2");
    await user.click(screen.getByRole("button", { name: /log in/i }));

    expect(await screen.findByText("Invalid email/username or password.")).toBeTruthy();
    expect(await screen.findByText("2 + 1")).toBeTruthy();
  });

  it("the OTP-login tab does not load or require a captcha", async () => {
    const user = userEvent.setup();
    renderLogin("/login");
    await screen.findByText("1 + 1");
    await user.click(screen.getByRole("button", { name: "OTP" }));
    await waitFor(() => expect(screen.queryByLabelText("Security check answer")).toBeNull());
  });
});
