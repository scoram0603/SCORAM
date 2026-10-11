# SCORAM Smart Progress — metric definitions and API contract

Single source of truth for the React website, the Flutter app and Study Partner. All numbers come from
`Services/Analytics/*` (database-free maths in `AnalyticsCalculator`, thresholds in `AnalyticsOptions`,
bound from the `Analytics` section of appsettings). Clients must NOT recompute any of them.
Response shapes: `DTOs/SmartProgressDTOs.cs` (JSON is camelCase, enums are strings).

## Metric semantics

| Topic | Rule |
|---|---|
| Completed attempt | `StudentTestResult.Status != InProgress` (same as the existing Progress page / Study Partner). In-progress and abandoned attempts never count. |
| Assessment types | PYP (`PreviousYearPaper`), Practice, Mock, Quiz — one engine, `kind` filter narrows. |
| Answered / attempted | A question with a selected option. Skipped = no option. |
| Accuracy % | correct ÷ answered, 1 dp. **null (never 0)** when nothing answered. |
| Score % | score ÷ total questions (existing result-screen definition); may be negative with negative marking. |
| Marks | +1 per correct, −`NegativeMarkingRatio` per wrong, 0 skipped (snapshotted on the attempt, mirrors `GradeAttempt`). Max score = total questions × 1. Marking is NOT hardcoded to any exam. |
| Change | Always **percentage points** (`…ChangePoints`). Relative % is a separate calculator function and is not exposed. |
| Comparison windows | recent `ComparisonWindowDays` vs the same length before. A window with fewer than `MinQuestionsPerComparisonWindow` answered questions ⇒ `NotEnoughData` ("Not enough comparable data yet."). |
| Comparable attempt | same test kind **and** same source (same MockTest / Paper / Template / Quiz) or, for ad-hoc Practice, same exam; scores are compared only if the negative-marking ratio is equal, accuracy always. Otherwise `Comparable=false` with the reason in `Basis`. |
| Subject / topic | From the answer's frozen `SubjectSnapshot` / `TopicSnapshot` (so editing a question later never rewrites history). Blank ⇒ explicit **Unclassified** bucket; nothing is inferred from question text. Subjects absent from an assessment are absent from its analytics (never shown as 0%). |
| Weak topic detector | `<MinAttemptsForAnalysis` (5) answered ⇒ `InsufficientData`; 5–19 ⇒ preliminary; ≥20 ⇒ higher confidence. Accuracy `<50` NeedsAttention, `50–<70` PracticeRecommended, `≥70` PerformingWell (or NeedsRetesting if not practised for `RetestAfterDays`). A ≥`ImprovementDeltaPoints` gain between windows (still <70) ⇒ Improving. All thresholds configurable. |
| Time analysis | Per-question seconds come from `StudentAnswer.TimeSpentSeconds` (client-reported running total, validated and clamped server-side). Analysed only if ≥`MinTimingCoveragePercent` of answered questions carry a timing; otherwise `Time.Available=false`. Pace labels are relative to the assessment's **own** allotted time, never an invented benchmark. |
| Mistake Book | Derived from the student's own `StudentAnswer`s (nothing copied). Status: New → Learning → Corrected (answered correctly in a later assessment, or `CorrectedAfterConsecutiveCorrect` correct revisions in a row). A fresh mistake resets it. Dismiss is reversible. |
| Revision attempts | Stored in `RevisionAttempt`/`MistakeRevisionItem`, never in `StudentTestResult`/`StudentAnswer`: cannot alter or inflate an original score, rank, XP or history. POST is idempotent via `clientRequestId`. |
| Study plan | Rule-based, generated lazily per IST day, tasks keyed deterministically (idempotent). Progress is derived from real activity (revision answers, answered questions, submitted assessments) — opening a page never completes a task. |
| Day boundary | IST calendar day (same as the streak). |

## Endpoints (all `Authorize(Roles="Student")`; identity comes from the JWT, never from a client id)

Common query filters: `examId` (guid), `kind` (`Mock|Practice|PreviousYearPaper|Quiz`), `days` (1–365), `myExamsOnly` (bool).

| Method & route | Returns |
|---|---|
| `GET /api/progress/overview` | `ProgressOverviewDto` |
| `GET /api/progress/subjects?includeTopics=true` | `SubjectsResponseDto` |
| `GET /api/progress/topics/detail?subject=&topic=` | `TopicDetailDto` (use `Unclassified` for the unclassified bucket) |
| `GET /api/progress/trends` | `TrendsDto` |
| `GET /api/progress/assessments/{attemptId}/analytics` | `AttemptAnalyticsDto` (subjects→topics, time, improvement, recommendations) — existing `GET /api/tests/attempts/{id}` result (questions review) is unchanged |
| `PATCH /api/tests/attempts/{attemptId}/timings` | body `TestTimingsDto` → `TestTimingsResponseDto` (only while InProgress; running totals) |
| `GET /api/revision/mistakes` | `MistakeBookDto`; query: filters above + `subject,topic,search,status(Open|New|Learning|Corrected|Dismissed|All),type(Incorrect|Skipped|Repeated|Marked|All),dueOnly,sort(recent|oldest|mostWrong),page,pageSize(≤50)` |
| `GET /api/revision/mistakes/summary` | `RevisionSummaryDto` |
| `GET /api/revision/mistakes/detail?ref=` | `MistakeDetailDto` |
| `GET /api/revision/session?count=&subject=&topic=&dueOnly=` | `RevisionSessionDto` (no answer key) |
| `POST /api/revision/answers` | body `RevisionAnswerRequestDto` → `RevisionAnswerResultDto` |
| `PUT /api/revision/mistakes/dismiss?ref=` | body `MistakeDismissDto` → 204 |
| `GET /api/study-plan/today` | `StudyPlanDto` |
| `GET /api/study-plan/history?days=14` | `StudyPlanHistoryDto` |
| `PATCH /api/study-plan/tasks/{id}` | body `StudyTaskUpdateDto` → `StudyTaskDto` (dismiss / restore) |

Errors: `{ "message": "…" }` with 400 (bad filter), 401, 404 (also for another student's id — no IDOR leak), 409.

## Rollout / manual steps

1. **Create the migration locally** (it could not be generated in the authoring sandbox, and a hand-written one would fall out of sync with `ScoramDbContextModelSnapshot`):
   `dotnet ef migrations add AddSmartProgress` then review the generated file. It must contain ONLY additive changes:
   tables `RevisionAttempts`, `MistakeRevisionItems`, `StudyPlanTasks` (+ their indexes), nullable `StudentAnswers.TimeSpentSeconds`, and `PrivacySettings.ShareStudyPlan` (bit, default 0). If you see any DROP/rename, stop.
   Run `dotnet ef database update` on a dev/staging database first. Old rows are untouched (`TimeSpentSeconds` NULL = "timing not recorded").
2. Optional `Analytics` section in appsettings overrides the defaults in `AnalyticsOptions`.
3. Existing endpoints and DTOs are unchanged. New: `PATCH /api/tests/attempts/{id}/timings`, `GET /api/study-partners/{userId}/study-plan` (opt-in via `shareStudyPlan` on the existing `PUT /api/study-partners/privacy`).
4. Tests: `ScoramAPI.Tests/AnalyticsCalculatorTests.cs` and `ProgressServiceTests.cs` — add them to your existing test project (needs `Microsoft.EntityFrameworkCore.InMemory` 8.0.8 and xUnit) and run `dotnet test`.

## Known limitations

* Topic-level time pace advice only appears on the per-assessment result (it needs that assessment's own time limit); the cross-history topic view shows average seconds without a fast/slow verdict.
* Quizzes have no exam, so any exam filter (including My Exams only) excludes them.
* Attempts with no per-question timings (everything before this release) show "timing not recorded".
* The revision-attempt topic stats are not shown when an exam or test-kind filter is active (a revision answer isn't tied to either).
* Study Partner sharing exposes only weekly plan completion and active days, only for accepted partners who opted in.
