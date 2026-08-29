import { Sigma, Image as ImageIcon } from "lucide-react";
import { imgSrc } from "./EditImageField";
import { RichQuestionBody } from "../../components/questions/MathText";

// Shared between BulkImportPanel.jsx (PYP) and QuestionBankUploadWizard.jsx (PYQ) -- both bulk
// import preview tables show the same three things for a row that came from a ZIP upload: compact
// summary-row badges, a staged-image thumbnail, and a read-only rich-content preview.

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

export function StagedImage({ label, url }) {
  return (
    <span className="flex flex-col items-center gap-1">
      <img src={imgSrc(url)} alt={label} className="h-16 w-16 rounded border border-primary-100 object-cover" />
      <span className="text-[10px] text-ink-400">{label}</span>
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

// The read-only "From ZIP" block shown inside an expanded preview row -- every staged image field
// plus any ContentBlocks, none of it editable here (only the plain text fields below are, via
// PATCH .../rows/{rowNumber} -- see each wizard's own row-editor comment for why). Renders nothing
// if the row has none of this (a CSV/Excel/JSON-sourced row never does).
export function StagedContentPreview({ row }) {
  const hasAnyImage = row.questionImageUrl || row.optionAImageUrl || row.optionBImageUrl || row.optionCImageUrl || row.optionDImageUrl || row.explanationImageUrl;
  if (!hasAnyImage && !row.contentBlocksJson) return null;

  return (
    <div className="rounded-lg bg-primary-50/40 p-2">
      <p className="mb-1 text-[10px] font-bold uppercase tracking-wide text-primary-500">From ZIP (read-only here)</p>
      {hasAnyImage && (
        <div className="flex flex-wrap gap-2">
          {row.questionImageUrl && <StagedImage label="Question" url={row.questionImageUrl} />}
          {row.optionAImageUrl && <StagedImage label="Option A" url={row.optionAImageUrl} />}
          {row.optionBImageUrl && <StagedImage label="Option B" url={row.optionBImageUrl} />}
          {row.optionCImageUrl && <StagedImage label="Option C" url={row.optionCImageUrl} />}
          {row.optionDImageUrl && <StagedImage label="Option D" url={row.optionDImageUrl} />}
          {row.explanationImageUrl && <StagedImage label="Explanation" url={row.explanationImageUrl} />}
        </div>
      )}
      {row.contentBlocksJson && (
        <div className={hasAnyImage ? "mt-2 rounded bg-white p-2" : "rounded bg-white p-2"}>
          <RichQuestionBody contentBlocks={safeParseBlocks(row.contentBlocksJson)} className="text-xs text-ink-700" />
        </div>
      )}
    </div>
  );
}
