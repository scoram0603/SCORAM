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
        public const int MinChallengeQuestions = 5;
        public const int MaxChallengeQuestions = 500;

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

        public static string? ValidateChallenge(StudyChallengeType type, int? questionCount, int? targetDays, int durationDays)
        {
            if (durationDays < 1 || durationDays > MaxChallengeDays)
                return $"A challenge can last 1 to {MaxChallengeDays} days.";

            if (type == StudyChallengeType.Streak)
            {
                if (targetDays is null || targetDays < 2 || targetDays > durationDays)
                    return "Pick a streak target of at least 2 days that fits inside the challenge period.";
                return null;
            }

            if (questionCount is null || questionCount < MinChallengeQuestions || questionCount > MaxChallengeQuestions)
                return $"Pick between {MinChallengeQuestions} and {MaxChallengeQuestions} questions.";
            return null;
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
