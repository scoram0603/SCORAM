# SCORAM — Subject Management: implementation report

## 1. What I found (analysis before code)

**There is already a real Subject entity — I reused it, no new table.**
`QuestionBankSubject` (`Id, Name, IsActive, CreatedAt, CreatedByAdminId`, unique index on `Name`). An older minimal screen (`/admin/question-bank/subjects-topics`, endpoints under `api/admin/question-bank/subjects`) already lets admins add/retire subjects and topics. I left all of that untouched.

**Subjects are linked to content in three different ways** — the key finding that shaped the design:

| Content | How it links to a subject | Handled by merge/reassign/rename |
|---|---|---|
| **PYQ** = Question Bank questions | FK `QuestionBankQuestion.SubjectId` | Re-pointed |
| Topics | FK `QuestionBankTopic.SubjectId` (unique per subject+name) | Moved, or folded into a same-named topic |
| Practice Test templates | FK `PracticeTestTemplate.SubjectId` (nullable) | Re-pointed |
| **PYP** = questions inside uploaded papers | **Plain text** `Question.Subject` (max 50 chars, free-typed) | Text re-labelled to the target's name |
| Mock Tests / Quizzes / Papers | **No SubjectId of their own** — they only hold questions | Nothing to update; they follow the question. Shown as counts, *not* fabricated as "records to move" |
| Past student answers / practice attempts | Snapshots (`SubjectSnapshot`, `PracticeSubjectId`) | Deliberately **never rewritten** (history) |

PYP questions are also auto-mirrored into the Question Bank (resolved **by name**), so a rename/merge that only touched the FK rows would make the two representations drift apart. That's why the PYP text is rewritten in the same transaction.

## 2. Files

**New (9):**
`Controllers/SubjectManagementController.cs`, `Services/SubjectManagementService.cs`, `DTOs/SubjectManagementDTOs.cs`, `Migrations/20260929120000_AddQuestionBankSubjectUpdatedAt.cs`, `Scripts/AddQuestionBankSubjectUpdatedAt.sql`, `admin/api/subjects.js`, `admin/pages/SubjectManagement.jsx`, `admin/components/SubjectManagementDialogs.jsx`, `admin/components/subjectFormat.js`

**Modified (7, small edits):**
`Enums/Enums.cs` (+`ManageSubjects` permission), `Models/QuestionBankModels.cs` (+`UpdatedAt`), `Migrations/ScoramDbContextModelSnapshot.cs` (+3 lines), `Program.cs` (+1 DI line), `AdminSidebar.jsx` (+1 item, +1 icon), `AdminApp.jsx` (+1 route), `ManageAdmins.jsx` (+1 permission entry)

## 3. Database
One **additive, nullable** column: `QuestionBankSubjects.UpdatedAt datetime2 NULL`. No existing row is touched; no data is moved. Provided as an EF migration **and** an idempotent SQL script (your repo applies migrations manually — no `Migrate()` on startup).

**Deploy order matters:** apply the column *before* the new API build goes live, otherwise every query on `QuestionBankSubjects` fails.

Not needed / not added: `Description` (the existing model has none, per your instruction), a separate "Archived" flag (the project's existing retire pattern is `IsActive=false`; archive = deactivate, restore = re-activate), any new audit table.

## 4. API (`api/admin/subjects`, all `[Authorize(Admin,SuperAdmin)]` + `ManageSubjects` checked server-side)
`GET /` list (search, status, sort, paging + live usage counts) · `GET /{id}` detail + usage + topics · `POST /` create · `PUT /{id}` rename · `PATCH /{id}/active` · `POST /merge/preview` · `POST /merge` · `POST /reassign/preview` · `POST /reassign` · `GET /{id}/delete-preview` · `DELETE /{id}?confirm=true&confirmName=`

Errors return `{ message, code, data }` — friendly text, never SQL or stack traces (`DUPLICATE_SUBJECT`, `SUBJECT_IN_USE`, `SOURCE_IS_TARGET`, `IMPACT_CHANGED`, `CONCURRENT_MODIFICATION`, `ALREADY_INACTIVE`, …).

## 5. Behaviour
- **Duplicates:** trimmed, inner whitespace collapsed, case-insensitive, checked against *all* subjects (inactive included). "General Knowledge" vs "General Awareness" stay separate — no semantic matching.
- **Rename:** same record/Id kept. Confirmation required when in use (also enforced by the API). Legacy PYP text re-labelled in the same transaction.
- **Merge / reassign:** one DB transaction, full rollback on any failure. Sources are **deactivated, not deleted**, on merge. Never touches question text, `UpdatedAt` of questions, or creates/copies/deletes any question/paper/test/quiz. Topics with a name that already exists under the target are folded into it (no duplicate topics).
- **Safety on top of the spec:** merge requires typing the target name (checked server-side too); `expectedTotalAffected` from the preview is compared to live numbers at execution — if content changed meanwhile, nothing runs and a fresh preview is shown. Stale-edit detection via a version token.
- **Delete:** only when there is zero linked content (Q-Bank, PYP, topics, templates, recorded attempts); otherwise `SUBJECT_IN_USE` with counts and options (reassign / archive / cancel). Zero-dependency delete is a real delete (per your spec).
- **Audit:** uses the existing `AuditLog` table (`Subject.Create/Update/Activate/Deactivate/Merge/Reassign/Delete`), written **in the same transaction** as the change; the existing Audit Log page displays them with no changes.
- **Search index:** rewritten PYP text is re-indexed for affected *published* papers via the existing job queue (inline fallback if no queue). Best-effort, same contract as PapersController.
- **Permissions:** new `ManageSubjects` (Super Admin implicitly). Stored as a string, so no migration for it. Grant it under *Manage Admins → Permissions*.
- Inactive subjects already drop out of existing new-content dropdowns and student filters (they call the existing active-only endpoints) — nothing needed changing there.

## 6. Verification actually performed — and not
**Done:** full frontend production build passes; lint clean on new files; new C# compiled with Roslyn against the real .NET 8 / ASP.NET reference assemblies with hand-written EF Core signature stubs (caught and fixed one real bug: `Data` hid `Exception.Data`); name-normalisation/duplicate logic executed and passes; diff confirmed to touch only the files listed.

**NOT done — NuGet and a database aren't reachable from my environment:**
- The API was **not built with the real EF Core package** and nothing was run against SQL Server. The LINQ (group-bys, `ExecuteUpdateAsync`, `Trim().ToLower()` predicates) is written for EF Core 8 but is unproven until a real build + run.
- Your 22-item test list therefore has **not been executed**. Please run it on a staging copy, especially: merge (incl. multi-source), forced-failure rollback, topic collision, rename with PYP questions, delete blocked/allowed, unauthorized merge → 403, and PYQ/PYP/Mock/Quiz/student-side filtering afterwards.

## 7. Assumptions & things to know
1. "PYQ" = Question Bank, "PYP" = paper questions (matches your sidebar labels).
2. "Total affected records" = rows a merge actually rewrites (PYQ + PYP + topics + practice templates). Mock/Quiz/Paper numbers are shown separately as "follows automatically". A PYP question and its Question Bank mirror can both appear in the counts (noted in the UI).
3. **Free-text PYP subjects:** admins can still type any subject string when uploading a paper. If someone types an old/merged name later, it won't map to the merged subject. The old name is also *reserved* (unique index), so the mirror can't silently recreate it. Suggested follow-up: turn the PYP subject input into a dropdown of active subjects.
4. **Bulk import by name** resolves subjects including inactive ones (existing behaviour in `QuestionBankImportCommitService`) — an import using a merged-away name would attach questions to the deactivated subject. Existing behaviour, left alone; an alias table would fix it properly.
5. **Weak-topic analytics** group past answers by the subject *name recorded at answer time*; after a rename/merge, history under the old name stops counting toward the new name until new answers accumulate. Rewriting millions of `StudentAnswer` snapshots inside the merge transaction seemed riskier than the benefit — say if you want it as an optional step.
6. A subject whose only dependency is empty topics can't be hard-deleted (no topic-delete exists) — deactivate it instead.
7. Legacy PYP names longer than 50 characters can't be stored; rename/merge is refused with a clear message only when PYP rows would be affected.
8. **Pre-existing issue found (not changed):** earlier hand-written migrations (`AddPerformanceIndexes`, `AddSecurityStampAndRefreshTokens`, `AddAdminTotpMfa`) have no `[Migration]` attribute, so EF never discovers them. Mine carries its attributes explicitly. Also a comment in `ScoramDbContext` claims a partial unique index on subject names, but the real index is a full unique index.
9. Row-action "Merge" and header "Merge Subjects" open the same dialog; the old Subjects & Topics page remains for topic management and is linked from the usage dialog.
