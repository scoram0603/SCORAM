using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    /// <summary>
    /// RESULT RELEASE -- the one place that decides whether a student may see the score / answer key of a
    /// submitted Mock Test attempt yet (MockTest.ResultReleaseMode). Only Mock Test attempts are ever held
    /// back; Practice, Quiz and Previous Year Paper attempts are always visible immediately, exactly as before.
    ///
    /// Used in two ways: the in-memory helpers below (single attempt -> result screen / My Tests), and the
    /// WhereReleased() query filters, which every student-facing query that exposes scores or correct
    /// answers (progress, mistake book, revision, analytics) applies so a held-back result can't leak
    /// through some other screen.
    /// </summary>
    public static class TestResultVisibility
    {
        /// <summary>The moment (UTC) the result opens for an attempt submitted at <paramref name="submittedAtUtc"/>,
        /// or null when it opens immediately.</summary>
        public static DateTime? ReleaseAtFor(MockTest? test, DateTime submittedAtUtc)
        {
            if (test == null) return null;
            switch (test.ResultReleaseMode)
            {
                case ResultReleaseMode.AfterDelay:
                    return submittedAtUtc.AddMinutes(Math.Max(0, test.ResultDelayMinutes ?? 0));
                case ResultReleaseMode.OnDate:
                    return test.ResultReleaseAt;
                case ResultReleaseMode.AfterWindowEnds:
                    return test.EndAt;
                default:
                    return null;
            }
        }

        /// <summary>True when the attempt's result is held back right now. Always false for non-Mock attempts
        /// and for attempts still in progress (they have no result yet).</summary>
        public static bool IsLocked(StudentTestResult attempt, DateTime nowUtc, out DateTime? releaseAtUtc)
        {
            releaseAtUtc = null;
            if (attempt.TestKind != TestKind.Mock || attempt.MockTest == null) return false;
            if (attempt.Status == TestAttemptStatus.InProgress) return false;

            var releaseAt = ReleaseAtFor(attempt.MockTest, attempt.AttemptedAt);
            if (releaseAt == null || nowUtc >= releaseAt.Value) return false;

            releaseAtUtc = releaseAt;
            return true;
        }

        /// <summary>Keeps only attempts whose result is open to the student (see class comment).</summary>
        public static IQueryable<StudentTestResult> WhereReleased(this IQueryable<StudentTestResult> query)
        {
            var now = DateTime.UtcNow;
            return query.Where(r =>
                r.TestKind != TestKind.Mock
                || r.MockTest == null
                || r.MockTest.ResultReleaseMode == ResultReleaseMode.Instant
                || (r.MockTest.ResultReleaseMode == ResultReleaseMode.AfterDelay
                    && r.AttemptedAt.AddMinutes(r.MockTest.ResultDelayMinutes ?? 0) <= now)
                || (r.MockTest.ResultReleaseMode == ResultReleaseMode.OnDate
                    && (r.MockTest.ResultReleaseAt == null || r.MockTest.ResultReleaseAt <= now))
                || (r.MockTest.ResultReleaseMode == ResultReleaseMode.AfterWindowEnds
                    && (r.MockTest.EndAt == null || r.MockTest.EndAt <= now)));
        }

        /// <summary>Same filter, for queries that start from the answers of an attempt.</summary>
        public static IQueryable<StudentAnswer> WhereReleased(this IQueryable<StudentAnswer> query)
        {
            var now = DateTime.UtcNow;
            return query.Where(a =>
                a.StudentTestResult!.TestKind != TestKind.Mock
                || a.StudentTestResult.MockTest == null
                || a.StudentTestResult.MockTest.ResultReleaseMode == ResultReleaseMode.Instant
                || (a.StudentTestResult.MockTest.ResultReleaseMode == ResultReleaseMode.AfterDelay
                    && a.StudentTestResult.AttemptedAt.AddMinutes(a.StudentTestResult.MockTest.ResultDelayMinutes ?? 0) <= now)
                || (a.StudentTestResult.MockTest.ResultReleaseMode == ResultReleaseMode.OnDate
                    && (a.StudentTestResult.MockTest.ResultReleaseAt == null || a.StudentTestResult.MockTest.ResultReleaseAt <= now))
                || (a.StudentTestResult.MockTest.ResultReleaseMode == ResultReleaseMode.AfterWindowEnds
                    && (a.StudentTestResult.MockTest.EndAt == null || a.StudentTestResult.MockTest.EndAt <= now)));
        }
    }
}
