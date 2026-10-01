# SCORAM — Business IDs (EXMSSC001, SUB001, TST0001, MCK0001, ADM0001)

Human-readable IDs **added alongside** the existing GUID keys of Exams, Subjects, Tests, Mock Tests and Admins.
GUIDs remain the primary key and every foreign key. Nothing is dropped, truncated, recreated or re-keyed.

> **Verification status — read first.** The sandbox this was built in has no NuGet access, so the ASP.NET /
> EF Core project **could not be compiled or run against SQL Server here.** What *was* verified:
> the pure ID rules (formats, validation, organization-code mapping — 60+ assertions pass), a syntax-level
> compile of every new backend file (no syntax errors; only the expected "EF type not found" noise), and
> a full production build of the React app. Everything database-facing (migration, backfill, concurrency,
> no-reuse) is written but **unexecuted** — run the checklist in §7 on a staging copy before production.

---

## 1. Inspection findings (what the code actually looks like)

| Topic | Finding |
|---|---|
| Stack | ASP.NET Core 8 + EF Core 8 on **SQL Server**; React/Vite admin app. |
| Primary keys | Every entity uses `Guid Id`. |
| Exams | `Exam` (`Exams`): `Name`, optional `OrganizationId → Organization`, `IsBlocked`, `CreatedAt`. **Hard-deleted** (Delete + Merge endpoints), so "never reuse" needs more than `MAX()+1`. Created from several places (admin form, PYQ/Question-Bank wizards via get-or-create). |
| Subjects | `QuestionBankSubject` (`QuestionBankSubjects`): `Name`, `IsActive`, `CreatedAt`, `UpdatedAt`. Subject Management service does rename / deactivate / merge / reassign / delete inside transactions with audit logging. |
| Tests | "Tests" in the admin panel = `PracticeTestTemplate` (`PracticeTestTemplates`). |
| Mock Tests | `MockTest` (`MockTests`). |
| Admins | `Admin` (`Admins`), `Role` = `AdminRole { SuperAdmin, Admin }`, JWT role claim, `[Authorize(Roles = "SuperAdmin")]` already used. No admin delete endpoint. |
| Audit | `AuditLog` (`AdminId`, `Action`, `TargetType`, `TargetId`, `Detail ≤1000`) — reused. |
| Migrations | EF migrations with a hand-maintained model snapshot; startup does fail-soft bootstrap work in `Program.cs`. |
| Quizzes | Exist but are **not** in your list — untouched (see §6). |

## 2. Design

* `BusinessId` is a nullable `nvarchar(30)` on the five tables, with a **filtered unique index**
  (`WHERE BusinessId IS NOT NULL`) → the **database** rejects duplicates, and the index can be created
  before any data has IDs.
* **Assignment is central:** `ScoramDbContext.SaveChangesAsync` assigns an ID to every newly inserted
  Exam/Subject/Test/Mock Test/Admin, wherever it was created (controllers, bulk imports, bootstrap).
  No creation site had to change, and a future one can't forget.
* **Concurrency:** `BusinessIdCounters` row per prefix (`EXM:SSC`, `EXM:RRB`, `SUB`, `TST`, `MCK`, `ADM`),
  incremented with one atomic `MERGE … WITH (HOLDLOCK)` **inside the same transaction as the insert**.
  The row lock is held until commit, so simultaneous creates serialize (A→004, B→005). No `MAX()+1`.
* **Never reused:** counters only go up, **and** every ID ever issued is written to `BusinessIdRegistry`
  (primary key = the ID, no FK to the entity). Deleted / merged-away / renumbered IDs stay reserved.
  If the transaction rolls back, the counter bump and registry row roll back with it.
* **Not editable by normal admins:** no create/update DTO carries the field, and `ScoramDbContext`
  throws if anything modifies an existing `BusinessId` outside the SuperAdmin service.
* **SuperAdmin change:** `POST /api/admin/business-ids/change` — role-gated, re-checks the live account,
  validates entity-type format + uniqueness + history, requires `confirm: true`, writes the audit row in
  the same transaction, leaves the GUID and all FKs untouched. A previously-issued ID can't go to a
  different record (only back to its own).
* **Exam organization code** (`ExamOrganizationCodes`): (1) the exam's assigned Organization → map;
  (2) else the exam name's **leading** word(s) → map ("UPSSSC PET"→`UP`, "Bihar Police …"→`BP`);
  (3) unmapped Organization → its own letters (≤10); (4) else `GEN`. Never a blind substring
  ("SSCGD" → `GEN`, not `SSC`). Letters-only so the number can always be parsed back out.

## 3. Existing-data migration (deterministic)

The migration is schema-only. Existing rows are numbered by `BusinessIdService.BackfillAsync`, run
automatically at startup (after the SuperAdmin bootstrap) **using the same code path as new records**:

* oldest first (`CreatedAt`, then GUID as tie-break) → "first exam ever created" = `EXMSSC001`; no randomness;
* one transaction under `sp_getapplock` (two instances starting together can't double-number);
* idempotent; a failed attempt rolls back fully and retries next start;
* until it completes, newly created records keep a null ID and are numbered by the backfill in creation
  order, so existing records still get the low numbers;
* `BusinessIds:AutoBackfillOnStartup=false` disables auto-run; then use
  `POST /api/admin/business-ids/backfill?dryRun=true` (SuperAdmin) to see the exact plan, `dryRun=false` to commit.

## 4. Files

**New (API):** `Models/BusinessIdModels.cs`, `Services/BusinessIdFormats.cs`, `Services/BusinessIdGenerator.cs`,
`Services/BusinessIdService.cs`, `Controllers/BusinessIdsController.cs`, `DTOs/BusinessIdDTOs.cs`,
`Migrations/20260930120000_AddBusinessIds.cs`, `Scripts/BusinessIdsVerify.sql`, this report.
**New (tests):** `ScoramAPI.Tests/Services/BusinessIdFormatsTests.cs` — one file to add to your EXISTING `ScoramAPI.Tests` project (no csproj change).
**New (web):** `src/admin/api/businessIds.js`, `src/admin/components/BusinessId.jsx`.

**Modified (API):** the five entity models (+`BusinessId`), `ScoramDbContext` (DbSets, 5 unique indexes,
assignment + edit guard), `ScoramDbContextModelSnapshot` (hand-updated), `Program.cs` (DI + startup
backfill), admin DTOs (`businessId` next to `id`), `ExamsController`, `AdminAuthController`,
`MockTestsController`, `MockTestsAdminController` (+`search`), `PracticeTestsAdminController` (+`search`),
`QuestionBankAdminController`, `SubjectManagementService` (search by ID; audit text shows IDs).
**Modified (web):** `ExamManagement`, `SubjectManagement`, `MockTestManagement`, `PracticeTestManagement`,
`ManageAdmins` — ID badge beside the name, ID search, SuperAdmin-only "change ID" button + two-step dialog
(entry → "Change Business ID?" warning with typed confirmation).

**DB changes:** columns `BusinessId nvarchar(30) NULL` on `Exams`, `QuestionBankSubjects`,
`PracticeTestTemplates`, `MockTests`, `Admins`; unique filtered indexes `UX_<Table>_BusinessId`;
tables `BusinessIdCounters`, `BusinessIdRegistry`.

**API:** `POST /api/admin/business-ids/change`, `POST /api/admin/business-ids/backfill` (both SuperAdmin);
`search` param on admin mock-test and practice-test lists; `businessId` on admin exam / subject / test /
mock-test / admin responses.

## 5. Deploy steps

1. Back up the database.
2. Apply the migration (`dotnet ef database update`) **before** the new build serves traffic — the new
   code reads the new columns/tables.
3. Start the app once. Watch the log for `Business ID backfill complete: …`.
4. Run `Scripts/BusinessIdsVerify.sql`; sets 1–5 must be empty/expected. **Review result set 6 (exam IDs and
   their organization codes) before anyone uses these IDs externally** — after that only a SuperAdmin can
   change them. The map to review is `ExamOrganizationCodes` in `Services/BusinessIdFormats.cs`.
5. Apply to staging first.

## 6. Assumptions / decisions to confirm

* **Org codes:** the mapping list (SSC, RRB, UP, UPPR, BP, BPSC, BSSC, IBPS, SBI, …) is my best reading of
  your examples plus common bodies — I did not have your live exam list. Unlisted bodies get their own
  letters or `GEN`. Please review step 4 above.
* **"Tests" = `PracticeTestTemplate`.** Quizzes, Papers/PYQ papers were not in scope and have no ID.
* **Column stays nullable** (needed for a safe, additive migration). Once the verify script is clean you may
  add a follow-up migration making it `NOT NULL`; I did not, so a failed backfill can't block inserts.
* IDs are 3-digit (exam, subject) / 4-digit (test, mock, admin) minimum and simply widen past that (`SUB1000`).
* A SuperAdmin cannot move a record to an ID that was *ever* issued to a different record (even a deleted
  one) — that is the "never reuse" rule applied to manual changes.
* Hard deletes of exams/subjects need no code change: their IDs stay in the registry.
* Merges: exam/subject merge code is unchanged; the target keeps its ID, the source's ID is retired for good.
* Multi-entity saves take counter locks in the order the entities appear; deadlocks are theoretically possible
  under heavy parallel mixed creation but not expected at admin-panel volumes.

## 6b. Not touched
Login/auth, question/bulk-upload/PYP/PYQ/quiz/student code and all foreign keys. The only edits outside the
new feature are additive (`BusinessId` on DTOs/selects, `search` params, audit text).

## 7. Test status vs. your 20-point list

| # | Item | Status |
|---|---|---|
| 1–5 | Existing exams/subjects/tests/mocks/admins get IDs | **Not run** (needs SQL Server) → startup backfill + `BusinessIdsVerify.sql` #2 |
| 6–7 | GUIDs & FKs unchanged | By construction (migration only adds columns); confirm with row counts before/after |
| 8 | New records get correct IDs | Format logic **tested**; DB assignment **not run** |
| 9 | Duplicates rejected | Unique indexes + registry PK + service checks; **not run** |
| 10 | Concurrent creation | Design above; **not run** — suggested: 20 parallel `POST /api/admin/exams` then verify script #3 |
| 11 | Deleted IDs not reused | Registry design; **not run** — create exam, delete it, create another, expect next number |
| 12–14 | Admin can't change / SuperAdmin can / no duplicates | Role gate + context guard + service; **not run** |
| 15–16 | Search by ID; name search intact | Code in place (server-side for subjects/tests/mocks, client-side for exams/admins); **not run** |
| 17–18 | Subject merge keeps target ID | Merge code untouched; **not run** |
| 19–20 | Relationships / existing features | **Regression pass required** (login, exam/subject/question CRUD, bulk upload, PYP/PYQ, mocks, tests, quizzes, reports) |

Run the unit tests with `dotnet test` in your `ScoramAPI.Tests` project.
