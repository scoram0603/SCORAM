# SCORAM — Single-Device Login: Implementation Report

**Scope of this round:** the "one user = one active device" security feature only, exactly as scoped in the approved plan. All Question Bank / API performance work from the original brief (catalog caching, search rewrite, SQL indexes, client fan-out/caching, etc.) is **explicitly deferred** to a later round — see "Deferred to a later round" below.

**Status:** Implemented and committed in all four repos. React is fully built/linted/tested in this sandbox. ScoramAPI, ScoramAPI.Tests and the Flutter app could **not** be compiled or run here (no .NET SDK, no Flutter SDK, no network route to NuGet/pub.dev in this sandbox) — they were written carefully against the existing codebase's own patterns, and an exact verification checklist is given below instead of a false "tests passed" claim.

---

## 1. Architecture, in one paragraph

Single-device enforcement is a **new, separate mechanism**, not a reuse of the existing `SecurityStamp` (which already drives logout/password-change/deactivation/theft-response). A fresh `User.ActiveSessionId` (nullable `Guid`) is generated on every successful Register/Login/LoginWithOtp; it's baked into the JWT as a `"sid"` claim and checked on every subsequent request. A new login also supersedes every refresh token issued before it, tagged `RevokedReason = "SupersededByLogin"`. The old device gets a specific, machine-readable error — `401 { "code": "SESSION_REVOKED", "message": "Your account was logged in on another device." }` — on its very next API call, and on its next refresh attempt. Every other 401 (expired token, logout, password change, deactivation, genuine refresh-token theft) is **byte-for-byte unchanged**. Admins are intentionally exempt (see `AdminSessionsController`, which already gives SuperAdmins explicit multi-session management) — `Admin`/`AdminAuthController` were not touched.

## 2. Files changed, by repo

### ScoramAPI (commit `6e9c335`, on top of baseline `3f725e2`)
| File | Change |
|---|---|
| `Models/User.cs` | + `ActiveSessionId` (`Guid?`, nullable) |
| `Models/RefreshToken.cs` | + `RevokedReason` (`string?`, max 50) + `RevokedReasonSupersededByLogin` constant |
| `Migrations/20261109000000_AddSingleDeviceLogin.cs` (+ `.Designer.cs`) | New migration — see §3 |
| `Migrations/ScoramDbContextModelSnapshot.cs` | Updated to match |
| `Services/TokenService.cs` | `GenerateToken` adds a `"sid"` claim, omitted entirely when `ActiveSessionId` is null |
| `Services/RefreshTokenService.cs` (+ interface) | New `SupersedeForNewLoginAsync(principalId)` — revokes all active non-admin tokens for the user, tagged with the new reason |
| `Services/SecurityStampService.cs` | New `IsSessionValidAsync`; refactored to share one cache entry (`"stamp\|sid"`) with the existing stamp check — no extra DB round trip or cache entry per request |
| `Controllers/AuthController.cs` | `IssueAuthResponseAsync` now supersedes old sessions before issuing new tokens; `Refresh()` gets a new branch that returns `SESSION_REVOKED` for a superseded token **before** the existing theft-detection `else` |
| `Program.cs` | `OnTokenValidated` checks the `sid` claim (students only, skipped entirely if absent); new `OnChallenge` writes the JSON body for `SESSION_REVOKED`, otherwise defers to the existing bodyless-401 default; the "My Exams" scoped-public-routes middleware (a separate early-exit 401 path that runs *before* `UseAuthorization()`/`OnChallenge` ever fires) is patched the same way |

**Not touched:** `AdminAuthController`, `Admin` model, `AdminSessionsController`, every Question Bank / papers / exams controller and service.

### ScoramAPI.Tests (commit `34fe05a`, on top of baseline `abb3e49`)
| File | Change |
|---|---|
| `Services/SecurityStampServiceTests.cs` | +4 tests for `IsSessionValidAsync` and the shared-cache-entry refactor |
| `Services/RefreshTokenServiceTests.cs` | +4 tests for `SupersedeForNewLoginAsync` |
| `Services/TokenServiceTests.cs` | +3 tests for the `sid` claim (present/absent/admin-never) |
| `Controllers/SingleDeviceLoginTests.cs` (new) | End-to-end scenarios A, B, C, E, I, J, K, N from the brief (see §8 for the full scenario mapping) |

### App — Flutter (commit `d0d1195`, on top of baseline `c32096a`)
| File | Change |
|---|---|
| `lib/core/network/api_client.dart` | New `RefreshResult.rejectedSessionRevoked`; `_doRefresh()` detects `code == "SESSION_REVOKED"`; `onUnauthorized` signature changed from `void Function()?` to `void Function({bool revokedElsewhere})?` |
| `lib/features/auth/data/auth_state.dart` | + `sessionRevokedElsewhere` field |
| `lib/features/auth/data/auth_controller.dart` | `_handleSessionExpired` threads the flag through |
| `lib/features/auth/presentation/login_screen.dart` | Distinct banner copy when `sessionRevokedElsewhere` is true |
| `test/core/network/api_client_test.dart` | Fixed 3 existing closures broken by the signature change; +2 new tests |

### Web — ScoramWeb student site (commit `138ba4d`, on top of baseline `f2542a5`)
| File | Change |
|---|---|
| `src/api/client.js` | `refreshStudentSession()` reads `code === "SESSION_REVOKED"` off a rejected `/api/auth/refresh`; threaded through a module-level flag into the `scoram:session-expired` event's `detail` |
| `src/context/AuthContext.jsx` | New `sessionRevokedElsewhere` state, reset on login/logout/account-deletion |
| `src/pages/Login.jsx` | Distinct banner copy when set |
| `src/api/__tests__/client.test.js` (new) | 4 tests: propagation, the negative case, no-leak-into-later-event, admin event untouched |

**Not touched:** `src/admin/**` (`AdminAuthContext.jsx`, `adminAuth.js`, `AdminLogin.jsx`) — admins stay multi-device by design.

## 3. Database migration

Additive-only, both columns nullable, no backfill, no existing column/row/index/table altered:

```sql
ALTER TABLE Users ADD ActiveSessionId uniqueidentifier NULL;
ALTER TABLE RefreshTokens ADD RevokedReason nvarchar(50) NULL;
```

Hand-written as `Migrations/20261109000000_AddSingleDeviceLogin.cs` (+ a generated `.Designer.cs` and updated model snapshot), since `dotnet ef` isn't available in this sandbox. **This must be regenerated/verified with the real tool before trusting it** — see the checklist in §9.

**Rollback:** `Down()` drops exactly these two columns. No data migration ever ran, so rollback is clean at any point.

## 4. Deployment safety — the one honest tradeoff

A token issued *before* a user's next post-deployment login carries no `sid` claim and is never checked against `ActiveSessionId` — this is what prevents the deployment itself from logging anyone out. The tradeoff: a device that logged in before the deploy and never logs in again could keep using its pre-deploy access token for up to its full lifetime (`Jwt:ExpiryMinutes`, 60 min by default) even after a second device logs in post-deploy. This is bounded, time-limited, and was an explicit requirement ("existing users must not be accidentally logged out during deployment") — flagging it rather than glossing over it.

## 5. Error contract

```
HTTP 401
{ "code": "SESSION_REVOKED", "message": "Your account was logged in on another device." }
```
Returned from exactly three places, all server-side: `OnChallenge`, the scoped-public-routes middleware, and `Refresh()`'s new branch. Every other 401 path is unchanged.

## 6. SignalR

Per the brief's own guidance ("do not rely solely on SignalR for revocation... implement server-side request/session validation first"), an **already-connected** hub session is not forcibly disconnected in this round — REST calls are the enforced source of truth, and a reconnect/new connection correctly re-validates. Forced hub disconnect is listed as deferred (§10).

## 7. Multiple tabs on the same device

Enforcement keys off *login events*, not requests, tabs, or sessions-per-connection — several tabs sharing the same stored token continue to work normally by construction; nothing here is tab-aware.

## 8. Test scenario coverage (brief's list A–N)

| # | Scenario | Covered |
|---|---|---|
| A | Login on Device A, then Device B — A's next request fails | ✅ `SingleDeviceLoginTests.cs` |
| B | A's next **refresh** fails with SESSION_REVOKED, not generic theft | ✅ `SingleDeviceLoginTests.cs` (the collateral-damage case) |
| C | Refresh tokens are actually invalidated, not just left to expire | ✅ `SingleDeviceLoginTests.cs` + `RefreshTokenServiceTests.cs` |
| D | A successful refresh rotation after supersession | ⚠️ **Not testable here** — EF Core's in-memory provider doesn't support `ExecuteUpdateAsync`/transactions, a pre-existing, already-documented limitation (`RotateAsync_...` is already `[Fact(Skip=...)]` in this codebase). Needs a relational test DB. |
| E | Flutter 401+SESSION_REVOKED → logout, specific message | ✅ `test/core/network/api_client_test.dart` |
| F | React same | ✅ `src/api/__tests__/client.test.js` (actually run — passing) |
| G | Logout → re-login on the same device doesn't self-revoke | Logically covered by design (new login always supersedes *previous* tokens, including its own prior one) but not written as a standalone test — low risk, flagging for manual check |
| H | Two devices both log in — both prior sessions revoked, neither treated as theft | ✅ `SingleDeviceLoginTests.cs` (`TwoSupersededDevices_...`) |
| I | Multi-tab, same device, both keep working | ✅ by construction (§7) + `SingleDeviceLoginTests.cs` |
| J | SignalR | Partially — new connections re-validate; forced disconnect of an open one is deferred (§10) |
| K | Migration safety — pre-deploy tokens not logged out | ✅ `TokenServiceTests.cs` (omits `sid` when null) + design (§4) |
| L | `dotnet ef migrations add` dry-run / review | ⚠️ **Not run here** — see checklist |
| M | `dotnet test` full run | ⚠️ **Not run here** — see checklist |
| N | Manual two-browser-profile walkthrough | ⚠️ **Not run here** — see checklist |

## 9. Verification checklist — please run before merging

Nothing below has been executed by me; this sandbox has no .NET SDK, no Flutter SDK, and no network route to NuGet or pub.dev.

1. `dotnet restore && dotnet build` (ScoramAPI + ScoramAPI.Tests)
2. **Regenerate the migration properly** rather than trusting the hand-written one:
   `dotnet ef migrations add AddActiveSessionId --project ScoramAPI` against a throwaway/dev DB, diff it against mine, then `dotnet ef database update` on a **non-production** copy first.
3. `dotnet test` — all of `ScoramAPI.Tests`, including the new `SingleDeviceLoginTests.cs`. One **new risk to watch**: this is the first test file in the project to call `BCrypt.Net.BCrypt` directly. It should resolve transitively through the existing `ScoramAPI` ProjectReference (same as EF Core types already do), but if `dotnet build` disagrees, add `<PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />` to `ScoramAPI.Tests.csproj`.
4. Note: `ScoramAPI.Tests.csproj` references `..\ScoramAPI\ScoramAPI.csproj` as a sibling folder — true in your real repo presumably, just not in my scratchpad extraction (`api/` / `tests/` were separate top-level dirs here), so nothing to fix, just flagging why I couldn't actually invoke `dotnet` here even if the SDK were present.
5. `flutter analyze && flutter test` for the app.
6. Manual two-device (or two-browser-profile) walkthrough of scenarios A, B, D, G, J, N above against a running API + both clients.
7. React — **already done in this sandbox**: `npm test` → 147/147 passing (143 pre-existing + 4 new), `npm run lint` → clean (oxlint exit 0, only pre-existing warnings elsewhere in the codebase), `npm run build` → succeeds.

## 10. Deferred to a later round

- All Question Bank / API performance work (Phases 3–6 of the original brief) — per your own pacing choice, this round was single-device login only.
- Admin single-device enforcement (left multi-device, matching `AdminSessionsController`'s existing design).
- Forced disconnect of an already-open SignalR hub connection on revocation.
- A standalone automated test for scenario G (logout → re-login same device) — logically sound by design, just not written as its own test case.

## 11. Rollback

Each repo's changes sit in exactly one commit on top of a clean baseline commit (`git revert <commit>` in each of the four repos undoes this round cleanly). The DB migration's `Down()` drops the two new columns with no data loss, since nothing in this feature ever migrates or depends on existing data.
