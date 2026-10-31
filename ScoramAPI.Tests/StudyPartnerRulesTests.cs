using ScoramAPI.Enums;
using ScoramAPI.Services;
using Xunit;
using static ScoramAPI.Services.StudyPartnerRules;

namespace ScoramAPI.Tests
{
    // Unit tests for the database-free Study Partner rules (xUnit; drop into the ScoramAPI.Tests project).
    // NOT covered here -- they need a database harness (SQL Server / EF test context): duplicate-request
    // and duplicate-partnership unique-index behaviour, accept/reject/remove flows, endpoint-level
    // privacy and ownership checks (403/404), and challenge accept/complete persistence.
    public class StudyPartnerRulesTests
    {
        private static readonly Guid A =
            Guid.Parse("00000000-0000-0000-0000-000000000001");

        private static readonly Guid B =
            Guid.Parse("00000000-0000-0000-0000-000000000002");

        // ---- connection model -------------------------------------------------------------

        [Fact]
        public void Canonical_IsTheSameForBothDirections_SoABandBAAreOneRow()
        {
            Assert.Equal(
                Canonical(A, B),
                Canonical(B, A));
        }

        // ---- privacy ----------------------------------------------------------------------

        [Theory]
        [InlineData(VisibilityLevel.Everyone, false, false, true)]
        [InlineData(VisibilityLevel.StudyPartnersOnly, false, false, false)]
        // non-partner: private progress hidden

        [InlineData(VisibilityLevel.StudyPartnersOnly, false, true, true)]
        // partner-only progress works

        [InlineData(VisibilityLevel.OnlyMe, false, true, false)]
        // even partners can't see OnlyMe

        [InlineData(VisibilityLevel.OnlyMe, true, false, true)]
        // owner always sees their own

        public void CanView_Matrix(
            VisibilityLevel level,
            bool owner,
            bool partner,
            bool expected)
        {
            Assert.Equal(
                expected,
                CanView(level, owner, partner));
        }

        [Fact]
        public void DefaultPrivacy_IsPrivacyConscious()
        {
            var d = DefaultPrivacy(A);

            Assert.Equal(
                VisibilityLevel.StudyPartnersOnly,
                d.ProgressVisibility);

            Assert.Equal(
                VisibilityLevel.StudyPartnersOnly,
                d.ActivityVisibility);
        }

        // ---- challenge validation ---------------------------------------------------------
        // ValidateChallenge takes the duration in MINUTES (the student types 1 minute .. 30 days; see
        // MinChallengeMinutes / MaxChallengeMinutes). 1 day = 1440 minutes. Question count is 1..1000.

        [Theory]
        [InlineData(StudyChallengeType.Practice, 50, null, 10080, true)]
        // 50 questions in 7 days
        [InlineData(StudyChallengeType.Practice, 1, null, 10080, true)]
        // lowest allowed question count
        [InlineData(StudyChallengeType.Practice, 0, null, 10080, false)]
        // too few questions
        [InlineData(StudyChallengeType.Practice, 1000, null, 10080, true)]
        // highest allowed question count
        [InlineData(StudyChallengeType.Practice, 1001, null, 10080, false)]
        // too many questions
        [InlineData(StudyChallengeType.Practice, null, null, 10080, false)]
        // missing count
        [InlineData(StudyChallengeType.Accuracy, 50, null, 0, false)]
        // invalid duration
        [InlineData(StudyChallengeType.Accuracy, 50, null, 1, true)]
        // 1 minute is the shortest allowed duration
        [InlineData(StudyChallengeType.Speed, 50, null, 43200, true)]
        // exactly 30 days
        [InlineData(StudyChallengeType.Speed, 50, null, 44640, false)]
        // 31 days: too long
        [InlineData(StudyChallengeType.Streak, null, 7, 10080, true)]
        // 7-day streak inside 7 days
        [InlineData(StudyChallengeType.Streak, null, 8, 10080, false)]
        // target can't exceed the period
        [InlineData(StudyChallengeType.Streak, null, 7, 7, false)]
        // 7 MINUTES leaves room for 1 study day, not 7
        [InlineData(StudyChallengeType.Streak, null, 1, 10080, false)]
        // a streak needs at least 2 days
        public void ValidateChallenge_MatchesExpectedValidity(
            StudyChallengeType type,
            int? q,
            int? days,
            int durationMinutes,
            bool valid)
        {
            Assert.Equal(
                valid,
                StudyPartnerRules.ValidateChallenge(
                    type,
                    q,
                    days,
                    durationMinutes) == null);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1440, 1)]
        [InlineData(1441, 2)]
        [InlineData(10080, 7)]
        public void DaysInWindow_RoundsUpToWholeDays(int minutes, int expectedDays)
            => Assert.Equal(expectedDays, StudyPartnerRules.DaysInWindow(minutes));

        [Fact]
        public void EffectiveMinutes_PrefersExactMinutes_AndFallsBackToDaysForOlderRows()
        {
            Assert.Equal(90, StudyPartnerRules.EffectiveMinutes(90, 1));
            Assert.Equal(7 * 1440, StudyPartnerRules.EffectiveMinutes(null, 7));
        }

        // ---- challenge scoring ------------------------------------------------------------

        [Fact]
        public void Practice_MoreQuestionsWins_WhenBothQualify()
        {
            var o = Evaluate(
                StudyChallengeType.Practice,
                50,
                null,
                A,
                new Side(80, 60, null, 3),
                B,
                new Side(60, 55, null, 3));

            Assert.Equal(A, o.WinnerUserId);
        }

        [Fact]
        public void OnlyTheQualifierWins_AndNobodyWinsIfNeitherQualifies()
        {
            Assert.Equal(
                B,
                Evaluate(
                    StudyChallengeType.Practice,
                    50,
                    null,
                    A,
                    new Side(10, 5, null, 1),
                    B,
                    new Side(50, 20, null, 2))
                    .WinnerUserId);

            Assert.Null(
                Evaluate(
                    StudyChallengeType.Practice,
                    50,
                    null,
                    A,
                    new Side(10, 5, null, 1),
                    B,
                    new Side(20, 5, null, 1))
                    .WinnerUserId);
        }

        [Fact]
        public void Accuracy_HigherPercentWins_ExactTieHasNoWinner()
        {
            Assert.Equal(
                B,
                Evaluate(
                    StudyChallengeType.Accuracy,
                    20,
                    null,
                    A,
                    new Side(20, 10, null, 1),
                    B,
                    new Side(20, 15, null, 1))
                    .WinnerUserId);

            Assert.Null(
                Evaluate(
                    StudyChallengeType.Accuracy,
                    20,
                    null,
                    A,
                    new Side(20, 10, null, 1),
                    B,
                    new Side(20, 10, null, 1))
                    .WinnerUserId);
        }

        [Fact]
        public void Speed_LowerSecondsWins()
        {
            var o = Evaluate(
                StudyChallengeType.Speed,
                20,
                null,
                A,
                new Side(20, 10, 900, 1),
                B,
                new Side(20, 10, 600, 1));

            Assert.Equal(B, o.WinnerUserId);
        }

        [Fact]
        public void Streak_MoreActiveDaysWins_ButMustReachTarget()
        {
            Assert.Equal(
                A,
                Evaluate(
                    StudyChallengeType.Streak,
                    null,
                    5,
                    A,
                    new Side(0, 0, null, 7),
                    B,
                    new Side(0, 0, null, 5))
                    .WinnerUserId);

            Assert.Null(
                Evaluate(
                    StudyChallengeType.Streak,
                    null,
                    5,
                    A,
                    new Side(0, 0, null, 3),
                    B,
                    new Side(0, 0, null, 4))
                    .WinnerUserId);
        }
    }
}
