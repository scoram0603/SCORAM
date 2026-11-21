# SCORAM — Performance Round 2 (P1 API) Report

Scope: ScoramAPI + ScoramAPI.Tests only. No schema migration, no auth/DTO/contract change, no new packages, no deploy, search results untouched.

## 1. Honest status: nothing was compiled or run
This sandbox has no .NET SDK, no NuGet access and no database. **No build, no test run, no SQL was executed.** Every edit was re-read for syntax and signatures, but all verification is yours/CI's (section 7). The supplied audit (`SCORAM_Performance_Audit.md`) was not attached, so claims were re-verified against current code, not taken from it.

## 2. Drift vs. the hand-off
- The Round 1 SQL-01 candidate script was **not** in the zips; a new script was written (section 5).
- The zips contain no `.git`; each repo was `git init`-ed with a baseline commit (`Baseline: supplied zip (post Round 1)`), then one round commit each (API `4d4b910`, Tests `e9c3eb7`). These local histories exist only in this sandbox; the zip delivers files, not history.
- No test covered `ExamsController.List/AdminList` before this round.

## 3. Changes

### API-06 — cancellation tokens (`Controllers/QuestionBankController.cs`)
- Trailing `CancellationToken ct = default` on `Search`, `GetTodaysChallenge`, `GetById`, `GetSubjects`, `GetTopics`, `GetExams`, `GetYears` and private `ApplyExamNamesAsync` (repo convention; all existing call sites still compile).
- `ct` passed to every EF async call in those methods and to the services that already accepted one: `GetScopeAsync`, `IsInitializedAsync`, `GetStudentSubjectsAsync`, `GetStudentTopicsAsync`, `ResolveNamesAsync`.
- `GetOrAddCatalogCacheAsync(key, factory, ct)`: `await gate.WaitAsync(ct)`, kept **outside** the `try` so a cancelled wait never releases a gate it doesn't hold. A cancelled/throwing factory caches nothing (`Set` is only reached on success) and `finally` releases the gate.
- Search's LIKE fallback is unchanged: `IsFullTextUnavailable` already returns false for `OperationCanceledException`, so cancellation propagates and never triggers the fallback.
- **Deliberately not changed: `MarkSolved`.** It writes then awards XP through `IGamificationService.RecordActivityAsync`, which has no token; cancelling mid-write is a consistency decision, not a pure perf fix. Say if you want it.
- Behavior change: none for completed requests; an aborted request now stops its remaining queries (and no longer logs/continues work for a client that left).

### API-07 — `/api/exams` counts (`Controllers/ExamsController.cs`)
- `GetQuestionBankCountsByExamAsync` previously loaded every mirrored id and every distinct (question, exam) pair of the whole bank and grouped in C#. It is now **one grouped server-side query** returning (examId, count) rows, restricted to the exams being returned.
- Same rule, same numbers: per exam, distinct bank questions that are `IsActive`, not `IsTestOnly`, tagged to the exam (any year), and not the target of any `Questions.MirroredToQuestionBankQuestionId` (all mirrors excluded regardless of paper status — intentionally different from `VisibleQuestions()`; not unified).
- `List` and `AdminList` got `ct`. PYP half, DTO, field names, ordering untouched (Flutter/React contract preserved). No cache added (your decision).
- Risk: **not verified that EF 8 translates this to SQL Server as expected** (same GroupBy/Distinct().Count() and NOT EXISTS patterns already exist in `ExamTaxonomyService.ListExamsAsync` and `GetCombinedQuestionCountAsync`, so I expect it to, but it is unrun).

## 4. Test → change map (`ScoramAPI.Tests`)
| Test | Covers |
|---|---|
| `ExamsListCountTests.List_BankHalf_MatchesHandComputedNumbers` | API-07 explicit numbers (dup years, multi-exam, inactive, test-only, Draft/Pending/Published/no-paper mirrors, empty exam, PYP half) |
| `ExamsListCountTests.List_AndAdminList_MatchPreviousInMemoryAlgorithm_ForEveryExam` | API-07 equivalence vs the old algorithm (copied verbatim as oracle) for both endpoints |
| `ExamsListCountTests.List_SearchFilter_DoesNotChangeAListedExamsCount` | API-07 id-restriction does not alter counts; empty result doesn't throw |
| `ExamsListCountTests.List_ResponseShape_IsUnchanged` | order by Name, `BusinessId` null in public list |
| `ExamsListCountTests.List_CancelledToken_Throws` | API-06 on `/api/exams` — **depends on the in-memory provider observing a pre-cancelled token; unconfirmed**. If only this one fails, suspect the provider |
| `QuestionBankEquivalenceTests.CancelledToken_StopsEveryReadEndpoint_BeforeAnyWork` | API-06: token reaches scope service on all 6 list/read endpoints (mock throws on a cancelled token — deterministic) |
| `…CancelledToken_DoesNotChangeResults_WhenNotCancelled` | token param is a pure addition |
| `…CancelledWaitForGate_DoesNotPoisonCache_AndLeavesNothingLocked` | `WaitAsync(ct)` path; next call on same cache succeeds (10 s timeout guard) |
| `…CancelledFactory_DoesNotPoisonCache_AndReleasesGate` | factory throws OCE once → nothing cached, gate released, second call recomputes |

`NewController` in the existing test file gained three optional parameters (`scopeHonorsCancellation`, `taxonomy`, `logger`); every old call is unaffected. I did **not** add a "cancelled Search doesn't log the LIKE-fallback warning" test: in-memory has no `EF.Functions.Contains`, so the full-text path can't be exercised there.

## 5. SQL-01 / SQL-04 — `Scripts/OptionalQuestionBankVisibilityIndexes.sql` (NOT run)
Idempotent, not an EF migration, snapshot unchanged, no existing index dropped. Three candidates, each with its target query, "existing" index and rollback:
1. `QuestionBankExamMappings (ExamId, QuestionBankQuestionId)` — serves the new API-07 GROUP BY (highest confidence).
2. `Questions (MirroredToQuestionBankQuestionId) INCLUDE (PaperId)` — covering the mirror/Paper-status EXISTS checks (medium; adds cost to a write-heavy table).
3. Filtered `QuestionBankQuestions (CreatedAt DESC) INCLUDE (SubjectId, TopicId, Language) WHERE IsActive=1 AND IsTestOnly=0` — lowest confidence; a read-only selectivity query in the header tells you whether to skip it.

These are **hypotheses written without a plan or data**. Apply one at a time on a staging copy and keep only what a before/after plan + `STATISTICS IO/TIME` comparison justifies (procedure is in the script header).

## 6. Remaining risks
- Unrun code (build errors would show up on first `dotnet build`; I checked signatures by reading, not compiling).
- API-07 SQL translation/perf unmeasured; index candidates unmeasured.
- Catalog caches remain TTL-only (3 min) per your Round 1 decision; nothing new cached this round.
- Query-string note: `CancellationToken` is bound by ASP.NET automatically — no route/contract change.

## 7. NOT YET RUN BY ME — checklist for you/CI
1. `dotnet build` (API + Tests).
2. `dotnet test` — new `ExamsListCountTests` and the four new `QuestionBankEquivalenceTests`, then the **full suite** (confirm Round 1 tests and the Round A tests still pass; the `NewController` signature change is the only edit to old test code).
3. If `List_CancelledToken_Throws` fails only there, check provider behaviour before blaming the controller.
4. On SQL Server staging: capture the actual plan + `SET STATISTICS IO, TIME ON` for `GET /api/exams` before/after (git stash the ExamsController change for the "before"); confirm it returns identical `questionCount`s for every exam against the old build (diff the two JSON responses).
5. Manual cancel check: start a slow `GET /api/question-bank/search`, abort the client, confirm the command is cancelled on the server (e.g. `sys.dm_exec_requests`) and no `LIKE-Fallback` warning is logged.
6. Evaluate each index candidate per the script header.

## 8. Next candidates (unstarted)
P1 Flutter (persisted SWR catalog cache, drop `search?pageSize=1`, local-first resume, `CancelToken`), P1 React (keyed cache + de-dup, drop the stat call), then P2/P3 — tell me which.
