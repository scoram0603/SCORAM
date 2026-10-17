# SCORAM Admin Panel — delivery notes

Scope: audit, reorganise, modernise and extend the existing admin panel **in place** (ScoramAPI, ScoramWeb, SCORAM_APP). Nothing was rebuilt; every pre-existing route, endpoint and screen is still there.

## 0. Read this first — verification status

| Area | Built | Compiled | Tested |
|---|---|---|---|
| ScoramWeb (React) | yes | **yes** — `vite build` passes | **yes** — 21 unit tests pass (`npm test`); `oxlint` has 0 errors (only warning styles that already existed) |
| ScoramAPI (C#) | yes | **NO** — the sandbox has no .NET SDK and no route to download one | **NO** |
| SCORAM_APP (Flutter) | yes | **NO** — no Flutter SDK | **NO** |

The C# and Dart were written to match the surrounding code and then re-read against the real model/enum/DbSet names (a script confirmed every web permission key matches the `AdminPermission` enum). They still need a first `dotnet build` / `flutter pub get && flutter analyze` on your machine. Expect, at worst, small compile fixes. **Do not deploy the API until it builds and the migration has been applied to a staging copy of the database.** The EF queries (grouped by admin-local date via `AddMinutes(offset).Date`, `COUNT(DISTINCT)` inside grouped projections) are standard EF Core 8 SQL Server translations but have not been run.

## 1. Feature inventory (before → after)

| Capability | Before | Now |
|---|---|---|
| Dashboard | UTC-day buckets, "Uploads storage: 0 MB" (never populated), fixed 7d/6m charts | Date-range picker, previous-period deltas, sparklines, tooltips with definitions, drill-downs, local-timezone buckets, honest "not tracked" cards; fake storage figure removed |
| Analytics | none | Tabbed page: Users, Content, Learning, Revenue (explicitly unavailable), Community, Operations |
| Navigation | flat groups | 9 collapsible, searchable, permission-aware groups with live badges; mobile drawer |
| Student management | none | Search/filter, detail drawer, suspend/reactivate (reason mandatory, audited, sessions killed), sessions, sign-out-everywhere |
| Reports | none | 7 CSV/Excel reports, permission-aware, audited, 50k row cap, formula-injection guard |
| Notifications | announce endpoint only | Compose UI, audience preview, confirm step, sent history with recipients/read rate |
| Support | basic feedback list | Tickets: priority, assignee, status, internal notes, reply-to-student (push/in-app), timeline, `ResolvedAt` |
| App config | none | Maintenance mode, feature flags, min/recommended app versions, release notes; full revision history + rollback; version impact preview |
| Force update | none | Server evaluates version; Flutter blocks / nags / shows maintenance; fails open |
| Sessions/devices | none | Student sessions + push devices (per student), all admin sessions (SuperAdmin), force logout |
| Roles | checkbox editor, 18 permissions | 22 permissions grouped, presets, read-only matrix page |
| System health | two booleans on dashboard | Page with live DB/search checks and explicit "not measured" list |
| Moderation | 4 separate queues | Overview page + sidebar badges (all queues unchanged) |

## 2. The 16 checklist modules

| # | Module | Status | Where / why |
|---|---|---|---|
| 1 | User management | **Implemented** | `/admin/users` |
| 2 | Content management | **Already existed** | Papers, Question Bank, Exams, Subjects, Tests (unchanged, regrouped) |
| 3 | Payments | **Not applicable — no payment system exists** | Needs a provider; see §11. Cards show "Not available" |
| 4 | Notifications | **Implemented** | `/admin/notifications` (delivery/click stats not recorded, stated in UI) |
| 5 | Reports & exports | **Implemented** | `/admin/reports` |
| 6 | Roles & permissions | **Improved** | 4 new permissions, presets, `/admin/roles` matrix |
| 7 | Order management | **Not applicable** | No orders/products. Deliberately not invented |
| 8 | Support tickets | **Implemented** (extends feedback) | `/admin/support` |
| 9 | Live analytics | **Implemented, with limits** | Login history is not stored → "active learners" (students who started a test) instead of login DAU/MAU, stated in UI |
| 10 | Audit logs | **Already existed** | Plus audit entries for every new sensitive action |
| 11 | Feature flags / config | **Implemented** | `/admin/app-config` |
| 12 | Coupons | **Not applicable** | No purchases |
| 13 | Partner management | **Mapped** to Manage Organizations | Menu label "Partners / Organizations"; org performance chart added |
| 14 | Moderation | **Improved** | `/admin/moderation` + badges |
| 15 | Session / device management | **Implemented** | per-student + admin sessions |
| 16 | Force update / version control | **Implemented** | admin config + Flutter gate |

## 3. Sidebar / route map

Group → items (all old URLs preserved):

* **Overview** — Dashboard `/admin`, Analytics `/admin/analytics` (ViewAnalytics)
* **Exam Content** — PYQs, Add Paper, All Papers, Shared Content, Bulk Editor, Manage Exams, Subjects
* **Tests & Learning** — Practice Tests, Mock Tests, Quizzes
* **Users & Community** — Students `/admin/users`, Group Chat, Support Tickets `/admin/support`, Feedback (classic) `/admin/feedback`
* **Review & Quality** — Moderation Overview `/admin/moderation`, Review Queue, Question Reports, Solutions Queue, Reported Comments
* **Business** — Partners / Organizations, Reports & Exports `/admin/reports`
* **Communication** — Notifications `/admin/notifications`
* **Administration** — Tasks, App Configuration `/admin/app-config`, System Health `/admin/system-health`, Audit Log, Manage Admins, Roles & Permissions `/admin/roles`, Admin Sessions `/admin/sessions`
* **Account** — Two-Factor Auth, Security

No "Payments / Orders / Coupons" entries exist because there is nothing behind them.

## 4. Backend endpoints added

| Endpoint | Permission (server-enforced) |
|---|---|
| `GET /api/admin/analytics/overview` | any admin; user & learning KPIs only with **ViewAnalytics** |
| `GET /api/admin/analytics/{users,content,learning,community}` | ViewAnalytics |
| `GET /api/admin/analytics/operations` | Audit |
| `GET /api/admin/analytics/badges` | any admin; each count only if the admin may open that queue |
| `GET /api/admin/reports/catalog`, `GET /api/admin/reports/{type}?format=csv\|xlsx` | ExportReports **plus** the data permission of that report |
| `GET/PATCH/POST /api/admin/users…` (list, detail, `{id}/status`, sessions, revoke, revoke-all) | ManageUsers |
| `GET/POST /api/admin/sessions…` | SuperAdmin |
| `GET/PUT /api/admin/app-config`, `…/revisions`, `…/revisions/{id}/rollback`, `…/version-impact` | ManageAppConfig; critical keys additionally SuperAdmin (enforced in the service) |
| `GET /api/app-config`, `GET /api/app-config/status` | public (read-only) |
| `POST /api/admin/notifications/{audience-preview}`, `GET …/find-users`, `GET …/campaigns` (existing `announce` unchanged) | PostNotices |
| `GET/PATCH/POST /api/admin/support/…` (agents, tickets, notes, respond) | ManageFeedback |
| `GET /api/admin/system-health` | ViewAnalytics |

Behavioural changes to existing endpoints: registration and feedback submission honour their feature flags; login/refresh now record the user-agent; a global maintenance middleware (admin routes, `/api/app-config` and admins are exempt; **fails open**); CORS now exposes `Content-Disposition` and `X-Report-Truncated` to the already-allowed origins (nothing else changed).

## 5. Database changes

Migration `20261016000000_AddAdminPlatformFeatures` (hand-written, with `Down`, snapshot updated):

* `AppSettings`, `AppSettingRevisions` (config + append-only history)
* `FeedbackNotes` (ticket timeline; FK to Admins is `Restrict` — admins are deactivated, never deleted)
* `UserFeedbacks` + `Priority` (default Normal), `AssignedToAdminId`, `ResolvedAt`
* `RefreshTokens` + `UserAgent`
* All additive; existing rows untouched. No data migration needed.

Optional: `Scripts/AdminAnalyticsIndexes.sql` — idempotent indexes for analytics queries, with a commented rollback block. Apply only if the analytics queries are slow on your data volume.

## 6. KPI / chart definitions (single source: `AdminAnalyticsService`; the tooltip text and the exports use the same strings)

* **Day boundaries**: the browser sends its UTC offset; days, weeks and months are admin-local. Previous period = the immediately preceding span of equal length.
* **Active learner**: distinct student who *started* a test in the bucket. **Test attempt**: one started attempt.
* **Completed**: Submitted or AutoSubmitted. **Abandoned**: Expired. **Completion rate** = completed ÷ started (attempts started in the period).
* **Accuracy** = correct ÷ (correct + wrong + skipped), averaged over completed attempts.
* **Total questions** = PYP questions + active Question Bank questions, excluding auto-mirrored copies (no double count).
* **Papers rejected**: counted from "Paper.Reject" audit entries (a rejected paper returns to Draft).
* **Active today / 30 days**: from each student's *latest* login time — point-in-time only, so these cards have no previous-period delta (we don't fake one).
* **Cumulative users**: accounts created up to the bucket end; deleted accounts are gone from the database, so old totals can read slightly high (stated in UI).
* **Not available (shown as such)**: revenue/MRR/refunds, notification delivery/failed/clicks, login-based DAU/MAU history, CPU/disk/uptime, failed-login counts.
* Charts: line, area, stacked bar, grouped bar, horizontal bar, donut, scatter (sampled to 400 points, with a visible note), sparklines, tables, status bars. Server caches each result 60 s; **Refresh** bypasses it.

## 7. Roles & permission matrix

New permissions: `ViewAnalytics`, `ExportReports`, `ManageUsers`, `ManageAppConfig` (stored as strings; no schema change). SuperAdmin holds all implicitly. Existing admins gain **none** of the new ones automatically.

| Area | Needs |
|---|---|
| Dashboard (content + moderation KPIs) | any admin |
| Dashboard user/learning KPIs, Analytics, System Health | ViewAnalytics |
| Analytics → Operations | Audit |
| Students | ManageUsers |
| Reports & Exports | ExportReports (+ ViewAnalytics / ManageUsers / Audit depending on report) |
| Notifications | PostNotices |
| Support | ManageFeedback |
| App Configuration | ManageAppConfig; maintenance mode, registration flag, minimum versions: SuperAdmin only |
| Admin Sessions, Roles, Manage Admins | SuperAdmin |

Presets in the editor (client-side only; server validates every key): Content editor, Reviewer/publisher, Moderator, Support agent, Analyst, Test manager.

Security properties: lists show masked email/phone (full phone only to SuperAdmin on the detail view); no password, token or hash is ever returned; suspension needs a ≥5-char reason and kills all sessions immediately; admins cannot verify a student's phone; every sensitive action writes an audit entry (`User.Suspend/Reactivate`, `Report.Export`, `AppConfig.Update/Rollback`, `AdminSession.Revoke/ForceLogout`, `Support.Update/Respond`, `Notification.Announce`); CSV/Excel cells starting `= + - @` are neutralised.

## 8. Test results and known issues

* Web: `npm run build` ✔, `npm test` ✔ (21 tests: date-range presets incl. month/year boundaries and timezone sign, custom-range validation, permission-catalogue integrity, KPI formatting), `npm run lint` — 0 errors.
* API / Flutter: **not compiled or run** (see §0). No C# test project was added because it could not be verified here.
* Not exercised in a browser against a live API (no database in the sandbox). Do a click-through on staging: log in as SuperAdmin and as a limited admin, open each new page, suspend/reactivate a test student, send a notification to one test student, flip maintenance on/off, download one CSV and one XLSX.
* Known limits: multi-instance deployments can lag config reads by up to 30 s and analytics caches are per-instance; the web bundle is a single 2 MB chunk (pre-existing; code-splitting is a follow-up); the admin Flutter version impact only covers devices that registered for push and reported a version (older app builds report none until updated).
* Worth a look: the old "Feedback" page still works unchanged beside the new Support page (same records).

## 9. Deployment

1. Back up the database. 2. Deploy API (build first!) — apply the migration (`dotnet ef database update` or your pipeline). 3. Deploy web. 4. In Manage Admins, grant the new permissions to the people who need them (SuperAdmin already has them). 5. Release the Flutter build (needs `flutter pub get` for `package_info_plus`). **Order matters for force-update**: the app gate fails open, so an old app simply ignores it; set a minimum version only after the new build is live in the stores and after checking the *version impact* number.

Rollback: web/Flutter — redeploy previous build (the gate is inert when the server has no minimum set). API — redeploy previous build; the new tables/columns are ignored by old code. Full DB rollback: `dotnet ef database update <previous migration>` runs the `Down` (drops the two config tables, `FeedbackNotes`, and the 4 added columns — **ticket notes and config history are lost**). To simply stop maintenance mode in an emergency: Admin → App Configuration → toggle off (admin routes are never blocked), or roll the change back from Change history.

## 10. Flutter

`lib/core/app_gate/*` wraps the app via `MaterialApp.router(builder:)`: checks `GET /api/app-config/status?platform=&version=` at launch and on resume (max once/min), uses a standalone Dio (no auth/refresh side effects), 8 s timeouts, **fails open**. `update_required` → blocking screen with "Update now" (https links only); `maintenance` → notice + Try again; `update_recommended` → dismissible banner. Push registration now also sends `appVersion`. New dependency: `package_info_plus`.

## 11. External dependencies you must supply for the missing pieces

* **Payments / orders / coupons / subscriptions**: a payment provider (Razorpay/Stripe/etc.), a product/plan model, server-side webhook verification, a transactions table. Refunds must be issued through the provider API, never by flipping a status. Until then those modules stay "not applicable".
* **Push delivery/click metrics**: store FCM send results and add click tracking in the app, then the notification stats can be real.
* **Login history**: an event table written on login would enable true DAU/MAU trends.
* **Host metrics / uptime**: an external monitor on `/health`.
* **Firebase** config (`google-services.json` / APNs) for push, as before.

## 12. Prioritised remaining work

1. `dotnet build`, apply migration on staging, fix any compile errors; `flutter pub get && flutter analyze`. (Blocking)
2. Staging click-through of every new page with two roles (SuperAdmin, limited admin). (Blocking)
3. Add a C# test project for `CompareVersions`, `ResolveRange`, `ClientInfo`, and permission checks on the new controllers.
4. Code-split the admin bundle (`React.lazy` per page).
5. Login-event table → real DAU/MAU; push-result logging → real delivery stats.
6. Payments stack (only if/when the business needs it).
7. Optional: per-admin activity drill-down, saved report schedules (needs a scheduler), role *templates* stored server-side.
