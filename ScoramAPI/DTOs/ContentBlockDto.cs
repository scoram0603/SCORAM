using System.Text.Json;

namespace ScoramAPI.DTOs
{
    // One piece of a question's optional rich-content sequence -- lets a question mix plain text,
    // LaTeX math, and inline images/tables in order (spec: "Rich Question Content"). Deliberately
    // NOT a replacement for QuestionText/OptionX/Explanation -- those stay authoritative and always
    // render; ContentBlocks is an optional, additive sequence a question can carry on top for
    // Math/Reasoning content that doesn't fit "one block of text + one image" (e.g. "Given: <math>,
    // <image>, then <math> again"). A question with no ContentBlocks renders exactly as it did before
    // this feature existed.
    public class ContentBlockDto
    {
        // "text" | "math" | "image" | "table" -- kept as an open string rather than an enum so a
        // future type doesn't need a migration or an API version bump.
        public string Type { get; set; } = string.Empty;

        // For "text"/"math": the literal text/LaTeX source. For "image": a relative "/uploads/..."
        // URL (same shape as QuestionImageUrl etc.), resolved server-side -- never a bare filename,
        // so the frontend can render it the same way as any other image field. For "table": a small
        // JSON-encoded 2D array, left as a raw string here (the renderer owns interpreting it) so
        // this DTO never needs to change shape as table formatting needs grow.
        public string Content { get; set; } = string.Empty;
    }

    // Shared (de)serialization for the ContentBlocksJson column on both Question and
    // QuestionBankQuestion -- keeps the "how" (System.Text.Json, tolerant of null/empty/garbage) in
    // one place instead of duplicated across every controller that reads or writes it.
    public static class ContentBlocksJsonHelper
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        private static readonly HashSet<string> ValidTypes =
            new(StringComparer.OrdinalIgnoreCase) { "text", "math", "image", "table" };

        // Never throws -- a question with malformed/legacy ContentBlocksJson should still load and
        // display (via its normal QuestionText/Options/Explanation) rather than 500. Returns an
        // empty list on null, empty, or unparsable input.
        public static List<ContentBlockDto> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<ContentBlockDto>();
            try
            {
                return JsonSerializer.Deserialize<List<ContentBlockDto>>(json, Options) ?? new List<ContentBlockDto>();
            }
            catch (JsonException)
            {
                return new List<ContentBlockDto>();
            }
        }

        // Serializes for storage. An empty/null list stores as null (not "[]") so existing rows and
        // rows that never use this feature stay indistinguishable from before this column existed.
        private static string? Serialize(List<ContentBlockDto>? blocks) =>
            blocks == null || blocks.Count == 0 ? null : JsonSerializer.Serialize(blocks, Options);

        // Validates a raw ContentBlocksJson string coming from a create/update request before it's
        // stored -- rejects unknown block types and empty content so bad admin input fails fast with
        // a clear message instead of silently storing something the renderer then has to guard
        // against. Returns null (nothing to store) or the re-serialized, validated JSON. Throws
        // ArgumentException with a user-facing message on invalid input, same convention as
        // IFileStorageService.SaveImageAsync so callers can fold it into the same try/catch.
        public static string? ValidateAndSerialize(string? rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson)) return null;

            List<ContentBlockDto>? blocks;
            try
            {
                blocks = JsonSerializer.Deserialize<List<ContentBlockDto>>(rawJson, Options);
            }
            catch (JsonException)
            {
                throw new ArgumentException("contentBlocks must be a JSON array of { type, content } objects.");
            }

            if (blocks == null || blocks.Count == 0) return null;

            foreach (var block in blocks)
            {
                if (!ValidTypes.Contains(block.Type))
                    throw new ArgumentException($"Unsupported content block type '{block.Type}'. Use text, math, image, or table.");
                if (string.IsNullOrWhiteSpace(block.Content))
                    throw new ArgumentException($"A '{block.Type}' content block can't have empty content.");
            }

            return Serialize(blocks);
        }
    }
}
