# Scoram API — Backend (Phase 3: Solutions, Discussions & Mock Tests)

## New in this pass — Home page: real data instead of fake numbers, dead buttons wired up

Found while reviewing for "better student UX": several Home page sections were showing **fabricated**
numbers (a fake 7-day streak, fake view/comment counts) and several cards had **no `onClick` at all** --
tapping them did nothing. Both are now fixed.

- **`components/home/StreakXPCard.jsx` deleted entirely.** It showed a hardcoded "7 Day Streak, +120 XP"
  to every single student regardless of whether they'd ever answered a question -- actively misleading,
  and worse than just not having the feature yet. It comes back once Gamification is actually built.
- **`components/home/PopularExams.jsx`** now fetches real exams via `GET /api/exams` (question counts
  are real, sourced from Published papers) instead of three hardcoded exams with made-up counts. Tapping
  a card now navigates to Search Questions with that exam preset in Browse-by-Exam.
- **`components/home/TodaysChallenge.jsx`** now calls a new endpoint, `GET /api/questions/today`
  (deterministic daily pick from Published questions, same for every student, cached server-side so it
  isn't recomputed per request -- see below). Fake "12.5K views, 45 comments" are gone; the real
  `solutionCount` is kept. Tapping "View Question" expands the full `QuestionCard` in place rather than
  linking anywhere.
- **`components/home/QuickAccess.jsx`** cards now actually navigate (`PYQ Questions`/`Test` -> Search/
  Test pages). Also fixed a latent bug: the "Mock Tests" card's data key (`mock`) didn't match any real
  tab in `App.jsx` (only `test` was wired), so it silently fell through to a generic "Coming Soon" even
  though the real Tests page existed. `Quizzes` and `My Progress` are left going to Coming Soon
  deliberately -- those genuinely don't exist yet (Exam Utility / Gamification), so that's the honest
  state to show.
- The sidebar's **"PYQ Bank"** item, previously always "Coming Soon", now also routes to Search Questions
  -- Browse-by-Exam already **is** a PYQ Bank experience, so there was no reason to keep it a dead end.

### Backend: `GET /api/questions/today`

New endpoint in `QuestionsController`. Picks one Published question deterministically (a seed derived
from today's UTC date, modulo the count of Published questions) rather than randomly -- same question for
every student all day, no per-user state to track, and trivially safe to cache. Response is cached
in-memory for 6 hours (`IMemoryCache`, registered in `Program.cs`) so the (potentially large) query for
all Published question IDs doesn't re-run on every single home-page load across however many students are
online.



### Registration & Login

`User` now has a required, unique **Username** (Instagram-style: lowercase letters/numbers/dots/underscores,
enforced both client-side live and server-side via regex). `PhoneNumber` is now **required and unique**
too (was optional before). `POST /api/auth/register` returns 409 with a specific message for whichever of
Username/Email/Phone is already taken.

**Login now accepts either an email or a username** — `LoginDto.Identifier` replaces the old `Email`
field; `AuthController.Login` tries an exact match on `Email` first, then a case-insensitive match on
`Username`.

New: `GET /api/auth/check-username?username=xxx` — public, no auth, used for the live "is this available"
indicator while someone's typing during registration. Checks format (length, allowed characters) before
even hitting the database.

**Confirm Password is frontend-only** (standard practice) — the backend never sees it, only `Password`.

### Meilisearch instant search

`Services/InstantSearchService.cs` talks to Meilisearch over **plain `HttpClient` against its REST API**,
not the official NuGet SDK — this was a deliberate choice: the REST API surface is stable and
well-documented, whereas I couldn't verify the exact NuGet package's class/method names against a real
build in this environment, so a raw HTTP client meaningfully de-risked this integration. Config lives under
`Meilisearch:Url` / `Meilisearch:ApiKey` in `appsettings.json` (defaults to `http://localhost:7700`, no
key — fine for local dev, but set a real master key before any real deployment).

**You need Meilisearch actually running** for this to work — it's a separate lightweight process/binary,
not something this API starts for you. See https://www.meilisearch.com/docs/learn/self_hosted/getting_started_with_self_hosted_meilisearch
for the quickest way to run it locally (a single downloaded binary works fine for dev).

**Sync points** — only three, all in `PapersController`, because editing only ever happens on Draft
papers (never Published, per the earlier Publish/Unpublish rules), so there's no separate per-question
sync needed:
- `Publish` (and `Submit`, when it publishes directly) → indexes every question in that paper
- `Unpublish` / `Delete` → removes that paper's questions from the index

All three are wrapped in try/catch — **a Meilisearch outage never fails the underlying Publish/Unpublish/
Delete database operation**, it just logs an error. The index can drift from the DB if Meilisearch was down
during a sync; that's what the reindex endpoint below is for.

New: `POST /api/admin/papers/reindex-search` (Super Admin only) — wipes and rebuilds the whole index from
every currently-Published paper. Run this once after first deploying this feature (existing Published
papers were never indexed before now), and any time you suspect drift.

New: `GET /api/questions/instant-search?q=` (public, student-facing) — the actual search bar endpoint.
Returns lightweight `QuestionSearchDocument` records (enough to render a result card without a DB round
trip) rather than full `QuestionResponseDto`s.

### Browse-by-Exam (cascading filters, Published only)

New public controller, `Controllers/StudentPapersController.cs` (`api/papers`) — separate from the
admin-only `PapersController` since these need to be reachable without an admin token:
- `GET /years?examId=` → distinct years with at least one Published paper
- `GET /shifts?examId=&year=` → distinct shifts
- `GET /languages?examId=&year=&shift=` → distinct languages
- `GET /sets?examId=&year=&shift=&language=` → matching Published papers (usually one; more than one
  means multiple Sets/PaperCodes exist for that combination)

The final step — actually viewing a specific paper's questions in order — reuses the existing
`GET /api/questions?paperId=` (already ordered by `QuestionNumber` when a `PaperId` filter is given, and
already restricted to Published papers, from the Paper-system pass).

### ⚠️ Migration needed

```bash
cd ScoramAPI
dotnet ef migrations add StudentUsernameAndRequiredFields
dotnet ef database update
```

This one has a real data consideration if you already have registered students: `Username` and
`PhoneNumber` are now `[Required]`, but existing rows won't have a `Username` at all and may have a null
`PhoneNumber`. If you have real (not just test) student accounts already, you'll want a one-off script to
backfill a placeholder username (e.g. derived from email) and a placeholder phone before this migration
can succeed with `NOT NULL` constraints — ask if you need help with that. If this is still all test data,
the simplest path is the same one from before: drop and recreate.

### What's not built yet

- No "reindex" trigger in the admin UI — the endpoint exists (`POST /api/admin/papers/reindex-search`),
  but there's no button for it yet; call it via Swagger/curl for now after first deploying this pass
- No password-reset flow for students (same gap noted for admins earlier)



`Program.cs` now registers a global `JsonStringEnumConverter`. Every enum-typed field across the whole
API — `DifficultyLevel`, `OptionLetter`, `AdminTaskStatus`, `AdminRole`, `MockTestType`, `SolutionType`,
etc. — now sends/accepts strings (`"Medium"`, `"A"`, `"Pending"`) instead of raw integers (`1`, `0`, `0`)
in JSON. This was discovered while wiring up the admin PYQ upload UI: `POST /api/questions` expects a
`DifficultyLevel` and `OptionLetter` in the body, and without this converter those only accept integers,
which is fragile and unreadable in Swagger for anyone testing by hand.

**Nothing existing broke from this** — response DTOs that output enums were already manually calling
`.ToString()` in the controllers (so their JSON was already string-shaped), and no frontend code existed
yet that sent one of the enum-typed *input* fields as an integer. If you're calling the API directly
(Postman, curl, a script) and were previously sending integers for any enum field, switch to the string
name.



Two real gaps closed in this pass: there was no way to log in as an admin at all (every "admin-only"
endpoint just required *any* logged-in student account, or defaulted to "the first row in `Admins`"), and
Section 12 (Admin Task Management) had a database table but no endpoints.

### Admin login — `Controllers/AdminAuthController.cs`

There's intentionally **no public admin self-registration** — per SRS Section 3, only a Super Admin
creates other admin accounts. To make a fresh database usable immediately, one Super Admin is seeded via
`ScoramDbContext`'s `HasData()`:

```
Email:    superadmin@scoram.com
Password: SuperAdmin@123
```

**Change this password before any real deployment** — either log in and use a future "change password"
endpoint once one exists, or replace the pre-computed BCrypt hash in `ScoramDbContext.cs` with your own
before running migrations on a production database.

| Method | Route | Auth | What it does |
|---|---|---|---|
| POST | `/api/admin/auth/login` | — | Admin login, returns a JWT with a `Role` claim of `Admin` or `SuperAdmin` |
| GET | `/api/admin/admins` | SuperAdmin | List all admin accounts |
| POST | `/api/admin/admins` | SuperAdmin | Create a new Admin (or SuperAdmin) account |
| PATCH | `/api/admin/admins/{id}/status` | SuperAdmin | Activate/deactivate an admin account (can't deactivate yourself) |

An admin JWT looks just like a student JWT (same `sub`/id claim shape) except its `Role` claim is `Admin`
or `SuperAdmin` instead of `Student` — this is what lets `[Authorize(Roles = "Student")]` and
`[Authorize(Roles = "Admin,SuperAdmin")]` tell the two kinds of account apart on every endpoint below.

### Admin Task Management — `Controllers/AdminTasksController.cs` (SRS Section 12)

Added an `AssignedByAdminId` column to `AdminTask` (nullable, additive) so a task records which Super
Admin assigned it, not just who it's assigned to.

| Method | Route | Auth | What it does |
|---|---|---|---|
| POST | `/api/admin/tasks` | SuperAdmin | Assign a new task to an admin |
| GET | `/api/admin/tasks` | SuperAdmin | View all tasks, optional `?status=` / `?assignedTo=` filters |
| GET | `/api/admin/tasks/mine` | Admin, SuperAdmin | An admin's own assigned tasks, soonest deadline first |
| PATCH | `/api/admin/tasks/{id}/status` | Admin (own tasks only), SuperAdmin (any) | Move a task through Pending → InProgress → Completed |
| PATCH | `/api/admin/tasks/{id}` | SuperAdmin | Edit title/description/deadline, or reassign to a different admin |
| DELETE | `/api/admin/tasks/{id}` | SuperAdmin | Remove a task |

### Existing endpoints locked down properly

Every endpoint previously marked "⚠️ should be admin-only, but no admin auth exists yet" now has a real
`[Authorize(Roles = "Admin,SuperAdmin")]` check, and endpoints that should only ever be called by a student
now have `[Authorize(Roles = "Student")]` instead of a bare `[Authorize]` (an admin token was never
rejected by those before — it would have failed with an FK error deep inside the handler instead of a
clean 403 up front):

- `SolutionsController.Verify`, `SolutionsController.MarkEasiest` — now Admin-only
- `SolutionsController.Create`, `SolutionsController.Upvote` — now Student-only
- `DiscussionsController.Pin` — now Admin-only
- `DiscussionsController.Create`, `.Reply`, `.Upvote` — now Student-only
- `MockTestsController.Create` — now Admin-only, and resolves the real admin id from the JWT instead of
  defaulting to the first row in `Admins`
- `MockTestsController.SubmitAttempt`, `.MyAttempts`, `.GetAttempt` — now Student-only
- `QuestionsController.Create` — now Admin-only, same real-admin-id fix as above

### ⚠️ Additive migration needed

```bash
cd ScoramAPI
dotnet ef migrations add AddAdminAuthAndTasks
dotnet ef database update
```

This is purely additive (one new nullable column on `AdminTask`, one seeded Admin row) — no existing data
is affected.

## New in this pass — Group Chat (one room per exam, mentions, polls, notices, moderation)

The biggest single feature so far. A working group chat, real-time via SignalR, with student
participation and full admin moderation.

### Data model

New entities in `Models/ChatModels.cs`:
- **`ChatRoom`** — now requires an `ExamId` (unique — one room per exam) instead of being a free-standing
  named room. **Auto-created** whenever an exam is created (`ExamsController.Create`), and there's a
  **backfill endpoint** (`POST /api/admin/chat/sync-rooms`, Super Admin only) for exams that already
  existed before this feature. The old fixed seed rooms ("SSC CGL", "Daily Doubt Room", etc.) are gone —
  they didn't correspond to real exams and predate this design.
- **`ChatRoomMembership`** — durable, explicit membership (joining is optional). `IsBanned` is a
  **permanent** removal — a banned student can't rejoin themselves; only an admin re-adding them lifts it
  (there's no "unban" endpoint by design — re-adding is `POST /api/chat/rooms/{id}/join` after asking the
  admin to reconsider, not automatic).
- **`ChatMessage`** — `UserId` is now nullable, with a new nullable `SenderAdminId` alongside it: exactly
  one is set (student message vs. admin Notice/Poll). `MessageType` distinguishes Text/Image/Document/
  Poll/Notice.
- **`ChatMessageMention`**, **`ChatReport`**, **`ChatPoll`/`ChatPollOption`/`ChatPollVote`**,
  **`BannedWord`** — all new, see the model file for details.

### Real-time architecture: REST for mutations, SignalR purely for push

`Hubs/ChatHub.cs` was rewritten to **not** handle sending messages anymore — file uploads, banned-word
filtering, and mention parsing all need transactional DB work that doesn't fit a Hub method well. Instead:
- Sending goes through `POST /api/chat/rooms/{id}/messages` (multipart, optional `Attachment`), which
  does all the validation/parsing, saves to the DB, then pushes the result out via an injected
  `IHubContext<ChatHub>` — to the room's `room-{roomId}` SignalR group, and separately to each mentioned
  user's personal `user-{userId}` group.
- The Hub's only remaining jobs: on connect, join the caller's personal group plus every room they're an
  active (non-banned) member of; and two callable methods, `JoinRoomGroup(roomId)` / `LeaveRoomGroup(roomId)`,
  which the frontend calls right after a REST join/leave succeeds so the *live* connection's group
  membership updates without needing a reconnect.

**Only @mentions are real-time-notified**, per the earlier product decision — not every message. The
frontend's `ChatConnectionContext` is wired at the app root specifically so a mention notification can
arrive even while the student is on a completely different page, not just while a specific room's chat is
open.

### Word restriction

`POST /api/chat/rooms/{id}/messages` checks the message text against every row in `BannedWords`
(case-insensitive substring match) before saving — a match rejects the whole message with a 400, nothing
is persisted or broadcast. Managed via `ManageBannedWords` permission
(`GET/POST/DELETE /api/admin/chat/banned-words`).

### Granular admin permissions (6 new `AdminPermission` values)

Deliberately **not** one big "ModerateChat" flag — `RemoveGroupMembers`, `CreatePolls`, `PostNotices`,
`ToggleChatLock`, `HandleChatReports`, `ManageBannedWords` are independent grants, so e.g. an admin trusted
to post exam notices doesn't automatically also get report-handling or member-removal access. Existing
permission-grant UI (Manage Admins → Permissions) picks these up automatically since the permission list
there is driven off the enum.

### Endpoints

**Student** (`Controllers/ChatController.cs`, `api/chat`, all `[Authorize(Roles = "Student")]`):
`GET /rooms`, `POST /rooms/{id}/join`, `POST /rooms/{id}/leave`, `GET /rooms/{id}/messages` (paginated,
`?before=` cursor), `POST /rooms/{id}/messages`, `DELETE /messages/{id}` (own messages only),
`POST /messages/{id}/report`, `GET /rooms/{id}/mentionable-users?q=` (@ autocomplete, room members only),
`GET /mentions`, `PATCH /mentions/{id}/read`, `PATCH /mentions/read-all`, `POST /polls/{id}/vote`.

**Admin** (`Controllers/AdminChatController.cs`, `api/admin/chat`): `GET /rooms` (unrestricted view,
matches the Papers pattern), `PATCH /rooms/{id}/lock` (ToggleChatLock), `GET /rooms/{id}/members`
(unrestricted view), `DELETE /rooms/{id}/members/{userId}` (RemoveGroupMembers), `POST /rooms/{id}/notices`
(PostNotices), `POST /rooms/{id}/polls` + `PATCH /polls/{id}/close` (CreatePolls),
`GET /reports` + `PATCH /reports/{id}/resolve` (HandleChatReports),
`GET/POST/DELETE /banned-words` (ManageBannedWords), `POST /sync-rooms` (Super Admin only).

### `FileStorageService` extended for chat attachments

Added `SaveChatAttachmentAsync` (images *or* documents — PDF/Word/Excel/PowerPoint, 15 MB cap vs. 5 MB
for images) alongside the existing `SaveImageAsync`, both now backed by a shared private `SaveFileAsync`
so the validation/GUID-naming logic isn't duplicated a third time.

### ⚠️ Migration needed

```bash
cd ScoramAPI
dotnet ef migrations add GroupChat
dotnet ef database update
```

This one **removes the old seeded ChatRooms** (they had no `ExamId` to satisfy the new required/unique
column) and adds several new tables. If you have real chat history you care about under the old rooms,
say so before running this — otherwise this follows the same "test data, safe to reset" pattern as
previous passes. After migrating, call `POST /api/admin/chat/sync-rooms` once (Super Admin) to create
rooms for any exam that predates this feature.

### What's not built yet

- No typing indicators / read receipts / online-presence UI
- No pagination on the admin reports queue (fine at expected volume; would need it at real scale)
- No "mentions inbox" page on the student side yet — the backend (`GET /api/chat/mentions`) is ready,
  but there's no dedicated UI beyond the toast the moment it's received live
- Reports can be duplicated by the same student against the same message (not deduplicated) -- treated as
  a severity signal for now rather than a bug worth guarding against yet



This is the big one: PYQ uploads are no longer flat Question rows. There's now a proper **Paper**
concept, admins have **granular per-action permissions** instead of just Admin/SuperAdmin, and papers go
through a **review pipeline** before students can see them.

### Paper = Exam + Year + Shift + Language + PaperCode

New entity, `Models/Paper.cs`. A Question now belongs to a Paper (`Question.PaperId`) instead of
carrying its own exam/year/shift/language. This is what makes duplicate-paper detection, "delete this
whole paper", and "list all uploaded papers" possible without fragile GROUP BY queries over loose fields.

**Uniqueness**: Exam+Year+Shift+Language+PaperCode together. PaperCode is optional — leave it blank for
the normal case (one paper per shift); set it when the same shift has multiple question Sets (Set A/B/C).
Same Exam+Year+Shift in two different languages are two separate papers (Hindi and English versions don't
collide). **Only Hindi and English are supported languages right now** — `Enums.PaperLanguage` — adding a
third is a one-line enum addition plus a migration, not a redesign.

`Controllers/PapersController.cs` (`api/admin/papers`) owns the full lifecycle:
- `POST /` — create, or **409 + the existing paper** if this Exam+Year+Shift+Language+PaperCode already
  exists (frontend should treat a 409 here as "resume this paper", not an error)
- `GET /` — the "Uploaded Papers" list, paginated, filters: `?status=&examId=&mine=true`
- `GET /pending` — the review queue (Publish permission required)
- `GET /{id}` — paper detail + its questions in original paper order (by `QuestionNumber`)
- `PATCH /{id}/submit` — "Done" in the wizard. Publishes immediately if the admin has Publish permission,
  otherwise queues it as PendingReview
- `PATCH /{id}/publish`, `/reject` (with a required reason), `/unpublish` — all Publish-permission-only
- `DELETE /{id}` — Delete-permission-only, cascades to every Question under it (and best-effort cleans up
  their images from disk) — this is how a paper gets "deleted so it can be re-uploaded fresh"

**Status lifecycle**: `Draft` (being built) → `PendingReview` or `Published` (on submit, depending on the
submitter's Publish permission) → `Published` (approved) or back to `Draft` with `RejectionReason` set
(rejected). A **Published paper can't be edited directly** — `PATCH /unpublish` first, which sends it back
to `PendingReview`.

### Granular per-admin permissions (not just Admin/SuperAdmin anymore)

`Enums.AdminPermission`: `UploadPaper`, `EditPaper`, `DeletePaper`, `PublishPaper`, `Audit`. Stored as
`AdminPermissionGrant` rows (`Models/AdminPermissionGrant.cs`) rather than fixed boolean columns on
`Admin`, specifically so adding a new capability later is one enum value, not a schema migration.

**A Super Admin implicitly has every permission** regardless of grants — enforced in code
(`Services/AdminPermissionService.cs`), not stored. New Admin accounts start with **zero permissions**;
a Super Admin grants them explicitly.

Deliberately **not baked into the JWT**. Permissions are checked against the database on every relevant
request instead of being embedded as token claims — the tradeoff is one extra indexed lookup per
permission-gated admin action (negligible at ~100 admins) in exchange for a real correctness property: if
a Super Admin revokes a permission, it's gone *immediately*, not "whenever their 24-hour token happens to
expire." This matters more than it might seem given the app's security requirements.

Managed via `AdminAuthController`:
- `GET /api/admin/admins/{id}/permissions` / `PUT /api/admin/admins/{id}/permissions` (Super Admin only,
  replace-all semantics — send the complete permission list, not incremental add/remove)
- `AdminResponseDto` (used by `GET /api/admin/admins`) now includes each admin's current `permissions`

### Question images (diagrams/maps/pattern-completion questions)

Six optional image slots per question — `Models/QuestionModels.cs`: `QuestionImageUrl`,
`OptionAImageUrl`..`OptionDImageUrl`, `ExplanationImageUrl`. Text stays required for all of these; images
are additive, not a replacement.

`POST /api/questions` is now **multipart/form-data** (it takes files now, not just JSON) and requires
`PaperId` + `QuestionNumber` instead of the old flat exam/year/shift fields — see `QuestionCreateDto`.
`PATCH /api/questions/{id}` (new) and `DELETE /api/questions/{id}` (new) round out real editing — before
this pass there was no way to fix or remove a question after uploading it. Both, like Create, only work
while the question's paper is in `Draft` (see the Published-paper-can't-be-edited-directly rule above).

Image handling is centralized in `Services/FileStorageService.cs` (extension/size validation, GUID
filenames, save/delete) — reused by both `QuestionsController` (question images) and `ExamsController`
(exam logos) rather than duplicated per-controller.

### Student-facing visibility: only Published papers

`QuestionsController.Search` and `.GetById` (the endpoints the student app actually calls) now filter out
any question belonging to a Draft or PendingReview paper. A direct/shared link to a question in an
unpublished paper 404s for students, same as if it didn't exist yet. Legacy questions with no `PaperId`
(created before this pass) are treated as always-visible, same as before.

### Backward compatibility: legacy (pre-Paper) questions

`Question.ExamId/ExamName/Year/Shift/Language` all became **nullable** rather than being removed —
questions created before this pass keep working exactly as they did. New uploads go through `PaperId`
exclusively and leave those legacy fields null. Response DTOs (`QuestionResponseDto`) transparently prefer
the Paper-sourced values when `PaperId` is set and fall back to the legacy flat fields otherwise, so
nothing reading question data needs to know which "generation" a given question is from.

### Scale & security hardening in this pass

- **Rate limiting** on both login endpoints (`AuthController.Login`, `AdminAuthController.Login`) — 5
  attempts/minute per IP, via ASP.NET Core's built-in rate limiter (`Program.cs`). Basic but real
  brute-force protection given this app is meant to handle real user volume.
- **New indexes**: `Paper(ExamId, Year, Shift, Language)` for fast duplicate-checks and listing, unique
  `Question(PaperId, QuestionNumber)` (catches accidental double-entry — SQL Server allows multiple NULLs
  through a composite unique index, so legacy questions are unaffected), unique
  `AdminPermissionGrant(AdminId, Permission)`.
- **Known, honest limitation**: none of this makes SQL Server Express or local-disk image storage
  suitable for 5 lakh+ concurrent students on their own — those are hosting/infrastructure decisions
  (a production-grade DB, and eventually cloud storage/CDN for images once running behind more than one
  API server instance), not something fixable by application code alone. Flagging this now so it isn't a
  surprise later.

### ⚠️ Migration needed (additive, but larger than previous passes)

```bash
cd ScoramAPI
dotnet ef migrations add AddPapersPermissionsAndQuestionImages
dotnet ef database update
```

Adds: `Papers` table, `AdminPermissionGrants` table, nullable `PaperId`/`QuestionNumber` + six nullable
image URL columns on `Questions`, makes `Question.ExamName`/`Year`/`Shift`/`Language` nullable. No existing
data is dropped or altered destructively.

**Before running this**, please re-read the note from last time: copy only `Controllers/`, `Models/`,
`DTOs/`, `Data/`, `Program.cs`, `Services/`, `Enums/`, `Extensions/` from the zip into your project —
**never overwrite your local `Migrations/` folder**, since it doesn't exist in this sandbox and copying it
over will desync your migration history from your actual database again (same issue as the
`AssignedByAdminId` incident).

### What's not built yet for this system

- No admin UI for any of this yet (Paper wizard rewrite, permission checkboxes, review queue, "Uploaded
  Papers" list) — this pass is backend-only; the admin panel UI is the natural next step
- No "Audit" feature behind the `Audit` permission yet — the permission exists so it can be assigned now,
  but there's no activity log to actually view yet
- The cascading Exam→Year→Shift→Subject student browse page (discussed but not started per your
  instruction) still needs its own pass once this backend is confirmed working



This is the flow the admin actually walks through to upload previous-year questions, and it needed a
real `Exam` entity that didn't exist before -- `Question.ExamName` was just a free-typed string with no
logo, no reusable picklist, and no language.

**Step 1 — choose or create the exam** (`Controllers/ExamsController.cs`)
- `GET /api/exams` — the picker list (Id, Name, LogoUrl, QuestionCount). Public, so it also works as a
  "browse by exam" list for students, not just the admin picker.
- If the exam the admin needs isn't in that list yet, `+ New Exam`: `POST /api/admin/exams`
  (Admin only, `multipart/form-data` with `Name` + optional `Logo` file). Duplicate names (case-insensitive)
  are rejected. The logo is validated (png/jpg/jpeg/webp/svg only, 2 MB max), saved under
  `wwwroot/uploads/exam-logos/{guid}.ext` (never the original filename, to avoid path traversal /
  collisions), and served back at `/uploads/exam-logos/{guid}.ext` via `app.UseStaticFiles()`.

**Step 2 — choose the language** — no separate endpoint; `POST /api/questions` (below) just takes a
free-text `Language` field (Hindi, English, Kannada, ...) alongside the question. Kept as a string rather
than a fixed enum since the list of exam languages isn't closed.

**Step 3 — upload the question** — `POST /api/questions` (already existed, from Phase 1) now takes
`ExamId` (the exam chosen/created in Step 1) instead of a free-typed `ExamName`, plus `Language`. Subject,
Topic, Year, Shift, the four options, correct option, explanation, and source reference are unchanged.

### Why `Question.ExamName` still exists as a column

Rather than replacing `ExamName` with `ExamId` everywhere (which would've meant touching every existing
search filter, DTO, and any frontend code reading `examName`), `ExamId` was added as a new **nullable**
column alongside it. `POST /api/questions` now sets both — `ExamId` from the chosen exam, and `ExamName`
copied from `Exam.Name` — so every existing reader that filters or displays `ExamName` keeps working
completely unchanged, while new code can join on `ExamId` for the logo, exact identity, etc. Older
questions created before this pass simply have `ExamId = null` and keep their original `ExamName`.

### ⚠️ Additive migration needed

```bash
cd ScoramAPI
dotnet ef migrations add AddExamsAndQuestionLanguage
dotnet ef database update
```

Purely additive: one new `Exams` table, two new nullable columns on `Question` (`ExamId`, `Language`).
No existing data changes or is at risk.

### What's still not built for this PYQ upload flow

- No bulk/CSV upload — one question at a time via `POST /api/questions`, same as before this pass
- No "edit exam" (rename, replace logo) or "delete exam" endpoints yet — only create + list
- No endpoint to backfill `ExamId` onto pre-existing questions that only have `ExamName` — if that matters
  for your data, it's a one-off script matching each distinct `ExamName` to an `Exam` row

### What's still not built for Admin Auth & Task Management

- No "forgot password" / change-password flow for admins yet (the seeded Super Admin's password should be
  rotated by editing the seed hash directly until this exists)
- No admin-facing frontend yet — `AdminAuthController`, `AdminTasksController`, and `ExamsController` are
  API-only in this pass; `ScoramWeb` is still the student-facing app only

---

ASP.NET Core 8 Web API backend for the Scoram competitive exam prep platform, built from the SRS (v2).
This slice gives you a **working, runnable foundation**: full database schema (all 27 tables across
core + gamification + exam-utility modules), JWT auth (student register/login), and a first working module
— **Previous Year Questions** with advanced search/filter — end to end.

## What changed in this pass

Every `Id` and foreign key across all 27 tables changed from `int` (sequential, auto-increment) to
**`Guid`** (`Guid.NewGuid()` default). Sequential integer IDs let anyone enumerate your data by guessing
`/api/questions/1`, `/2`, `/3`, ...; a GUID like `8f14e45f-ceea-467e-add1-000000000001` can't be guessed.
This touched:
- Every model's `Id` and `*Id` foreign key properties (`Models/`)
- `ScoramDbContext`'s seed data — `HasData()` requires fixed values (can't call `Guid.NewGuid()` at
  model-build time), so the seeded badges and chat rooms now use fixed, arbitrary GUID literals instead of
  `1, 2, 3...`
- `QuestionsController.GetById` — route constraint changed from `{id:int}` to `{id:guid}`
- `QuestionResponseDto.Id` and `AuthResponseDto.UserId` — both now `Guid`

Also added: `Properties/launchSettings.json` (missing from the first zip), with **fixed ports**
(`http://localhost:5192` / `https://localhost:7192`) so the frontend's default API URL lines up with
where this actually runs — see `ScoramWeb/.env.example`.

## ⚠️ BREAKING CHANGE — you'll need a fresh database

You already have a working `ScoramDB` with the old `int`-based schema (from Phase 1). Changing every
table's primary key type from `int` to `Guid` isn't something a normal migration can do cleanly across 27
interlinked tables (it would mean rewriting every primary key, every foreign key, and every relationship
at once). Since you're still early — no real user data to preserve yet — the simplest and safest path is
to **start the database fresh**:

```bash
cd ScoramAPI

# 1. Delete your old migration files (they describe the OLD int-based schema)
#    Delete the entire Migrations/ folder

# 2. Drop the old database (SSMS: right-click ScoramDB -> Delete,
#    or via sqlcmd: DROP DATABASE ScoramDB;)

# 3. Generate a fresh migration from the new Guid-based models
dotnet ef migrations add InitialCreate

# 4. Create the new database with the Guid schema
dotnet ef database update

# 5. Run it
dotnet run
```

If you do have test data you want to keep, let me know what's in it and I can write a proper data-preserving
migration instead — but for an empty/test database, dropping and recreating is far less error-prone.



## Bugfix — CORS blocked by middleware order (found during live testing)

While connecting a real frontend to a real running backend for the first time, registration failed with:
`Access to fetch ... has been blocked by CORS policy: ... No 'Access-Control-Allow-Origin' header is present`.

Root cause: `Program.cs` called `app.UseHttpsRedirection()` **before** `app.UseCors(...)`. When running the
`http`-only dev profile, the redirect middleware intercepted the browser's CORS preflight (`OPTIONS`)
request before it ever reached the CORS middleware, so the response never got CORS headers attached — a
redirect response can't carry them. This wasn't caught by the compiler check earlier in this project
because it's a runtime request-pipeline ordering issue, not a type error; it only surfaces when a real
HTTP request actually flows through the middleware pipeline, which requires a live server + browser.

**Fix**: swapped the order so CORS runs first:
```csharp
app.UseCors("FrontendDev");     // was after UseHttpsRedirection — now first
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
```

If you pulled an earlier copy of this project, apply this one swap in `Program.cs` and restart — no
migration or rebuild-from-scratch needed, this is a request-pipeline change only.

- `Models/` — EF Core entity classes for every table in SRS Section 15 (core, gamification, exam-utility)
- `Data/ScoramDbContext.cs` — DbContext with relationships, unique constraints, and seed data (badges, chat rooms)
- `Controllers/AuthController.cs` — student register & login (JWT), with referral-code handling on signup
- `Controllers/QuestionsController.cs` — advanced search/filter, question detail, admin question upload
- `Hubs/ChatHub.cs` — SignalR hub stub for the real-time Group Chat module (Section 8)
- `Services/TokenService.cs` — JWT generation
- `Properties/launchSettings.json` — fixed dev ports (5192 http / 7192 https)
- `appsettings.json` — wired to the SQL Server Express connection string you provided

## New in this pass — three modules, end to end

### 📎 Additive migration needed (safe, no data loss)

Adding these modules needed one new table (`StudentAnswers`, explained below). Unlike the earlier Guid
migration, this one is **purely additive** — no existing column types change, so a normal incremental
migration works and your existing data is untouched:

```bash
cd ScoramAPI
dotnet ef migrations add AddStudentAnswers
dotnet ef database update
```

### Solutions (`Controllers/SolutionsController.cs`) — SRS Section 5

| Method | Route | Auth | What it does |
|---|---|---|---|
| GET | `/api/questions/{questionId}/solutions` | — | List all solutions for a question (easiest-method first, then by upvotes) |
| POST | `/api/questions/{questionId}/solutions` | student | Submit a new solution |
| POST | `/api/solutions/{id}/upvote` | student | Upvote a solution |
| PATCH | `/api/solutions/{id}/verify` | ⚠️ see below | Mark a solution admin-verified |
| PATCH | `/api/solutions/{id}/mark-easiest` | ⚠️ see below | Mark a solution as the "easiest method" for its question |

### Discussions (`Controllers/DiscussionsController.cs`) — SRS Section 6

| Method | Route | Auth | What it does |
|---|---|---|---|
| GET | `/api/discussions` | — | Global feed of top-voted comments across all questions (powers a "Top Discussions" list) |
| GET | `/api/questions/{questionId}/comments` | — | Comments for one question, with one level of nested replies |
| POST | `/api/questions/{questionId}/comments` | student | Add a top-level comment |
| POST | `/api/comments/{commentId}/replies` | student | Reply to a comment |
| POST | `/api/comments/{commentId}/upvote` | student | Upvote a comment |
| PATCH | `/api/comments/{commentId}/pin` | ⚠️ see below | Pin/unpin a comment |

### Mock Tests (`Controllers/MockTestsController.cs`) — SRS Section 9

| Method | Route | Auth | What it does |
|---|---|---|---|
| GET | `/api/mocktests` | — | List available tests, filter by exam/type |
| GET | `/api/mocktests/{id}` | — | Test detail **without** the answer key |
| POST | `/api/mocktests/{id}/attempts` | student | Submit answers — auto-graded, returns full breakdown with the answer key now revealed |
| GET | `/api/mocktests/attempts/mine` | student | This student's own attempt history (powers "Recent Tests") |
| GET | `/api/mocktests/attempts/{attemptId}` | student, own attempts only | Full per-question breakdown of one past attempt |
| POST | `/api/mocktests` | ⚠️ see below | Create a test from a list of question ids |

**New table**: `StudentAnswers` (`Models/StudentAnswer.cs`) — the original schema only stored aggregate
scores (`StudentTestResults`: correct/wrong/skipped counts) with nowhere to persist *which* option a
student picked for *which* question. Without it, "view detailed solution after submission" could only work
immediately after submitting, not when revisiting a past attempt later — which the SRS explicitly asks for
("View previous attempts and scores"). This table is what makes `GET /api/mocktests/attempts/{attemptId}`
possible.

**Marking scheme**: `+1` per correct answer, `-NegativeMarkingRatio` per wrong answer, `0` for skipped —
the SRS doesn't specify exact marks, this matches typical SSC/Railway exam conventions and fits the
`NegativeMarkingRatio` field already in the schema.

### ⚠️ Known limitations (be aware before relying on these)

- **Admin role is now enforced** (was a known limitation as of this file's original Phase 3 pass): `verify`,
  `mark-easiest`, `pin`, `POST /api/mocktests`, and `POST /api/questions` are now genuinely
  `[Authorize(Roles = "Admin,SuperAdmin")]` — see the "New in this pass — Admin Auth & Task Management"
  section at the top of this file.
- **No duplicate-upvote prevention**: upvote endpoints just increment a counter — a student can upvote the
  same solution/comment repeatedly. Preventing that needs a join table (`UserId` + `SolutionId`/`CommentId`,
  unique) that isn't modeled yet.
- **Random question order isn't persisted per attempt**: `IsRandomOrder` reshuffles on every `GET`, so
  reloading the test page gives a different order. Fine for now; a real implementation would fix the order
  once per attempt.
- **Option shuffling (`IsShuffleOptions`) isn't implemented** — correctly remapping which letter is "correct"
  after shuffling needs careful handling to avoid leaking the answer; deferred rather than rushed.
- One data point in `MyAttempts` (accuracy %) is deliberately computed in plain C# *after* fetching rows,
  not inside the database query itself — `Math.Round`/conditional arithmetic inside an EF Core `Select()`
  projection isn't something I could test against a real SQL Server from this sandbox, so I sidestepped the
  translation risk entirely rather than ship an untested query shape.



1. **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
2. **SQL Server Express** running as `localhost\SQLEXPRESS` (matches your connection string) with Windows Auth enabled
3. **EF Core CLI tools**: `dotnet tool install --global dotnet-ef`

## First-time setup

```bash
cd ScoramAPI
dotnet restore

# Generate the initial migration from the models
dotnet ef migrations add InitialCreate

# Create ScoramDB and all 27 tables on your SQLEXPRESS instance
dotnet ef database update

# Run the API
dotnet run
```

Swagger UI opens at `https://localhost:<port>/swagger` — use it to try `POST /api/auth/register`,
`POST /api/auth/login`, and `GET /api/questions`.

## ⚠️ What I actually verified (read this before you run it)

I installed the .NET 8 SDK in my sandbox and tried to run it end-to-end. Here's exactly what happened:

- **`Models/`, `Enums/`, `DTOs/`, `Extensions/*`** (excluding the JWT-dependent claims helper) — these only
  depend on the base class library, so I copied them into an isolated project and ran a **real
  `dotnet build`** against them each time I added to them. Result: **0 errors, 0 warnings** every pass,
  including after adding `SolutionDTOs`, `DiscussionDTOs`, `MockTestDTOs`, and the new `StudentAnswer` model
  in this phase.
- **`Data/`, `Controllers/`, `Program.cs`, `Services/`, `Hubs/`, `Extensions/ClaimsPrincipalExtensions.cs`**
  — these need EF Core, JWT, and Swashbuckle from NuGet, which `dotnet restore` can't reach from this
  sandbox (403 from `api.nuget.org` — not a restriction you'll have locally). I did a careful line-by-line
  audit of every new controller instead, checking property names against the models, route-template overlap
  between controllers, and nullable-reference handling. One thing I deliberately avoided rather than risk
  shipping untested: doing `Math.Round`/conditional arithmetic *inside* an EF Core `Select()` projection
  (in `MyAttempts`) — I moved that calculation to plain C# after materializing the rows instead, sidestepping
  any SQL-translation edge case I couldn't verify without a real database connection.
- Instead I did a careful manual line-by-line audit of those files — and it caught a real bug: my first draft
  of `ScoramAPI.csproj` had an explicit `PackageReference` for `Microsoft.AspNetCore.SignalR`, which doesn't
  need to be (and shouldn't be) a separate package — SignalR's server side ships inside the ASP.NET Core
  shared framework for Web SDK projects, so an explicit reference would likely have broken restore or caused
  a version conflict. **I've removed it** — the current zip has the fix.
- I also traced through every cascade-delete relationship by hand to rule out SQL Server's "multiple cascade
  paths" error (a common cause of migration failures in schemas this size) — e.g. `QuestionSolution`'s links
  back to both `User` and `Admin` are set to `Restrict`, as is the self-referencing `QuestionComment` reply
  chain and both sides of `Referral`.

Bottom line: the DB-schema-independent code is compiler-verified; the rest is carefully reviewed but not
machine-verified, purely because of this sandbox's network restrictions. Please run the steps below on your
machine and paste me any error output — I'll fix it immediately.

**Two more things before you run it:**
- **`Jwt:Key` in `appsettings.json`** is a placeholder — replace it with a long random secret (32+ chars)
  before running migrations/seeding real data. Don't commit the real key; move it to user-secrets or
  environment variables for anything beyond local dev.
- **Admin login is now built** — `POST /api/admin/auth/login` (see `AdminAuthController`), with a seeded
  Super Admin (`superadmin@scoram.com` / `SuperAdmin@123`) so there's a way in on a fresh database.
  `QuestionsController.Create` and `MockTestsController.Create` now resolve the real admin id from the JWT
  instead of the old "first row in `Admins`" placeholder.


## What's deliberately not in this slice yet

To keep this a reviewable, working set of changes rather than a wall of untested code, these modules are
modeled in the database (`Models/`, `Data/ScoramDbContext.cs`) but don't have controllers yet:

- Question reports (Section 7) — reporting a wrong/duplicate question
- Group chat persistence (the SignalR hub broadcasts live messages but doesn't save history yet)
- Gamification logic — awarding XP/streaks/badges on actions (Section 10; tables exist, the "when do we
  award XP" logic doesn't yet)
- Exam utility endpoints — Current Affairs, syllabus tracker, typing test, exam calendar (Section 11)

Admin authentication and Admin Task Management (Section 12) were the two gaps closed in this pass — see
the "New in this pass" section at the top of this file.

Tell me which of these to build next and I'll pick up right where this leaves off.
