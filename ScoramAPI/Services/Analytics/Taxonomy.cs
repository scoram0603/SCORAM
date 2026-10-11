namespace ScoramAPI.Services.Analytics
{
    /// <summary>
    /// SMART PROGRESS -- how subject/topic NAMES (as frozen on each StudentAnswer snapshot) are
    /// normalised for grouping. A missing/blank name becomes the explicit "Unclassified" bucket
    /// instead of being dropped or guessed from question text. Matching is case-insensitive and
    /// whitespace-trimmed so "Percentage" and " percentage" are one topic.
    /// </summary>
    public static class Taxonomy
    {
        public const string Unclassified = "Unclassified";
        private const char Sep = '\u001F';

        public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        public static string SubjectName(string? subject) => Clean(subject) ?? Unclassified;

        public static string TopicName(string? topic) => Clean(topic) ?? Unclassified;

        public static bool IsUnclassified(string? name) => Clean(name) == null;

        public static string SubjectKey(string? subject) => SubjectName(subject).ToLowerInvariant();

        public static string TopicKey(string? subject, string? topic)
            => SubjectKey(subject) + Sep + TopicName(topic).ToLowerInvariant();
    }
}
