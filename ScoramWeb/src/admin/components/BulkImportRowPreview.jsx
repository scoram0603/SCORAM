import { Sigma, Image as ImageIcon } from "lucide-react";

// Shared between BulkImportPanel.jsx (PYP) and QuestionBankUploadWizard.jsx (PYQ) -- both bulk
// import preview tables use the same compact summary-row indicators and the same safe-JSON-parse
// guard for a row's ContentBlocks. Images themselves are editable directly in each wizard's own
// row editor (via EditImageField, same component the rest of the admin panel uses) rather than
// shown through a shared read-only component -- see each wizard's own row-editor for that.

// Compact indicators shown under a row's truncated question-text preview in the summary table --
// full KaTeX rendering doesn't truncate gracefully inside a line-clamped cell, so the summary row
// just signals "this one has math / images / rich content"; the actual rendering happens in the
// expanded row editor below it, where there's room for it.
export function RowBadges({ row }) {
  const imageCount = ["questionImageUrl", "optionAImageUrl", "optionBImageUrl", "optionCImageUrl", "optionDImageUrl", "explanationImageUrl"]
    .filter((k) => Boolean(row[k])).length;
  const hasMath = /\$/.test(row.questionText || "") || ["optionA", "optionB", "optionC", "optionD", "explanation"].some((k) => /\$/.test(row[k] || ""));
  const hasContentBlocks = Boolean(row.contentBlocksJson);

  if (imageCount === 0 && !hasMath && !hasContentBlocks) return null;

  return (
    <span className="mt-1 flex flex-wrap gap-1">
      {hasMath && (
        <span className="flex items-center gap-0.5 rounded bg-secondary-50 px-1.5 py-0.5 text-[10px] font-semibold text-secondary-600">
          <Sigma className="h-2.5 w-2.5" strokeWidth={2.5} /> math
        </span>
      )}
      {imageCount > 0 && (
        <span className="flex items-center gap-0.5 rounded bg-primary-50 px-1.5 py-0.5 text-[10px] font-semibold text-primary-600">
          <ImageIcon className="h-2.5 w-2.5" strokeWidth={2.5} /> {imageCount}
        </span>
      )}
      {hasContentBlocks && <span className="rounded bg-mint-50 px-1.5 py-0.5 text-[10px] font-semibold text-mint-600">rich content</span>}
    </span>
  );
}

// A row's contentBlocksJson is a raw JSON string from the backend -- guard against it somehow being
// malformed by the time it reaches here (it's already validated server-side, but a render crash
// over one bad row shouldn't take down the whole preview table).
export function safeParseBlocks(json) {
  try {
    const parsed = JSON.parse(json);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}
