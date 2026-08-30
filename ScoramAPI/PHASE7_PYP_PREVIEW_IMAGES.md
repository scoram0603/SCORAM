# Addendum — PYP bulk-import preview: add/replace/remove images before commit

Closes the gap noted in PHASE4_5_CHANGES.md's "What's still NOT covered": PYP bulk-import preview
rows could only ever show images read-only (from a ZIP's own images/ folder), with no way to add,
replace, or remove one before commit. **Scoped to PYP only** (`BulkImportPanel.jsx` /
`BulkImportController.cs`) per what was asked -- Question Bank (PYQ) bulk import
(`QuestionBankUploadWizard.jsx`) still shows staged images read-only, unchanged.

## Backend

- `DTOs/BulkImportDTOs.cs` -- added `BulkImportRowImagesDto` (mirrors `QuestionUpdateDto`'s image
  fields exactly: an `IFormFile?` + a `Remove*` bool per field).
- `Controllers/BulkImportController.cs` -- new endpoint:
  `POST /api/admin/bulk-import/{jobId}/rows/{rowNumber}/images`. Works for a row from **any**
  format (CSV/Excel/JSON/ZIP) -- a CSV-sourced row that never had an image can get its first one
  added here, exactly the same as a ZIP row can have its staged image replaced or removed. Images
  are saved into the same `bulk-import-staging/{jobId}` folder a ZIP upload's own images already
  use, so `Commit()`'s existing copy-to-permanent-storage-then-clean-up-staging logic (Phase 3)
  handles these identically either way -- nothing else needed to wire it in. New private helper
  `ApplyStagedImageUpdate` mirrors `QuestionsController.ApplyImageUpdate`'s exact "upload new
  before deleting old" ordering, just parameterized on subfolder. A successful call clears the
  row's `ImageErrors` wholesale (a manual admin fix supersedes whatever staging-time error, if any,
  the row had from its original ZIP upload) and re-runs `Validate()` on the full row list, same as
  the existing text-only `UpdateRow` endpoint.
- No migration needed -- this reuses existing storage methods (`SaveImageAsync`,
  `DeleteImageAsync`) and existing DB fields, no schema change.

## Frontend

- `src/admin/api/bulkImport.js` -- new `updateRowImages(token, jobId, rowNumber, images,
  removeImages)`, mirroring `adminQuestions.js`'s multipart `FormData` building convention.
- `src/admin/components/BulkImportPanel.jsx` -- `PreviewRowEditor` now renders an `EditImageField`
  (the same Add/Preview/Replace/Remove component already used everywhere else in the admin panel)
  for the question image, each of the four option images, and the explanation image. `handleSave`
  now does two sequential calls on submit: `updatePreviewRow` (text fields) then, only if any image
  was touched, `updateRowImages` -- both operate on the same server-side cached row, so they compose
  correctly. Any `ContentBlocksJson` a row already has (ZIP upload only) is still shown read-only,
  unchanged -- only the six plain image fields became editable here, not the rich-content sequence.

## Verification done this session

Same rigor as every prior phase: `esbuild.transformSync` (via `npm install esbuild --no-save`,
removed after) confirmed valid JSX/JS syntax on every touched file
(`BulkImportPanel.jsx`, `bulkImport.js`, `EditImageField.jsx`); every relative import path and every
referenced export (`updateRowImages`, `EditImageField`'s default export, `RowBadges`,
`safeParseBlocks`) was programmatically confirmed against the actual file tree rather than assumed.
The backend endpoint and its DTO were re-read in full after editing to confirm the field names in
`BulkImportRowImagesDto` line up exactly with what `updateRowImages` sends, and that
`IFormFile`/`DeleteImageAsync`'s nullable-argument handling are both used the same way
`QuestionsController.ApplyImageUpdate` already does. No .NET SDK or Node build was run end-to-end.
