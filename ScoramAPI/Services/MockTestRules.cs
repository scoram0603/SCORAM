using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    /// <summary>
    /// Validation rules for the two Mock Test settings added with sectional timing and result release,
    /// shared by the manual admin form (MockTestsController / MockTestsAdminController) and the bulk upload
    /// (TestBulkImportService) so a test behaves the same however it was created.
    /// </summary>
    public static class MockTestRules
    {
        /// <summary>Accepts the enum names plus a few friendly spellings ("delay", "date", "after end", ...).
        /// Blank means Instant.</summary>
        public static bool TryParseReleaseMode(string? raw, out ResultReleaseMode mode)
        {
            mode = ResultReleaseMode.Instant;
            if (string.IsNullOrWhiteSpace(raw)) return true;

            var key = new string(raw.Where(char.IsLetter).ToArray()).ToLowerInvariant();
            switch (key)
            {
                case "instant": case "instantly": case "immediate": case "immediately": case "now":
                    mode = ResultReleaseMode.Instant; return true;
                case "afterdelay": case "delay": case "delayed": case "after": case "aftertime":
                    mode = ResultReleaseMode.AfterDelay; return true;
                case "ondate": case "date": case "ondatetime": case "specificdate": case "fixeddate":
                    mode = ResultReleaseMode.OnDate; return true;
                case "afterwindowends": case "windowend": case "windowends": case "afterend": case "afterwindow": case "aftertestends": case "afterexam":
                    mode = ResultReleaseMode.AfterWindowEnds; return true;
                default:
                    return false;
            }
        }

        /// <summary>Null when the combination is valid, otherwise a message an admin can act on.</summary>
        public static string? ValidateResultRelease(ResultReleaseMode mode, int? delayMinutes, DateTime? releaseAtUtc, DateTime? endAtUtc)
        {
            switch (mode)
            {
                case ResultReleaseMode.AfterDelay:
                    if (delayMinutes == null || delayMinutes < 0)
                        return "Result release \"after a delay\" needs the number of minutes (0 or more).";
                    if (delayMinutes > 60 * 24 * 365)
                        return "Result delay is too large (maximum is one year).";
                    break;
                case ResultReleaseMode.OnDate:
                    if (releaseAtUtc == null)
                        return "Result release \"on a date\" needs the date and time the result should open.";
                    break;
                case ResultReleaseMode.AfterWindowEnds:
                    if (endAtUtc == null)
                        return "Result release \"after the test window ends\" needs the test's End time to be set.";
                    break;
            }
            return null;
        }

        /// <summary>Validates a test's sections. isSectional is true only when there are sections AND every one has
        /// a duration -- then totalMinutes is their sum. Sections without any duration are allowed (labels / result
        /// breakdown only); sections where only some have a duration are rejected, because the student
        /// couldn't tell which sections are timed.</summary>
        public static string? ValidateSections(IReadOnlyList<(string Name, int? DurationMinutes)> sections, out bool isSectional, out int totalMinutes)
        {
            isSectional = false;
            totalMinutes = 0;
            if (sections.Count == 0) return null;
            if (sections.Count > 20) return "A test can have at most 20 sections.";

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, _) in sections)
            {
                if (string.IsNullOrWhiteSpace(name)) return "Every section needs a name.";
                if (name.Trim().Length > 100) return $"Section name \"{name.Trim()}\" is too long (maximum 100 characters).";
                if (!seen.Add(name.Trim())) return $"Section \"{name.Trim()}\" is listed more than once.";
            }

            var timed = sections.Count(s => s.DurationMinutes.HasValue);
            if (timed == 0) return null;
            if (timed != sections.Count) return "Either every section has a time limit or none of them does.";
            if (sections.Any(s => s.DurationMinutes!.Value < 1 || s.DurationMinutes.Value > 600))
                return "A section's time limit must be between 1 and 600 minutes.";

            isSectional = true;
            totalMinutes = sections.Sum(s => s.DurationMinutes!.Value);
            return null;
        }

        /// <summary>The section a MockTestQuestion belongs to, as the name-and-timing summary students/admins see.</summary>
        public static List<MockTestSectionSummaryDtoShape> SummarizeSections(MockTest test) =>
            test.Sections
                .OrderBy(s => s.SectionOrder)
                .Select(s => new MockTestSectionSummaryDtoShape(
                    s.SectionOrder, s.Name, s.DurationMinutes, test.MockTestQuestions.Count(q => q.SectionId == s.Id)))
                .ToList();
    }

    /// <summary>Plain tuple-like carrier so Services doesn't reference the DTO namespace.</summary>
    public record MockTestSectionSummaryDtoShape(int Order, string Name, int? DurationMinutes, int QuestionCount);
}
