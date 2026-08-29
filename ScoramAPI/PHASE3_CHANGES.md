# Phase 3 — API changes (Rich PYP/PYQ upload feature)

Builds on Phase 2 (see PHASE2_CHANGES.md). Adds: ZIP-package bulk upload with per-question images
and ContentBlocks for both PYP and PYQ, image staging to a temp blob folder with cleanup on
commit/expiry (per your decision), and CSV support for Question Bank (per your decision).

## New files

- `Services/BulkUploadZipService.cs` — extracts/validates a SCORAM-BULK-UPLOAD.zip package
  (`questions.json` + `images/` + optional `metadata.json`). Guards against path traversal, zip
  bombs (300 MB total uncompressed cap), and excessive entry counts (5000 max). Returns the raw
  `questions.json` text plus a filename->bytes dictionary of everything under `images/`.
- `Services/BulkImportStagingCleanupService.cs` — a `BackgroundService` that sweeps every 10
  minutes for `PendingReview` import jobs older than 35 minutes and deletes their staging folder.
  Exists because a preview that's parsed-with-images-staged but never committed needs its staged
  blobs cleaned up eventually; deliberately a plain timer sweep rather than an `IMemoryCache`
  eviction callback (see the file's own comment for why).

## Storage layer

- `IAzureBlobService` — added `DeleteByPrefixAsync(prefix)`, used to clean an entire staging
  folder in one call.
- `IFileStorageService` — added `SaveImageFromStreamAsync(stream, originalFileName, length,
  subfolder)` (for staging a ZIP entry's bytes, which have no `IFormFile` wrapper) and
  `DeleteFolderAsync(subfolder)`. `SaveFileAsync` was refactored to share a stream-based core
  (`SaveStreamAsync`) with the new method — behavior for existing callers is unchanged.

## Enum

- `ImportFileFormat` — added `Zip`, appended at the end so `ImportJob.Format`'s existing int
  values (stored with no `HasConversion`) are untouched. `QuestionBankImportJob.Format` is stored
  as a string and unaffected either way.

## Import services

- `IBulkImportService` / `BulkImportService` (PYP) — constructor now takes `IFileStorageService`.
  Added `ParseZipAsync(questionsJson, images, stagingSubfolder)`, which reuses the existing `MapRow`
  for every text field, then layers on `ContentBlocksJson` and staged image URLs. A row whose
  referenced image is missing from the ZIP or fails validation gets a message on `ImageErrors`
  (row marked invalid) rather than failing the whole batch.
- `IQuestionBankImportService` / `QuestionBankImportService` (PYQ) — same shape: constructor takes
  `IFileStorageService`, added `ParseZipAsync`, and added CSV support (`ParseCsvAsync`, using the
  same `CsvHelper` dependency already used elsewhere) so Question Bank bulk import now accepts
  CSV/Excel/JSON/ZIP, matching PYP's format list.
- **Important shared fix**: `ImportedQuestionRow`/`QuestionBankImportRow` gained an `ImageErrors`
  field. `Validate()`/`ValidateAsync()` previously did `row.Errors.Clear()` at the top of every
  pass (including every `PATCH .../rows/{rowNumber}` re-validation) — if image-staging errors had
  been added directly to `Errors`, they'd vanish the moment an admin corrected a row's text.
  `ImageErrors` is set once during staging and never cleared; `Validate`/`ValidateAsync` now seed
  `Errors` from it on every pass instead of starting empty.
- Both `Validate()`/`ValidateAsync()` now also call `ContentBlocksJsonHelper.ValidateAndSerialize`
  on `row.ContentBlocksJson` (catching `ArgumentException` into a row error) — a ZIP's
  hand-authored `contentBlocks` array gets the same validation as the single-question form.

## Controllers

- `BulkImportController` (PYP):
  - `Preview` now auto-detects `.zip` too; for ZIP, extracts via `IBulkUploadZipService` and calls
    `ParseZipAsync` instead of the stream-based `ParseAsync`. `[RequestSizeLimit]` raised from 20 MB
    to 250 MB to accommodate a ZIP full of images (previously text-only formats didn't need this).
    A `stagingId` GUID is generated up front and used as `ImportJob.Id` — so images staged during
    parsing and the job's own eventual ID always agree.
  - `Commit()` now copies each row's staged image (if any) into permanent `question-images` storage
    via the existing `CopyImageAsync`, persists `ContentBlocksJson` onto the new `Question`, and
    deletes the whole staging folder once done.
  - `Rollback()` now deletes each removed question's images — a real gap that only mattered once
    bulk-imported questions could have images at all (previously nothing to clean up).
- `QuestionBankAdminController` (PYQ):
  - Added `PreviewCsv` and `PreviewZip` actions alongside the existing `PreviewExcel`/`PreviewJson`,
    all delegating to the same private `Preview` method (now format-aware for ZIP, same pattern as
    `BulkImportController`).
  - `Commit()` — same image-copy + `ContentBlocksJson` wiring as PYP. A row that turns out to merge
    into an existing question (duplicate detection) discards any staged images/content blocks it
    carried — consistent with how other new-question-only fields already worked before this
    feature, and documented inline.
  - Staging folder cleanup added at the end of `Commit()`.
- `Program.cs` — registered `IBulkUploadZipService` and `BulkImportStagingCleanupService` (as a
  hosted service).

## What's intentionally NOT done in this phase

- No frontend changes yet (Phase 4/5): no KaTeX, no ZIP upload UI, no per-row image controls in the
  preview/edit screens. The API fully supports a ZIP upload today via a raw HTTP client/Postman,
  but there's no admin-panel button for it yet.
- `PreviewRowEditor`'s `PATCH .../rows/{rowNumber}` still only edits text fields — a row's staged
  images can't be added/replaced/removed once parsed (only the original ZIP's images ever apply).
  Extending that is frontend + a small API addition, deferred to Phase 5 alongside the actual UI.
- As with Phase 2: **no EF Core migration included** (none needed this phase — every DB-facing
  change is either an enum addition or `Format` reuse, no column shape changes). If Phase 2's
  migration hasn't been run yet, run it first: `dotnet ef migrations add
  AddContentBlocksAndBulkImportImageFields` then `dotnet ef database update`.
- **No .NET SDK in this environment** — I traced every signature change by hand (all four touched
  controllers/services were re-read in full after editing to confirm call sites match), but this
  needs a real `dotnet build` before you trust it fully.
