# Phase 4/5 — Frontend changes (Rich PYP/PYQ upload feature) + backend fixes

Builds on Phase 2 (data model) and Phase 3 (API) -- see PHASE2_CHANGES.md / PHASE3_CHANGES.md.
Adds: KaTeX math rendering, ZIP/CSV upload options in both bulk-import UIs, and read-only
staged-image/rich-content display in the preview/edit screens (per Phase 3's scope note: preview-row
images come only from the ZIP itself -- editing a staged image during preview isn't supported by
the API, so the UI shows them read-only with a note to fix the ZIP and re-upload).

## Backend fixes applied (from corrected files you provided)

You caught two real compile errors in the Phase 3 backend code, both now fixed in this delivery:

- `Services/BulkUploadZipService.cs` -- `BulkUploadZipContents.Images` was declared
  `{ get; } = new(...)` but assigned via object-initializer syntax in `Extract()`
  (`new BulkUploadZipContents { Images = images }`), which doesn't compile for a get-only
  auto-property. Changed to `{ get; init; }`.
- `Services/AzureBlobService.cs` -- `DeleteByPrefixAsync` called `GetBlobsAsync(prefix: prefix,
  cancellationToken: cancellationToken)` using named arguments to skip the leading `BlobTraits`/
  `BlobStates` parameters; changed to explicit positional arguments
  (`GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken)`) to avoid the
  overload-resolution issue.
- `Hubs/ChatHub.cs` and `Controllers/DirectMessagesController.cs` -- two pre-existing nullable-
  reference fixes unrelated to this feature (`Context.User!.IsInRole(...)`,
  `PreviewFor(saved) ?? string.Empty`), applied as provided.

## New frontend files

- `src/components/questions/MathText.jsx` -- exports `MathText` (splits `$...$`/`$$...$$` out of
  any string, renders via KaTeX with `throwOnError:false` + `trust:false` so a malformed expression
  shows as a visible red error instead of crashing) and `RichQuestionBody` (renders a `contentBlocks`
  array -- text/math/image/table -- falling back to plain `MathText` when absent).
- `src/admin/components/BulkImportRowPreview.jsx` -- shared between both bulk-import UIs:
  `RowBadges` (compact "∑ math" / image-count / "rich content" indicators for a truncated summary
  row), `StagedImage` (one thumbnail), `safeParseBlocks`, and `StagedContentPreview` (the full
  read-only "From ZIP" block shown in an expanded row).

## Math rendering wired in

- `src/admin/components/QuestionEditor.jsx` -- `QuestionCard` renders question/options/explanation
  through `MathText`/`RichQuestionBody`. `QuestionEditForm` gained a live preview under the question
  textarea (shows whenever the text contains `$`) and a new collapsible `ContentBlocksEditor`
  (raw JSON textarea + live `RichQuestionBody` preview) wired into `contentBlocksJson` state, sent
  through to the backend.
- `src/admin/api/adminQuestions.js` -- `createQuestion`/`updateQuestion` now send
  `ContentBlocksJson` in their form data.
- `src/components/questions/InteractiveOptions.jsx` -- options and explanation render via
  `MathText` (covers both `QuestionBankFeedCard` and `QuestionBankQuestionDetail`, which both use
  this component).
- `src/pages/QuestionBankQuestionDetail.jsx` -- main question text renders via `RichQuestionBody`.
- `src/main.jsx` -- imports KaTeX's stylesheet globally.
- `package.json` -- added `katex` (`^0.18.4`) as a real dependency.

## Bulk upload UI

- `src/admin/components/BulkImportPanel.jsx` (PYP) -- accepts `.zip` in the file picker;
  description text explains the ZIP package format (`questions.json` + `images/` +
  per-field image filename columns). Preview table rows show `RowBadges`; expanded rows show
  `StagedContentPreview` (read-only thumbnails + rich-content preview) and a live math preview
  under the question-text/explanation edit fields.
- `src/admin/pages/QuestionBankUploadWizard.jsx` (PYQ) -- format selector extended from
  `["excel","json"]` to a `FORMATS` array covering `csv`/`excel`/`json`/`zip`, with the file input's
  `accept` attribute now driven by the selected format. Same `RowBadges`/`StagedContentPreview`/live
  math preview treatment as the PYP panel.
- `src/admin/api/questionBankImport.js` -- `previewQuestionBankImport` now routes to
  `bulk/csv`, `bulk/excel`, `bulk/json`, or `bulk/zip` based on the selected format (previously only
  excel/json were reachable, matching the Phase 3 backend's new routes).

## Verification done this session

No .NET SDK or Node build tooling is available in this environment, so nothing was compiled
end-to-end -- but every touched frontend file was run through `esbuild.transformSync` (installed
via `npm install esbuild --no-save` from the public registry, then removed) as a real JSX/JS syntax
check, not just eyeballed:

```
MathText.jsx, QuestionEditor.jsx, adminQuestions.js, InteractiveOptions.jsx,
QuestionBankQuestionDetail.jsx, BulkImportPanel.jsx, BulkImportRowPreview.jsx,
QuestionBankUploadWizard.jsx, questionBankImport.js, main.jsx
```

All ten passed. Every relative import path added in this phase was also cross-checked
programmatically against the actual file tree (catches typo'd paths and wrong `../` depth), and
every named export referenced by an import (`MathText`, `RichQuestionBody`, `RowBadges`,
`StagedContentPreview`, `imgSrc`, `QuestionCard`, `QuestionEditForm`, each `questionBankImport.js`
function) was confirmed to actually exist in its source file. `katex` (`0.18.4`, matching the
pinned version) and the exact pinned `lucide-react` (`1.25.0`) were both installed with
`--no-save` to confirm `katex.renderToString` behaves as expected (including on malformed LaTeX)
and that `Sigma`/`Image` icons exist in this project's specific lucide-react version -- neither
guessed from general knowledge. None of this is a substitute for a real `npm run build` /
`vite build`, which still hasn't been run.

## What's intentionally NOT done in this phase

- `TestAttempt.jsx` (live test-taking) and `TestAttemptResult.jsx` (post-test review) still don't
  render images or math at all -- this is a pre-existing gap, not something this feature introduced,
  but per spec section 35-36 it's arguably in scope for "student rendering." Deferred to Phase 6:
  needs checking first whether the backend's test-attempt DTOs even carry image/`ContentBlocks`
  fields (not yet verified) before the frontend can render them.
- `QuestionBankFeedCard`/`QuestionBankCard` (compact list/browse cards) weren't updated to use
  `MathText` -- these are truncated summary views where full KaTeX rendering doesn't clip cleanly;
  same reasoning as the bulk-import summary-row badges, but a "math" badge for browse cards wasn't
  added.
- `PyqUploadWizard.jsx` (the single-question "add new question to a draft paper" wizard, distinct
  from `QuestionEditor.jsx`'s edit form) was not updated with a `ContentBlocksEditor` or live math
  preview -- only the edit form got this treatment.
- No image add/replace/remove during bulk-import preview (before commit) -- consistent with Phase
  3's API, which only stages images from the ZIP itself; this would need a small API addition
  (a per-row image endpoint) plus corresponding UI, deferred alongside Phase 6.
