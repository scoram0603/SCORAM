using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services.Analytics
{
    /// <summary>
    /// SMART PROGRESS -- the ONE definition of "this student's completed answers / attempts, narrowed
    /// by the optional filters" used by every analytics query, so the dashboard, the result page and
    /// the Mistake Book can never disagree about which attempts count. A COMPLETED attempt is one
    /// whose status has left InProgress (same definition as the existing Progress page and Study
    /// Partner). The exam of an attempt is resolved exactly like StudentProgressService does.
    /// </summary>
    internal static class AttemptQueries
    {
        public static IQueryable<StudentAnswer> CompletedAnswers(
            ScoramDbContext db, Guid userId, TestKind? kind, IReadOnlyCollection<Guid>? examIds, DateTime? from, DateTime? to)
        {
            var q = db.StudentAnswers.AsNoTracking()
                .Where(a => a.StudentTestResult!.UserId == userId
                            && a.StudentTestResult.Status != TestAttemptStatus.InProgress)
                .WhereReleased(); // RESULT RELEASE: a held-back Mock Test result must not leak via analytics

            if (kind.HasValue)
            {
                var k = kind.Value;
                q = q.Where(a => a.StudentTestResult!.TestKind == k);
            }

            if (examIds != null)
            {
                q = q.Where(a => examIds.Contains(
                    a.StudentTestResult!.MockTest!.ExamId
                    ?? a.StudentTestResult.PracticeTestTemplate!.ExamId
                    ?? (Guid?)a.StudentTestResult.Paper!.ExamId
                    ?? a.StudentTestResult.PracticeExamId
                    ?? Guid.Empty));
            }

            if (from.HasValue)
            {
                var f = from.Value;
                q = q.Where(a => a.StudentTestResult!.AttemptedAt >= f);
            }
            if (to.HasValue)
            {
                var t = to.Value;
                q = q.Where(a => a.StudentTestResult!.AttemptedAt < t);
            }
            return q;
        }

        public static IQueryable<StudentTestResult> CompletedAttempts(
            ScoramDbContext db, Guid userId, TestKind? kind, IReadOnlyCollection<Guid>? examIds, DateTime? from, DateTime? to)
        {
            var q = db.StudentTestResults.AsNoTracking()
                .Where(r => r.UserId == userId && r.Status != TestAttemptStatus.InProgress)
                .WhereReleased();

            if (kind.HasValue)
            {
                var k = kind.Value;
                q = q.Where(r => r.TestKind == k);
            }

            if (examIds != null)
            {
                q = q.Where(r => examIds.Contains(
                    r.MockTest!.ExamId ?? r.PracticeTestTemplate!.ExamId ?? (Guid?)r.Paper!.ExamId ?? r.PracticeExamId ?? Guid.Empty));
            }

            if (from.HasValue)
            {
                var f = from.Value;
                q = q.Where(r => r.AttemptedAt >= f);
            }
            if (to.HasValue)
            {
                var t = to.Value;
                q = q.Where(r => r.AttemptedAt < t);
            }
            return q;
        }
    }
}
