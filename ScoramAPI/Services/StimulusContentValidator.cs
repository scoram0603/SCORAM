using System.Text.Json;
using ScoramAPI.DTOs;

namespace ScoramAPI.Services
{
    // Extra checks for SharedStimulus / PaperInstruction content on top of
    // ContentBlocksJsonHelper.ValidateAndSerialize (which only checks type + non-empty).
    // Throws ArgumentException with a user-facing message, same convention as SaveImageAsync.
    public static class StimulusContentValidator
    {
        public const int MaxBlocks = 100;
        public const int MaxBlockLength = 50_000;

        public static string? ValidateAndSerialize(List<ContentBlockDto>? blocks, bool requireContent)
        {
            if (blocks == null || blocks.Count == 0)
            {
                if (requireContent) throw new ArgumentException("Add at least one content block (text, image or table).");
                return null;
            }
            if (blocks.Count > MaxBlocks) throw new ArgumentException($"Too many content blocks (max {MaxBlocks}).");

            foreach (var b in blocks)
            {
                var content = b.Content ?? string.Empty;
                if (content.Length > MaxBlockLength) throw new ArgumentException("A content block is too long.");

                switch ((b.Type ?? string.Empty).ToLowerInvariant())
                {
                    case "image":
                        // Only our own stored uploads -- never an external/absolute/scheme URL or a traversal path.
                        if (!content.StartsWith("/uploads/", StringComparison.Ordinal)
                            || content.Contains("..") || content.Contains('\\') || content.Contains("://") || content.Contains('?') || content.Contains('#'))
                            throw new ArgumentException("Image blocks must use an uploaded image (upload it first).");
                        break;
                    case "table":
                        try { using var _ = JsonDocument.Parse(content); }
                        catch (JsonException) { throw new ArgumentException("A table block must contain valid table data."); }
                        break;
                }
            }

            var json = JsonSerializer.Serialize(blocks, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return ContentBlocksJsonHelper.ValidateAndSerialize(json); // unknown types / empty content
        }
    }
}
