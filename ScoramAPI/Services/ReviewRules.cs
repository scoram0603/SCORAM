using System.Text;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    // Pure, database-free rules for student reviews so they are trivially unit-testable.
    public static class ReviewRules
    {
        public const int MinRating = 1;
        public const int MaxRating = 5;
        public const int MinTextLength = 10;
        public const int MaxTextLength = 1000;

        // The landing page (and the public listing endpoint) only ever shows reviews rated this or higher.
        public const int MinPublicRating = 3;

        public const string AnonymousLabel = "SCORAM Student";

        /// <summary>Validates and normalises a submission. Returns the cleaned text, or an error message.</summary>
        public static (string? Text, string? Error) Validate(int? rating, string? text)
        {
            if (rating is null || rating < MinRating || rating > MaxRating)
                return (null, "Please choose a rating from 1 to 5 stars.");

            var cleaned = Normalize(text);
            if (cleaned.Length < MinTextLength)
                return (null, $"Please write at least {MinTextLength} characters about your experience.");
            if (cleaned.Length > MaxTextLength)
                return (null, $"A review can be at most {MaxTextLength} characters.");

            return (cleaned, null);
        }

        /// <summary>Trims, unifies line endings, drops control characters (keeps \n and \t), collapses
        /// runs of 3+ blank lines. Never alters the visible words.</summary>
        public static string Normalize(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var sb = new StringBuilder(text.Length);
            foreach (var ch in text.Replace("\r\n", "\n").Replace('\r', '\n'))
            {
                if (ch == '\n' || ch == '\t' || !char.IsControl(ch)) sb.Append(ch);
            }

            var result = sb.ToString().Trim();
            while (result.Contains("\n\n\n")) result = result.Replace("\n\n\n", "\n\n");
            return result;
        }

        /// <summary>The label shown publicly. Without consent: "SCORAM Student". With consent: first name +
        /// last initial ("Priya S."), or just the first name when only one name exists.</summary>
        public static string PublicDisplayName(string? fullName, bool showNamePublicly)
        {
            if (!showNamePublicly) return AnonymousLabel;

            var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return AnonymousLabel;
            if (parts.Length == 1) return parts[0];

            var initial = parts[^1][0];
            return char.IsLetter(initial) ? $"{parts[0]} {char.ToUpperInvariant(initial)}." : parts[0];
        }

        /// <summary>Single definition of "may appear on the public landing page".</summary>
        public static bool IsPubliclyEligible(ReviewStatus status, int rating, bool authorActive) =>
            status == ReviewStatus.Approved && rating >= MinPublicRating && authorActive;

        public static int ClampPageSize(int pageSize, int max = 24) => Math.Clamp(pageSize, 1, max);
        public static int ClampPage(int page) => Math.Max(1, page);
    }
}
