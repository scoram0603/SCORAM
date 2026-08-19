using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    // GAMIFICATION -- single place all XP/streak/badge/referral rules live, so every controller that
    // triggers a reward (SolutionsController, TestAttemptsController, QuestionBankController,
    // AuthController) calls the same logic instead of five slightly-different reimplementations.
    //
    // Two related-but-distinct concepts, kept as separate methods rather than one do-everything call:
    //   - XP-only (AwardXpAsync)      -- passive/delayed rewards: an upvote arriving, a solution
    //                                    getting approved days after it was written, a referral
    //                                    landing. None of these represent "the student practiced
    //                                    today", so they must NOT bump the daily streak.
    //   - XP + streak (RecordActivityAsync) -- genuine same-day practice: finishing a test, marking a
    //                                    question solved. These both earn XP and count as today's
    //                                    activity for streak purposes.
    //   - Streak-only (TouchStreakOnlyAsync) -- for actions where the practice happens today but the
    //                                    XP is deliberately deferred (submitting a solution earns
    //                                    streak credit immediately; the XP itself waits for admin
    //                                    approval, via AwardXpAsync, so unmoderated spam can't farm XP).
    public interface IGamificationService
    {
        Task AwardXpAsync(Guid userId, int amount, string reason, string? examName = null);
        Task RecordActivityAsync(Guid userId, int amount, string reason, string? examName = null);
        Task TouchStreakOnlyAsync(Guid userId);
        Task<bool> AwardBadgeByNameAsync(Guid userId, string badgeName);
        Task CheckTopContributorAsync(Guid userId);
        Task<string> GetOrCreateReferralCodeAsync(Guid userId);
        Task ApplyReferralAsync(Guid newUserId, string referralCodeInput);
    }

    public class GamificationService : IGamificationService
    {
        private readonly ScoramDbContext _db;
        private readonly ILogger<GamificationService> _logger;

        // Defaults for every numeric rule in this module, all in one place so they're easy to tune
        // later without hunting through call sites. Values agreed as a starting point -- see the
        // Gamification discussion: XP per action, level thresholds, streak-freeze allowance, and
        // streak-milestone bonuses.
        private const int XpSolutionApproved = 10;
        private const int XpUpvoteReceived = 2;
        private const int XpTestCompleted = 20;
        private const int XpQuestionSolved = 5;
        // Deliberately between XpQuestionSolved and XpTestCompleted, not equal to XpTestCompleted --
        // a Quiz is 5-20 questions vs. a full Practice/Mock/PYP attempt, and ApplyStreak already
        // caps streak progress at once/day regardless of how many quizzes are taken, but XP itself
        // has no such cap, so a flat XpTestCompleted here would let someone farm XP by generating
        // many tiny quizzes back-to-back.
        private const int XpQuizCompleted = 8;
        private const int XpReferralJoined = 30;
        private const int TopContributorUpvoteThreshold = 50;
        private const int MaxFreezesPerWeek = 1;

        // Exposed for ReferralsController, which needs this same number to compute a referrer's
        // "total XP earned from referrals" display without duplicating the constant.
        public const int ReferralXpReward = XpReferralJoined;

        public GamificationService(ScoramDbContext db, ILogger<GamificationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task AwardXpAsync(Guid userId, int amount, string reason, string? examName = null)
        {
            var xp = await _db.UserXPs.FirstOrDefaultAsync(x => x.UserId == userId);
            if (xp == null) return; // defensive -- every student gets this row at registration

            ApplyXp(userId, xp, amount, reason, examName);
            await _db.SaveChangesAsync();
        }

        public async Task RecordActivityAsync(Guid userId, int amount, string reason, string? examName = null)
        {
            var xp = await _db.UserXPs.FirstOrDefaultAsync(x => x.UserId == userId);
            var streak = await _db.UserStreaks.FirstOrDefaultAsync(s => s.UserId == userId);
            if (xp == null || streak == null) return;

            ApplyXp(userId, xp, amount, reason, examName);
            var milestoneBonus = ApplyStreak(streak);
            if (milestoneBonus > 0)
                ApplyXp(userId, xp, milestoneBonus, "StreakMilestone");

            await _db.SaveChangesAsync();

            if (streak.CurrentStreak == 100)
                await AwardBadgeByNameAsync(userId, "100 Day Warrior");
        }

        public async Task TouchStreakOnlyAsync(Guid userId)
        {
            var xp = await _db.UserXPs.FirstOrDefaultAsync(x => x.UserId == userId);
            var streak = await _db.UserStreaks.FirstOrDefaultAsync(s => s.UserId == userId);
            if (streak == null) return;

            var milestoneBonus = ApplyStreak(streak);
            if (milestoneBonus > 0 && xp != null)
                ApplyXp(userId, xp, milestoneBonus, "StreakMilestone");

            await _db.SaveChangesAsync();

            if (streak.CurrentStreak == 100)
                await AwardBadgeByNameAsync(userId, "100 Day Warrior");
        }

        public async Task<bool> AwardBadgeByNameAsync(Guid userId, string badgeName)
        {
            var badge = await _db.Badges.FirstOrDefaultAsync(b => b.Name == badgeName);
            if (badge == null)
            {
                _logger.LogWarning("Gamification: no badge named {BadgeName} exists to award", badgeName);
                return false;
            }

            var alreadyHeld = await _db.UserBadges.AnyAsync(ub => ub.UserId == userId && ub.BadgeId == badge.Id);
            if (alreadyHeld) return false;

            _db.UserBadges.Add(new UserBadge { UserId = userId, BadgeId = badge.Id, EarnedAt = DateTime.UtcNow });
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task CheckTopContributorAsync(Guid userId)
        {
            var totalUpvotes = await _db.QuestionSolutions
                .Where(s => s.SubmittedByUserId == userId)
                .SumAsync(s => (int?)s.UpvoteCount) ?? 0;

            if (totalUpvotes >= TopContributorUpvoteThreshold)
                await AwardBadgeByNameAsync(userId, "Top Contributor");
        }

        public async Task<string> GetOrCreateReferralCodeAsync(Guid userId)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null) throw new InvalidOperationException("User not found.");
            if (!string.IsNullOrEmpty(user.ReferralCode)) return user.ReferralCode;

            string code;
            do
            {
                code = GenerateCode(user.Username);
            } while (await _db.Users.AnyAsync(u => u.ReferralCode == code));

            user.ReferralCode = code;
            await _db.SaveChangesAsync();
            return user.ReferralCode;
        }

        // Called from AuthController.Register when a new signup supplies someone else's code.
        // Reward: bonus XP + one extra Mock Test attempt for the referrer (see User.BonusMockAttempts
        // and MockTestsController.Start). SRS also lists "premium days" as a possible reward, but the
        // app has no premium/subscription concept anywhere else yet -- deliberately not building a
        // one-off billing flag just for this, so that reward tier is left out for now.
        public async Task ApplyReferralAsync(Guid newUserId, string referralCodeInput)
        {
            if (string.IsNullOrWhiteSpace(referralCodeInput)) return;

            var referrer = await _db.Users.FirstOrDefaultAsync(u => u.ReferralCode == referralCodeInput);
            if (referrer == null || referrer.Id == newUserId) return; // unknown code, or can't refer yourself

            _db.Referrals.Add(new Referral
            {
                ReferrerUserId = referrer.Id,
                ReferralCode = referralCodeInput,
                ReferredUserId = newUserId,
                Status = ReferralStatus.Rewarded,
                JoinedAt = DateTime.UtcNow
            });

            referrer.BonusMockAttempts += 1;
            await _db.SaveChangesAsync();

            await AwardXpAsync(referrer.Id, XpReferralJoined, "ReferralJoined");
        }

        // ======================================================================================
        // Named-reason constants for the call sites above to use, so every controller writes the
        // exact same string (and the exact same XP amount) for a given action instead of each
        // inventing its own.
        // ======================================================================================
        public static class Reasons
        {
            public const string SolutionApproved = "SolutionApproved";
            public const string SolutionUpvoted = "SolutionUpvoted";
            public const string MockTestCompleted = "MockTestCompleted";
            public const string PracticeTestCompleted = "PracticeTestCompleted";
            public const string PreviousYearPaperCompleted = "PreviousYearPaperCompleted";
            public const string QuestionSolved = "QuestionSolved";
            public const string QuizCompleted = "QuizCompleted";
        }

        public static int XpFor(string reason) => reason switch
        {
            Reasons.SolutionApproved => XpSolutionApproved,
            Reasons.SolutionUpvoted => XpUpvoteReceived,
            Reasons.MockTestCompleted => XpTestCompleted,
            Reasons.PracticeTestCompleted => XpTestCompleted,
            Reasons.PreviousYearPaperCompleted => XpTestCompleted,
            Reasons.QuestionSolved => XpQuestionSolved,
            Reasons.QuizCompleted => XpQuizCompleted,
            _ => 0
        };

        // ======================================================================================
        // Internals
        // ======================================================================================

        private void ApplyXp(Guid userId, UserXP xp, int amount, string reason, string? examName = null)
        {
            _db.XpTransactions.Add(new XpTransaction
            {
                UserId = userId,
                Amount = amount,
                Reason = reason,
                ExamName = examName,
                CreatedAt = DateTime.UtcNow
            });

            xp.TotalXP += amount;
            xp.CurrentLevel = LevelForXp(xp.TotalXP);
            xp.UpdatedAt = DateTime.UtcNow;
        }

        private static UserLevel LevelForXp(int totalXp) => totalXp switch
        {
            >= 5000 => UserLevel.Master,
            >= 2000 => UserLevel.Expert,
            >= 500 => UserLevel.Intermediate,
            _ => UserLevel.Beginner
        };

        // Streak day-boundary is IST (UTC+5:30), not UTC -- this app's students are in India, and
        // using raw UTC calendar days would misattribute anything done between midnight and 5:30am
        // IST to "yesterday". Mutates streak in place; returns a one-off bonus XP amount for hitting
        // a 7/30/100-day milestone this call (0 most of the time).
        private int ApplyStreak(UserStreak streak)
        {
            var now = DateTime.UtcNow;
            var today = ToIstDate(now);

            if (streak.LastActiveDate == today)
                return 0; // already counted today -- calling this twice in one day is a no-op

            var (thisYear, thisWeek) = IsoWeek(today);
            var (lastYear, lastWeek) = IsoWeek(streak.LastActiveDate);
            if (thisYear != lastYear || thisWeek != lastWeek)
                streak.FreezesUsedThisWeek = 0; // new ISO week -- freeze allowance renews

            var gapDays = today.DayNumber - streak.LastActiveDate.DayNumber;

            if (gapDays == 1)
            {
                streak.CurrentStreak++;
            }
            else if (gapDays == 2 && streak.FreezesUsedThisWeek < MaxFreezesPerWeek)
            {
                // Exactly one day was missed and a freeze is still available this week -- spend it
                // to keep the streak alive instead of resetting.
                streak.FreezesUsedThisWeek++;
                streak.CurrentStreak++;
            }
            else
            {
                streak.CurrentStreak = 1; // gap too large (or no freeze left) -- today restarts it
            }

            streak.LongestStreak = Math.Max(streak.LongestStreak, streak.CurrentStreak);
            streak.LastActiveDate = today;
            streak.UpdatedAt = now;

            return streak.CurrentStreak switch
            {
                7 => 25,
                30 => 100,
                100 => 300,
                _ => 0
            };
        }

        public static DateOnly ToIstDate(DateTime utc) => DateOnly.FromDateTime(utc.AddHours(5).AddMinutes(30));

        private static (int Year, int Week) IsoWeek(DateOnly date)
        {
            var dt = date.ToDateTime(TimeOnly.MinValue);
            return (ISOWeek.GetYear(dt), ISOWeek.GetWeekOfYear(dt));
        }

        private static string GenerateCode(string username)
        {
            var lettersDigits = new string(username.Where(char.IsLetterOrDigit).ToArray());
            var basePart = (lettersDigits.Length >= 4 ? lettersDigits[..4] : lettersDigits.PadRight(4, 'X')).ToUpperInvariant();
            var suffix = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
            return $"{basePart}{suffix}";
        }
    }
}
