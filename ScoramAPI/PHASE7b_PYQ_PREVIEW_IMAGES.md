# Addendum 2 — Question Bank (PYQ) bulk-import preview: add/replace/remove images before commit

Same feature as PHASE7_PYP_PREVIEW_IMAGES.md, now extended to Question Bank (PYQ) bulk import
(`QuestionBankUploadWizard.jsx` / `QuestionBankAdminController.cs`) -- exact mirror of the PYP
implementation, same reasoning throughout.

## Backend

- `DTOs/QuestionBankDTOs.cs` -- added `QuestionBankRowImagesDto`, identical shape to PYP's
  `BulkImportRowImagesDto`. Kept as its own copy rather than shared, matching this controller's
  existing preference for small controller-local DTOs (see `ApplyImageUpdate`'s own comment making
  the same call for the single-question `{id}/images` endpoint).
- `Controllers/QuestionBankAdminController.cs` -- new endpoint:
  `POST /api/admin/question-bank/bulk/{jobId}/rows/{rowNumber}/images`, placed right after the
  existing text-only `UpdateRow` PATCH. Exact mirror of `BulkImportController.UpdateRowImages` --
  works for a row from any format (CSV/Excel/JSON/ZIP), saves into the same
  `bulk-import-staging/{jobId}` folder a ZIP upload's own images use, clears the row's
  `ImageErrors` on a successful call, and re-runs `ValidateAsync(rows, _db)` (same call shape the
  existing `UpdateRow` already uses -- no `defaultLanguage` param, since that's only relevant at
  initial parse time). New private helper `ApplyStagedImageUpdate` sits right next to the existing
  `ApplyImageUpdate`, same "upload new before deleting old" ordering, parameterized on subfolder
  instead of the hardcoded `"question-images"` the existing method always uses.
- No migration needed -- same as PYP's version, this reuses existing storage methods and DB fields.

## Frontend

- `src/admin/api/questionBankImport.js` -- new `updateRowImages(...)`, byte-for-byte the same
  `FormData`-building shape as `bulkImport.js`'s own version (PYP), just pointed at the Question
  Bank route.
- `src/admin/pages/QuestionBankUploadWizard.jsx` -- `QuestionBankRowEditor` now renders an
  `EditImageField` for the question image, each of the four option images, and the explanation
  image -- exact same layout PYP's `PreviewRowEditor` uses. `handleSave` does the same two
  sequential calls (`updatePreviewRow` then, only if touched, `updateRowImages`). Any
  `ContentBlocksJson` a row already has is still shown read-only via `RichQuestionBody`, unchanged.
- `src/admin/components/BulkImportRowPreview.jsx` -- **cleanup**: `StagedImage` and
  `StagedContentPreview` are now dead code (both wizards render images as editable fields directly
  instead), so they were removed from this shared file. Only `RowBadges` and `safeParseBlocks`
  remain, still used by both wizards. Confirmed via grep that neither removed export is referenced
  anywhere in the codebase before deleting them.

## Verification done this session

Same rigor as every prior phase: `esbuild.transformSync` (via `npm install esbuild --no-save`,
removed after) confirmed valid syntax on every touched file, including re-checking
`BulkImportPanel.jsx` (PYP) after the shared `BulkImportRowPreview.jsx` file was rewritten, to
confirm removing the two dead exports didn't break its own still-valid imports (`RowBadges`,
`safeParseBlocks`). Every import path was programmatically re-verified against the actual file
tree, and a grep confirmed zero remaining references to the removed `StagedContentPreview`/
`StagedImage` anywhere before deleting them. The backend endpoint, DTO, and helper were re-read in
full after editing to confirm exact placement (inserted once, in the right place, not duplicated)
and that field names line up between `QuestionBankRowImagesDto` and what `updateRowImages` sends.
No .NET SDK or Node build was run end-to-end.
