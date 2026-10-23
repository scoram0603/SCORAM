# Exam-wise Subject & Topic Management — implementation report

Files in this package keep their original project paths: copy them over the matching files in ScoramAPI / ScoramWeb.

## 1. Root cause & schema findings
- Subject/Topic were **global** master data (`QuestionBankSubject` / `QuestionBankTopic`). Every `QuestionBankQuestion` held a single `SubjectId`+`TopicId`; `QuestionBankExamMapping` links question↔exam(+year) but carries **no classification**. So a rename/delete was global and a question had one classification across all exams.
- Fix: a layer **on top of** the existing tables, no existing table altered:
  - `ExamSubject`, `ExamTopic` – per-exam subjects/topics with their own stable Id and own Name (copied from the global record at initialisation, then independent). `BaseSubjectId/BaseTopicId` = optional lineage link only.
  - `QuestionBankExamClassification` – one row per (question, exam) → exam subject/topic (NULL = explicitly unassigned). Unique (question, exam).
  - Resolution: explicit classification row wins; else legacy fallback via lineage to the question's global subject/topic. Existing data therefore keeps working with zero data change until an admin initialises an exam.

## 2. Files
**API (new):** Models/ExamTaxonomyModels.cs, DTOs/ExamTaxonomyDTOs.cs, Services/ExamTaxonomyService.cs, Controllers/ExamTaxonomyController.cs, Migrations/20261022120000_AddExamTaxonomy.cs, Scripts/AddExamTaxonomy.sql
**API (modified):** Data/ScoramDbContext.cs, Migrations/ScoramDbContextModelSnapshot.cs, DTOs/QuestionBankDTOs.cs, Controllers/QuestionBankController.cs, Controllers/ExamsController.cs, Services/SubjectManagementService.cs, Program.cs
**Web (new):** admin/api/examTaxonomy.js, admin/components/ExamTaxonomyDialogs.jsx, admin/pages/ExamTaxonomyManagement.jsx
**Web (modified):** admin/AdminApp.jsx, admin/components/AdminSidebar.jsx, admin/lib/permissions.js, api/questionBank.js, pages/QuestionBankSearch.jsx

## 3. Migration (non-destructive, additive)
`20261022120000_AddExamTaxonomy` creates 3 tables + indexes only; `Down` drops them. `Scripts/AddExamTaxonomy.sql` is the idempotent equivalent (also inserts the `__EFMigrationsHistory` row). **Run it before deploying the API.** All FKs are Restrict except classification→question (cascade, like exam mappings).

## 4. Endpoints  (`api/admin/exam-taxonomy`, Admin/SuperAdmin + `ManageSubjects`; map/unmap/classifications also accept `ManageQuestionBank`)
- `GET exams` · `GET {examId}` (overview, search/status filter) · `GET/POST {examId}/initialize[/preview]`
- Subjects: `POST/PUT/PATCH active/GET usage/GET delete-preview/DELETE {examId}/subjects[/{id}]`
- Topics: `GET/POST {examId}/subjects/{subjectId}/topics`, `PUT/PATCH active/GET usage/GET delete-preview/DELETE {examId}/topics/{id}`
- Questions: `GET {examId}/questions`, `POST {examId}/questions/map`, `POST {examId}/questions/unmap`, `GET question/{id}/classifications`
- Delete: `?mode=archive|unassign|delete&confirm=true&confirmName=…&expectedQuestions=N`. Errors use the existing `{message, code, data}` shape (409 `*_IN_USE`, `IMPACT_CHANGED`, duplicate name, stale `Version`, 403/401).
- Reused/extended (backward compatible): `GET question-bank/subjects?examId=`, `GET question-bank/topics?subjectId=&examId=`, `GET question-bank/search` (new optional `examSubjectIds`, `examTopicIds`), `GET question-bank/{id}?examId=`.
- Safety: every write is in one transaction (execution strategy + rollback) with its audit-log row; delete is blocked/offers archive or explicit unassign when questions depend on it; nothing is silently reassigned; a global-subject delete is blocked while exam subjects link to it; deleting/merging an exam handles its taxonomy (merge refused with `EXAM_HAS_TAXONOMY` until resolved).

## 5. Admin UI
New sidebar item **Exam Content → Exam Subjects & Topics** (`/admin/exam-taxonomy`, needs ManageSubjects): choose exam → subjects (search/status filter, counts) → topics → Questions tab (scope by subject/topic/unassigned, search, paging, multi-select **Map to subject/topic** or **Unassign**, per-question "All exams" classification view). Rename / archive-restore / usage / delete dialogs; delete shows an impact preview (questions, papers, tests, quizzes, answers) and needs typed-name confirmation. "Initialise from current classification" banner for exams without their own list (preview → confirm). Website Question Bank search now uses exam-specific subject/topic lists and filters when a single exam is selected.

## 6. Existing-data behaviour
Nothing changes on deploy. Per exam, an admin runs **Initialise** (previewed, idempotent, refuses on name conflicts, never renames/merges). It creates exam subjects/topics from the global ones in use and classification rows for questions. Questions, papers, tests, attempts, results and `StudentAnswer` snapshots are never rewritten.

## 7. Tests executed
- **Web:** `npm run build` ✔, `npm test` 85/85 ✔, `oxlint` – no errors; 2 style warnings in the new dialogs file (same kinds the repo already has).
- **API:** NuGet was blocked in my sandbox so the real project could not be restored/built. I compiled the **real** service/controller/model code against an in-memory EF stand-in and ran **101 checks, all passing** (see `verification-harness/`), covering your 10 scenarios: rename subject/topic per exam; delete subject per exam; blocked delete with dependents; delete topic with questions; same question mapped differently in two exams; exam-specific filters; attempts/results untouched; unauthorised users blocked; failed transaction leaves no partial changes (plus duplicates, stale version, initialise conflicts, My-Exams scope).

## 8. Limitations / manual steps
- **Not verified against real EF Core / SQL Server:** the migration, hand-edited model snapshot, EF fluent config, LINQ→SQL translation and the `ExamsController` edits. Run `dotnet build` and a smoke test on staging first (and `dotnet ef migrations has-pending-model-changes`).
- Practice-test / mock-test / quiz builders and the Flutter app still use global subjects. Flutter gets exam-specific names in search results when filtering by one exam, but must pass `examId` / `examSubjectIds` to get exam-specific filters.
- Legacy PYP text field `Question.Subject` is unchanged; a later *global* rename/merge does not change an exam's own list; questions unassigned in an exam show global names.
- A classification row can outlive an exam mapping if a question is later untagged from that exam (it is ignored in counts and is reused if re-tagged).
- Steps: apply migration/SQL → deploy API + web → grant `ManageSubjects` → open Exam Subjects & Topics and initialise each exam.
