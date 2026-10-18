using System.Text.Json;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    /// <summary>One section of a sectional attempt's frozen plan.</summary>
    public record SectionPlanItem(int Order, string Name, int DurationMinutes);

    /// <summary>
    /// SECTION-WISE TIMING -- the server-side rules for a sectional Mock Test attempt (Maths 25 min,
    /// Reasoning 25 min, ...). The student works through the sections in order; each has its own clock; a
    /// finished section is locked for good. The server -- not the browser -- decides which section accepts
    /// answers, using StudentTestResult.CurrentSectionOrder / CurrentSectionStartedAt.
    ///
    /// Timing is "lazy": nothing runs in the background. Whenever the attempt is touched (resume, save an
    /// answer, advance, submit) <see cref="Normalize"/> moves the student on past any section whose clock has
    /// run out. A section that expires while the student is away therefore ends at its real deadline, and
    /// the next section's clock is counted from that deadline (the clock of a real exam keeps running).
    /// Finishing a section early starts the next one immediately, so the whole attempt can only ever take
    /// LESS than the sum of the section durations, never more.
    /// </summary>
    public static class SectionedAttempts
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        // A save that is still in flight when a section's clock hits zero (network latency, the 500 ms
        // debounce on the client) is still accepted for this long.
        public static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);

        public static string Serialize(IEnumerable<SectionPlanItem> plan) => JsonSerializer.Serialize(plan.OrderBy(p => p.Order).ToList(), JsonOptions);

        public static List<SectionPlanItem> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<SectionPlanItem>();
            try
            {
                return (JsonSerializer.Deserialize<List<SectionPlanItem>>(json, JsonOptions) ?? new List<SectionPlanItem>())
                    .OrderBy(p => p.Order).ToList();
            }
            catch (JsonException)
            {
                return new List<SectionPlanItem>();
            }
        }

        public static bool IsSectional(StudentTestResult attempt) => Parse(attempt.SectionPlanJson).Count > 0;

        /// <summary>Total minutes of a sectional attempt, or null when the attempt isn't sectional.</summary>
        public static int? TotalMinutes(StudentTestResult attempt)
        {
            var plan = Parse(attempt.SectionPlanJson);
            return plan.Count == 0 ? null : plan.Sum(p => p.DurationMinutes);
        }

        /// <summary>The section the student is in right now (after <see cref="Normalize"/>), or null.</summary>
        public static SectionPlanItem? Current(StudentTestResult attempt)
        {
            var plan = Parse(attempt.SectionPlanJson);
            if (plan.Count == 0) return null;
            return plan.FirstOrDefault(p => p.Order == attempt.CurrentSectionOrder) ?? plan[0];
        }

        public static DateTime? CurrentDeadline(StudentTestResult attempt)
        {
            var cur = Current(attempt);
            if (cur == null || attempt.CurrentSectionStartedAt == null) return null;
            return attempt.CurrentSectionStartedAt.Value.AddMinutes(cur.DurationMinutes);
        }

        /// <summary>Moves the attempt past every section whose clock has run out. Returns true if anything
        /// changed (the caller then saves). Does nothing for a non-sectional attempt.</summary>
        public static bool Normalize(StudentTestResult attempt, DateTime nowUtc)
        {
            var plan = Parse(attempt.SectionPlanJson);
            if (plan.Count == 0) return false;

            var changed = false;
            if (attempt.CurrentSectionOrder == null || attempt.CurrentSectionStartedAt == null)
            {
                attempt.CurrentSectionOrder = plan[0].Order;
                attempt.CurrentSectionStartedAt = attempt.StartedAt;
                changed = true;
            }

            while (true)
            {
                var cur = plan.FirstOrDefault(p => p.Order == attempt.CurrentSectionOrder) ?? plan[0];
                var deadline = attempt.CurrentSectionStartedAt!.Value.AddMinutes(cur.DurationMinutes);
                if (nowUtc <= deadline + Grace) break;

                var next = plan.FirstOrDefault(p => p.Order > cur.Order);
                if (next == null) break; // last section: the attempt-level expiry takes over

                attempt.CurrentSectionOrder = next.Order;
                attempt.CurrentSectionStartedAt = deadline;
                changed = true;
            }
            return changed;
        }

        /// <summary>The student ends the current section (early, or because its clock hit zero) and starts the
        /// next. Returns false when there is no next section (the student is in the last one -- they submit
        /// the test instead).</summary>
        public static bool Advance(StudentTestResult attempt, DateTime nowUtc)
        {
            var plan = Parse(attempt.SectionPlanJson);
            if (plan.Count == 0) return false;
            Normalize(attempt, nowUtc);

            var cur = plan.FirstOrDefault(p => p.Order == attempt.CurrentSectionOrder) ?? plan[0];
            var next = plan.FirstOrDefault(p => p.Order > cur.Order);
            if (next == null) return false;

            attempt.CurrentSectionOrder = next.Order;
            attempt.CurrentSectionStartedAt = nowUtc;
            return true;
        }

        public static bool IsLastSection(StudentTestResult attempt)
        {
            var plan = Parse(attempt.SectionPlanJson);
            var cur = Current(attempt);
            return plan.Count == 0 || cur == null || plan.All(p => p.Order <= cur.Order);
        }
    }
}
