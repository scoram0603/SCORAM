using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    /// <summary>Pure, database-free Study Partner business rules -- kept separate from the controller so
    /// they can be unit-tested directly (see ScoramAPI.Tests/StudyPartnerRulesTests.cs).</summary>
    public static class StudyPartnerRules
    {
        /// <summary>How long after a rejection the sender may ask the same person again.</summary>
        public static readonly TimeSpan ResendCooldown = TimeSpan.FromDays(7);

        /// <summary>A challenge nobody answered within this long becomes Expired.</summary>
        public static readonly TimeSpan PendingChallengeLifetime = TimeSpan.FromDays(3);

        public const int MaxChallengeDays = 30;
        public const int MinChallengeMinutes = 1;
        public const int MaxChallengeMinutes = MaxChallengeDays * 24 * 60; // 43200
        public const int MinChallengeQuestions = 1;
        public const int MaxChallengeQuestions = 1000;

        /// <summary>An accuracy ranking only means something over enough answers.</summary>
        public const int MinAnsweredForAccuracyRank = 10;

        /// <summary>Same pair, same order, whichever way round it is asked -- the single place the
        /// StudyPartnership row ordering is decided.</summary>
        public static (Guid A, Guid B) Canonical(Guid x, Guid y) => x.CompareTo(y) < 0 ? (x, y) : (y, x);

        /// <summary>Defaults for a student who never opened Privacy settings: privacy-conscious.</summary>
        public static UserPrivacySettings DefaultPrivacy(Guid userId) => new() { UserId = userId };

        /// <summary>The ONE visibility decision, used for every piece of data another student can see.</summary>
        public static bool CanView(VisibilityLevel level, bool viewerIsOwner, bool viewerIsPartner) =>
            viewerIsOwner || level switch
            {
                VisibilityLevel.Everyone => true,
                VisibilityLevel.StudyPartnersOnly => viewerIsPartner,
                _ => false
            };

        /// <summary>The student picks both numbers freely: any question count (1..1000) and any duration from
        /// 1 minute up to 30 days. A Streak counts study DAYS, so its target must fit the days available.</summary>
        public static string? ValidateChallenge(StudyChallengeType type, int? questionCount, int? targetDays, int durationMinutes)
        {
            if (durationMinutes < MinChallengeMinutes || durationMinutes > MaxChallengeMinutes)
                return $"A challenge can last from 1 minute up to {MaxChallengeDays} days.";

            if (type == StudyChallengeType.Streak)
            {
                var daysAvailable = DaysInWindow(durationMinutes);
                if (targetDays is null || targetDays < 2 || targetDays > daysAvailable)
                    return "Pick a streak target of at least 2 days that fits inside the challenge period.";
                return null;
            }

            if (questionCount is null || questionCount < MinChallengeQuestions || questionCount > MaxChallengeQuestions)
                return $"Enter between {MinChallengeQuestions} and {MaxChallengeQuestions} questions.";
            return null;
        }

        /// <summary>Whole days a window of this many minutes can offer (rounded up, at least 1).</summary>
        public static int DaysInWindow(int durationMinutes) => Math.Max(1, (int)Math.Ceiling(durationMinutes / 1440.0));

        /// <summary>Minutes a stored challenge runs: the exact value, or days * 1440 for older rows.</summary>
        public static int EffectiveMinutes(int? durationMinutes, int durationDays) => durationMinutes ?? durationDays * 1440;

        /// <summary>Plain-English length: "15 minutes", "2 hours", "1 hour 30 minutes", "3 days".</summary>
        public static string FormatDuration(int minutes)
        {
            static string Unit(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
            if (minutes % 1440 == 0) return Unit(minutes / 1440, "day");
            if (minutes < 60) return Unit(minutes, "minute");
            var h = minutes / 60; var m = minutes % 60;
            if (minutes < 1440) return m == 0 ? Unit(h, "hour") : $"{Unit(h, "hour")} {Unit(m, "minute")}";
            var d = minutes / 1440; var rest = minutes % 1440;
            var rh = rest / 60;
            return rh == 0 ? Unit(d, "day") : $"{Unit(d, "day")} {Unit(rh, "hour")}";
        }

        /// <summary>0-100 progress of one side towards the challenge target (identical for both sides).</summary>
        public static int ProgressPercent(StudyChallengeType type, int? questionCount, int? targetDays, int answered, int bestAttemptAnswered, int activeDays)
        {
            double done, goal;
            switch (type)
            {
                case StudyChallengeType.Streak: done = activeDays; goal = targetDays ?? 0; break;
                case StudyChallengeType.Speed: done = bestAttemptAnswered; goal = questionCount ?? 0; break;
                default: done = answered; goal = questionCount ?? 0; break;
            }
            if (goal <= 0) return 0;
            return (int)Math.Clamp(Math.Floor(done / goal * 100), 0, 100);
        }

        /// <summary>One side's activity inside the challenge window (built from StudentTestResults).</summary>
        public sealed record Side(int Answered, int Correct, int? FastestSeconds, int ActiveDays)
        {
            public decimal Accuracy => Answered == 0 ? 0 : Math.Round((decimal)Correct / Answered * 100, 1);
        }

        public sealed record Outcome(decimal CreatorResult, decimal PartnerResult, Guid? WinnerUserId, string Summary);

        /// <summary>Decides a challenge from both sides' numbers. A side must QUALIFY (reach the target
        /// question count / streak days) to win; one qualifier beats a non-qualifier; no qualifier or an
        /// exact tie means no winner. "Result" is: Practice = questions answered, Accuracy = accuracy %,
        /// Speed = fastest attempt in seconds (0 = none), Streak = active days.</summary>
        public static Outcome Evaluate(StudyChallengeType type, int? questionCount, int? targetDays,
            Guid creatorId, Side creator, Guid partnerId, Side partner)
        {
            bool Qualifies(Side s) => type switch
            {
                StudyChallengeType.Streak => s.ActiveDays >= (targetDays ?? int.MaxValue),
                StudyChallengeType.Speed => s.FastestSeconds.HasValue,
                _ => s.Answered >= (questionCount ?? int.MaxValue)
            };

            decimal Result(Side s) => type switch
            {
                StudyChallengeType.Practice => s.Answered,
                StudyChallengeType.Accuracy => s.Accuracy,
                StudyChallengeType.Speed => s.FastestSeconds ?? 0,
                _ => s.ActiveDays
            };

            var cq = Qualifies(creator);
            var pq = Qualifies(partner);
            var cr = Result(creator);
            var pr = Result(partner);

            if (!cq && !pq) return new Outcome(cr, pr, null, "Neither of you reached the target this time.");
            if (cq && !pq) return new Outcome(cr, pr, creatorId, "Target reached by the challenger only.");
            if (!cq && pq) return new Outcome(cr, pr, partnerId, "Target reached by the partner only.");

            // Both qualified. Speed: lower is better. Everything else: higher is better.
            var cmp = type == StudyChallengeType.Speed ? pr.CompareTo(cr) : cr.CompareTo(pr);
            if (cmp == 0) return new Outcome(cr, pr, null, "A tie -- you both did equally well.");
            return new Outcome(cr, pr, cmp > 0 ? creatorId : partnerId, "Both reached the target.");
        }
    }
}
