# Scoram Web — Frontend (Phase 4: Discussions, Recent Tests & Mock Test Attempt UI)

## New in this pass — Group Chat (student + admin)

`pages/GroupChat.jsx` — one chat room per exam, join optional. Text/image/document messages,
@mention autocomplete (room members only), polls with live vote percentages, admin Notices rendered
distinctly, report-a-message, delete-your-own-message. `context/ChatConnectionContext.jsx` holds one
SignalR connection for the whole authenticated session (not just while the chat page is open) — this is
specifically what makes @mention notifications arrive no matter which page the student is currently on.

`admin/pages/ChatModeration.jsx` — rooms list with lock/unlock, per-room member management (remove/ban),
post Notice, create/close Poll, reports queue, banned-words list. Every action is gated by its own
specific permission via `useAdminAuth().hasPermission(...)`, matching the six new granular permissions on
the backend (see the ScoramAPI README for the full endpoint list).

Added the `@microsoft/signalr` package (client library for connecting to the ASP.NET Core SignalR hub).



Reviewed the Home page for honest, working UX: `StreakXPCard` (fake streak/XP) is deleted until
Gamification exists; `PopularExams` and `TodaysChallenge` now use real data (`GET /api/exams`,
new `GET /api/questions/today`) instead of hardcoded numbers; every Quick Access / Popular Exam /
"PYQ Bank" sidebar card now actually navigates somewhere instead of being a dead button. Details in the
ScoramAPI README (the new endpoint lives there).

## New in this pass — Registration/Login overhaul + Search & Browse

`pages/Login.jsx`: registration now collects **Username** (live availability check as you type, debounced
400ms, via `GET /api/auth/check-username`), required **Phone Number**, and a **Confirm Password** field —
both password fields have an eye-icon show/hide toggle. Login takes a single "Email or Username" field
(`identifier`) instead of just email.

`pages/SearchQuestions.jsx` is now two experiences on one page, matching the earlier design decision:
- **Type in the search bar** → instant, typo-tolerant results from Meilisearch (`api/questions.js` ->
  `instantSearch()`), rendered as lightweight cards; tap one to expand and load its full detail
  (options/explanation) on demand
- **Leave the search bar empty** → "Browse by Exam": cascading dropdowns (Exam → Year → Shift → Language,
  new `api/papers.js`) that only ever show options that actually exist for Published papers — never a
  dead-end selection. If a Shift/Language combination has multiple question Sets, a Set picker appears;
  otherwise it jumps straight to the paper, shown in original Question No. order with a subject filter

`components/questions/QuestionCard.jsx` now renders the question/option images from the Paper image-upload
feature, when present.

### What's not built yet
- No "recent searches" or search history
- No visual distinction yet between a fresh vs. a rejected-then-resubmitted paper's questions in the
  Browse view (not necessary for students, since they only ever see Published papers anyway)

## New — Admin Panel at `/admin`

A completely separate admin panel lives at **`/admin`** — not linked anywhere in the student UI (no nav
item, no footer link). Admins reach it by going straight to the URL. This is standard practice for
consumer apps: the admin surface shouldn't be discoverable by a normal browsing user.

- **`src/main.jsx`** checks `window.location.pathname` and renders `AdminApp` instead of the student
  `App` for anything under `/admin`. No router library was added for this — it's a single top-level
  split, so a full router would've been overkill.
- **Separate session from the student login** — `src/admin/context/AdminAuthContext.jsx` stores its
  token/user under different `localStorage` keys (`scoram_admin_token` / `scoram_admin_user`) than the
  student `AuthContext`, so testing both in the same browser never has one login silently overwrite
  the other.
- **Login**: `src/admin/pages/AdminLogin.jsx` → `POST /api/admin/auth/login`. Use the seeded Super Admin
  (`superadmin@scoram.com` / `SuperAdmin@123`) to log in the first time — see the ScoramAPI README for
  details on that seed.

### Screens in this pass

| Screen | File | What it does |
|---|---|---|
| Dashboard | `src/admin/pages/AdminDashboard.jsx` | Exam/question/task counts, shortcuts into the other screens |
| Upload PYQ | `src/admin/pages/PyqUploadWizard.jsx` | 3-step wizard: choose/create exam (with logo) → choose language → question form |
| Tasks | `src/admin/pages/TaskManagement.jsx` | Admin: own tasks + status updates. Super Admin: all tasks + assign new ones |
| Manage Admins | `src/admin/pages/ManageAdmins.jsx` | Super Admin only: create admin accounts, activate/deactivate |

`src/admin/components/AdminUI.jsx` holds small shared primitives (Card, Button, FormField, StatusBadge,
Alert) used across all four screens so they stay visually consistent without duplicating Tailwind class
strings everywhere.

### Backend enum change that this UI depends on

`ScoramAPI/Program.cs` now registers a global `JsonStringEnumConverter`, so enum fields serialize as
strings (`"Easy"`, `"A"`, `"Pending"`, `"SuperAdmin"`, ...) instead of raw integers in **both** directions
(requests and responses). The PYQ upload form sends `difficultyLevel: "Medium"` / `correctOption: "A"`
directly — if you see those fields silently failing to bind on the backend, double check that change is
present. This was a genuinely pre-existing gap (not something this pass broke) — nothing in the app sent
these fields before, so it never surfaced until now.

### ⚠️ Deploying to production: same SPA-fallback rule applies to `/admin`

Vite's **dev server** already serves `index.html` for any unmatched path (including `/admin`), so
`npm run dev` needs no extra config. A **production static host** (Vercel, Nginx, S3+CloudFront, etc.)
needs a catch-all rewrite to `index.html` for this to work too — the same rule your host almost certainly
already needs for the student app's client-side routing, just confirm it also covers `/admin/*`.

### What's not built for the admin panel yet

- No "forgot password" for admins (same gap as the backend — see ScoramAPI README)
- No bulk/CSV question upload — the wizard is one question at a time, matching the API today
- No exam edit/delete UI (the API doesn't have those endpoints yet either)
- No "edit task" UI for Super Admins (create + status-update only; the API supports editing but this pass
  didn't wire it up)

---

React + Vite + Tailwind CSS frontend for **Scoram**. This pass wires three more real backend modules into
the UI — **Discussions**, **Recent Tests**, and a full **Mock Test attempt experience** (list → timed
attempt → auto-graded result with detailed solutions) — on top of the Auth and Question Search wiring from
the previous pass.

## What's genuinely live vs. still mock

| Feature | Status |
|---|---|
| Register / Login | **Live** — `src/context/AuthContext.jsx`, `src/pages/Login.jsx` |
| Search Questions | **Live** — `src/pages/SearchQuestions.jsx`, real filters + pagination |
| Top Discussions (Home preview) | **Live** — `src/components/home/TopDiscussions.jsx`, real upvotes |
| Discussions (full page) | **Live** — `src/pages/Discussions.jsx`, paginated feed, upvote + reply |
| Recent Tests (Home preview) | **Live** — `src/components/home/RecentTests.jsx`, guest-gated |
| Test (full attempt flow) | **Live** — `src/pages/Tests.jsx`: browse → timed attempt → auto-graded result → history |
| Sidebar / TopBar profile | **Live** — shows real logged-in user, or a "Guest" + Log In prompt |
| Today's Challenge, Popular Exams counts, Streak/XP card | **Mock** — no matching backend endpoint yet (each file has a comment explaining exactly why) |

## New pieces

- **`src/api/client.js`** — fetch wrapper: base URL from `VITE_API_BASE_URL`, attaches the JWT
  `Authorization` header when needed, and turns network failures into a readable
  "Couldn't reach the Scoram API — is the backend running?" message instead of a cryptic exception
- **`src/api/auth.js`** / **`src/api/questions.js`** — thin functions mirroring
  `AuthController` / `QuestionsController` exactly
- **`src/context/AuthContext.jsx`** — real login/register/logout state, JWT + user persisted to
  `localStorage` (this is a normal deployed web app, not a Claude.ai artifact, so `localStorage` is the
  right, standard tool here)
- **`src/pages/Login.jsx`** — combined login/register form, wired to the real backend, with loading and
  error states
- **`src/pages/SearchQuestions.jsx`** — real search page: keyword + subject + difficulty filters,
  pagination, loading/error/empty states, and a helpful hint if the API is unreachable
- **`src/pages/Profile.jsx`** — shows the real authenticated user; logout
- Sidebar/TopBar/BottomNav now reflect real auth state (Guest vs. logged in) instead of a hardcoded user
- **`src/api/discussions.js`** / **`src/api/mockTests.js`** — thin functions mirroring
  `DiscussionsController` / `MockTestsController` exactly
- **`src/pages/Discussions.jsx`** — paginated global feed (`GET /api/discussions`), with working upvote
  and an inline reply box per item
- **`src/pages/Tests.jsx`** — container that switches between three sub-views as a small local state
  machine (no router needed for this):
  - **`src/components/tests/TestsList.jsx`** — available tests (public) + the student's own attempt
    history (auth-gated, guest sees a Log In prompt instead of an error)
  - **`src/components/tests/TestAttempt.jsx`** — the actual timed attempt: countdown timer with
    auto-submit at zero, a question palette to jump between questions, answer selection, and a
    confirm-before-submit prompt if questions are left unanswered
  - **`src/components/tests/TestResultView.jsx`** — score/accuracy/correct/wrong stat cards plus a full
    per-question breakdown (your answer vs. the correct one, highlighted, with explanations)
- **`src/utils/format.js`** — shared `timeAgo`, `isRecent`, `formatDuration` helpers used across the new
  discussion and test-history views
- `src/components/home/TopDiscussions.jsx` / `RecentTests.jsx` — rewritten to fetch real data instead of
  reading `mockData.js`

## GUID migration

Every `Id` and foreign key across all 27 backend tables changed from `int` to `Guid`
(`Guid.NewGuid()` default). On the frontend this is nearly invisible — IDs are just opaque strings — but a
few call sites needed updating: `QuestionResponseDto.Id`, `AuthResponseDto.UserId`, and the question detail
route now expects a GUID string rather than a number.

## Setup

```bash
npm install
cp .env.example .env
# edit .env — match VITE_API_BASE_URL to whichever ScoramAPI profile you're
# actually running (check the "http" vs "https" dropdown in Visual Studio, or
# ScoramAPI/Properties/launchSettings.json):
#   http profile  -> http://localhost:5192
#   https profile -> https://localhost:7192
npm run dev
```

Without a `.env`, the app falls back to `http://localhost:5192` and will show a clear
"can't reach the API" message on the Search Questions page if that's wrong — it won't fail silently.

**Confirmed working end-to-end** against a real running `ScoramAPI` (not just the sandbox mock): registered
a real account through the UI, got a real JWT + GUID `userId` back, and had it persist through a reload.
Getting there surfaced one real backend bug — a CORS-blocking middleware ordering issue in `Program.cs` —
documented and fixed in `ScoramAPI/README.md`. If you pulled an older copy of the backend, apply that one
swap (`UseCors` before `UseHttpsRedirection`) before testing this against it.

## Verified

```
npm run build     # ✓ built clean, 0 errors
```

Because NuGet is blocked in my sandbox (so I couldn't run the real ScoramAPI locally to test against), I
built a **mock backend** (`mock-backend/server.js`, not shipped in this zip — just used for my own testing)
that returns the exact same JSON shapes as `AuthController`/`QuestionsController`, pointed the built app at
it, and drove it with Playwright:

- **Register** — filled the real form, submitted, confirmed a real `POST /api/auth/register` fired, a
  GUID `userId` came back, the JWT landed in `localStorage`, and the UI updated to show the real name/email
  in both the sidebar and top bar
- **Search Questions** — confirmed real `GET /api/questions` calls with filters (subject dropdown) return
  and render live results, with correct counts
- **Mobile** — bottom-nav → Search and → Profile (showing the real Login form when logged out) both work
- **Error handling** — pointed the built app at a nonexistent port and confirmed the "Couldn't reach the
  Scoram API" message renders with a helpful hint, rather than a blank page or unhandled exception
- **Discussions** — loaded the real feed (`GET /api/discussions`), clicked Upvote and confirmed the count
  genuinely went `41 → 42` via a real `POST /api/comments/{id}/upvote` call (not just an optimistic UI
  update that silently fails), then opened the reply box, submitted a reply, and confirmed a real
  `POST /api/comments/{id}/replies` fired
- **Mock Test attempt flow, end to end**: browsed the real test list (`GET /api/mocktests`) → started a
  test → answered all questions → submitted (`POST /api/mocktests/{id}/attempts`) → landed on a result
  screen with real score/accuracy/correct/wrong stats and per-question explanations → went back to the
  test list and confirmed the just-completed attempt now appears under "My Recent Attempts"
  (`GET /api/mocktests/attempts/mine`) — checked on both desktop and mobile viewports

This means the integration code paths themselves (fetch calls, auth flow, error handling, state
persistence) are genuinely exercised — what I can't verify from this sandbox is your actual SQL Server data
coming back through the real ASP.NET Core pipeline, since I have no network access to NuGet or a SQL Server
instance here. Please run it against the real backend and let me know if anything doesn't match.

## Next steps

- Build the remaining sidebar destinations against real endpoints as the backend adds them (PYQ Bank,
  Group Chat, Quizzes, Leaderboard, Bookmarks, My Progress, Settings)
- Wire Solutions ("one answer, multiple methods") into the question detail view — the backend
  (`SolutionsController`) is already built, just not connected to a UI yet
- Add a "forgot password" flow once the backend supports it
- Consider a refresh-token flow before the JWT's 24h expiry becomes a real UX issue
- `TestAttempt`'s question-order randomization and the timer aren't persisted server-side, so a page
  refresh mid-attempt loses progress — worth a "resume attempt" feature once there's a real need for it
