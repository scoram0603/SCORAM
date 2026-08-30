# Phase 6 — Student test-taking rendering (images + math during live tests and review)

Closes the gap flagged at the end of Phase 4/5: `TestAttempt.jsx`/`TestAttemptResult.jsx` didn't
render images or math at all. Turned out `TestAttempt.jsx` is dead code -- explicitly removed from
routing (see `App.jsx`'s own comment: "the legacy TestAttempt.jsx" was replaced by `TestRunner.jsx`
to fix a bug where two different components could handle the same attempt). The real live
components are `TestRunner.jsx` (live test-taking, route `tests/attempt/:attemptId`) and
`TestAttemptResult.jsx` (post-submit review, route `tests/result/:attemptId`) -- both updated here.
`TestAttempt.jsx` itself was left untouched.

## Root cause (backend)

`StudentAnswer` snapshots a question's content once at attempt-start (`QuestionTextSnapshot`,
`OptionASnapshot`-`OptionDSnapshot`, `CorrectOptionSnapshot`, `ExplanationSnapshot`,
`SubjectSnapshot`, `TopicSnapshot`) so a later admin edit to the live question can never
retroactively change what a past attempt showed or how it was scored. This snapshot never included
images or `ContentBlocksJson` -- so even with Phase 2/3's backend support and Phase 4/5's frontend
rendering, a test-taking attempt genuinely had no image/rich-content data to render. Confirmed by
reading `TestAttemptDTOs.cs`, `TestAttemptService.cs`, and `TestAttemptsController.cs` before
touching anything.

The fix's shared-mapping methods (`ToStartResponse`/`ToResultDto` in `TestAttemptsController.cs`)
are used by every test type -- Mock, Practice, Quiz, and PYP Paper practice all funnel through the
same two methods (confirmed via grep across `MockTestsController`, `PracticeTestsController`,
`QuizzesController`, `QuizChallengesController`, `StudentPapersController`) -- so this one fix
covers the entire student test-taking surface, not just mock tests.

## Backend changes

- `Models/StudentAnswer.cs` -- added `QuestionImageUrlSnapshot`, `OptionAImageUrlSnapshot`,
  `OptionBImageUrlSnapshot`, `OptionCImageUrlSnapshot`, `OptionDImageUrlSnapshot`,
  `ExplanationImageUrlSnapshot`, `ContentBlocksJsonSnapshot` -- same frozen-at-attempt-start
  contract as every existing `*Snapshot` field. No explicit Fluent API config needed (plain nullable
  strings, same as `ExplanationSnapshot`).
- `Services/TestAttemptService.cs` -- `BuildSnapshotAnswersAsync` now copies these fields from both
  the `Question` and `QuestionBankQuestion` branches when building a fresh attempt.
- `DTOs/TestAttemptDTOs.cs` -- added the same image fields plus `ContentBlocks` (parsed via the
  existing `ContentBlocksJsonHelper.Parse`) to `TestAttemptQuestionDto` (live attempt) and
  `TestAnswerReviewDto` (post-submit review).
- `Controllers/TestAttemptsController.cs` -- `ToStartResponse` and `ToResultDto` populate the new
  DTO fields from the snapshot.
- **Migration**: another additive-only column change, no explicit Fluent config -- run
  `dotnet ef migrations add AddImageAndContentBlocksSnapshotsToStudentAnswer` locally (alongside
  Phase 2's still-outstanding migration, if that hasn't been run yet) then `dotnet ef database
  update`.

## Frontend changes

- `src/components/tests/TestRunner.jsx` -- question text renders via `RichQuestionBody` (handles
  `ContentBlocks`, falls back to plain `MathText` math parsing), with a question image above the
  options if present. Each option renders via `MathText` with its own image below the option text
  if present.
- `src/pages/TestAttemptResult.jsx` -- `QuestionReviewCard`'s expanded section now shows the full
  question text (via `RichQuestionBody`) and image at the top -- previously the expanded review
  jumped straight to the options grid with no restatement of the question itself, since the
  collapsed header's truncated text was the only place it appeared. Options render via `MathText`
  with per-option images; the explanation renders via `MathText` with its own image.
- Collapsed/summary rows (the truncated one-line question preview in both components) were left as
  plain text -- same reasoning as the Phase 4/5 bulk-import summary rows: full KaTeX rendering
  doesn't clip gracefully inside a `truncate`/single-line container, and the full rendering happens
  once expanded/on the main attempt screen anyway.

## Verification done this session

Same rigor as Phase 4/5: both touched files passed a real `esbuild.transformSync` JSX syntax check
(package installed via `npm install esbuild --no-save`, removed after), and every import path was
programmatically checked against the actual file tree, with every referenced export
(`MathText`, `RichQuestionBody`, `API_BASE_URL`) confirmed to exist in its source file. The backend
mapping changes were re-read in full after editing to confirm the new DTO fields line up with the
new model fields and `ContentBlocksJsonHelper` is reachable from `TestAttemptsController.cs`
(`ScoramAPI.DTOs` was already `using`'d there). No .NET SDK or Node build was run end-to-end.

## What's still NOT covered

- `TestAttempt.jsx` (confirmed dead code, unrouted) was not touched.
- `QuestionBankFeedCard`/`QuestionBankCard` (compact browse cards) still don't use `MathText` --
  unchanged from the Phase 4/5 deferral.
- The `PreExamInstructions.jsx` page (shown before `TestRunner.jsx`) wasn't checked -- if it
  previews any question content, it wasn't audited for image/math rendering in this pass.
