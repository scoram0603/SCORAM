# Phase 2 — Data Model changes (Rich PYP/PYQ upload feature)

Scope: only the schema/DTO shape needed for ContentBlocks and for bulk-import rows to eventually
carry images. No ZIP parsing, no staging/upload logic, no CSV support for Question Bank yet —
those are Phase 3 (API).

## New file

- `DTOs/ContentBlockDto.cs` — `ContentBlockDto { Type, Content }` plus `ContentBlocksJsonHelper`
  (`Parse`, `ValidateAndSerialize`) shared by both question types.

## Model changes

- `Models/QuestionModels.cs` — `Question.ContentBlocksJson` (nullable `string`).
- `Models/QuestionBankModels.cs` — `QuestionBankQuestion.ContentBlocksJson` (nullable `string`).

Both are plain `nvarchar(max)` columns (no explicit Fluent config needed — same as `Explanation`).
Existing rows get `NULL`; nothing renders differently until a question actually sets this.

## DTO changes

- `DTOs/QuestionDTOs.cs` — `ContentBlocksJson` added to `QuestionCreateDto`/`QuestionUpdateDto`
  (plain form-text field, sits alongside the existing `IFormFile` image fields); `ContentBlocks`
  (parsed list) added to `QuestionResponseDto`.
- `DTOs/QuestionBankDTOs.cs` — same additions to `QuestionBankQuestionCreateDto`/`UpdateDto`/
  `ResponseDto`. Also added to `QuestionBankImportRow`: staged image URL fields
  (`QuestionImageUrl`, `OptionA-DImageUrl`, `ExplanationImageUrl`) and `ContentBlocksJson` — **fields
  only, not wired up yet** (see Phase 3).
- `DTOs/BulkImportDTOs.cs` — same staged image URL fields + `ContentBlocksJson` added to
  `ImportedQuestionRow` (PYP), also unwired for now.

## Controller/service changes

- `Controllers/QuestionsController.cs` — `Create`/`Update` validate + persist `ContentBlocksJson`
  via `ContentBlocksJsonHelper.ValidateAndSerialize`, inside the same try/catch that already
  handles image-validation `ArgumentException`s. `MapToResponseDto`/`MapToDetailDto` now populate
  `ContentBlocks`.
- `Controllers/QuestionBankAdminController.cs` — same pattern for Create/Update/`ToAdminDto`.
- `Controllers/QuestionBankController.cs` — `MapToResponseDto` (student-facing) now populates
  `ContentBlocks`.
- `Services/QuestionBankMirrorService.cs` — `MirrorFromPyqAsync`/`SyncMirrorAsync` now carry
  `ContentBlocksJson` across from a PYQ `Question` to its Question Bank mirror. Note: any image URLs
  *embedded inside* a ContentBlocksJson blob are **not** re-copied to a mirror-specific file the way
  `QuestionImageUrl` etc. are — the mirror's content blocks point at the same images as the source
  until an admin edits the mirror directly. Documented in-line; revisit if that turns out to matter
  in practice.

## What's intentionally NOT done in this phase

- No EF Core migration is included. **Run this yourself after applying these changes:**
  ```
  dotnet ef migrations add AddContentBlocksAndBulkImportImageFields
  dotnet ef database update
  ```
  (I don't have the .NET SDK available in this environment, so I can't run `dotnet ef` or compile
  the project — please build/restore locally before trusting these edits fully.)
- Bulk-import rows now have image/content-block fields, but `BulkImportController.Commit()` and
  `QuestionBankAdminController`'s commit path don't read them yet — that's Phase 3.
- No ZIP upload endpoint, no image staging (temp blob folder + cleanup-on-expiry), no CSV support
  for Question Bank, no KaTeX on the frontend yet.
