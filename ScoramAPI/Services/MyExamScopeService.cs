using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Extensions;

namespace ScoramAPI.Services
{
    // "MY EXAMS" -- the single place that answers "which exams may THIS caller see content for?".
    //
    // My Exams is a STRICT content scope for students, not a default filter (see
    // Controllers/UserExamsController.cs). Every student-facing, exam-specific list/search endpoint
    // resolves the scope here and applies it IN THE DATABASE QUERY -- never by fetching everything
    // and trimming it in React/Flutter.
    //
    // Who is scoped:
    //   * An authenticated Student  -> scoped to their saved My Exams (possibly an EMPTY set -- a
    //     student who skipped selection sees no exam-specific content, never "all exams").
    //   * Anonymous visitors, Admins, SuperAdmins -> NOT scoped (MyExamScope.Unscoped). Public
    //     browsing and the admin tools keep working exactly as before; only the student
    //     personalization contract changed.
    //
    // What is NOT scoped (deliberately, see each controller's own comment):
    //   * Content with no exam relationship at all (Quizzes, standalone Group rooms, ...) -- that is
    //     intentionally global content and stays visible.
    //   * Direct "get by id" detail endpoints -- they back a student's own history (past attempts,
    //     bookmarks, shared chat links) which must keep opening even after My Exams changes.
    public interface IMyExamScopeService
    {
        /// <summary>Resolves the My Exams scope for the current request's principal. One query per
        /// request at most (cached for the lifetime of the scoped service instance).</summary>
        Task<MyExamScope> GetScopeAsync(ClaimsPrincipal user, CancellationToken ct = default);
    }

    public sealed class MyExamScope
    {
        public static readonly MyExamScope Unscoped = new(false, Array.Empty<Guid>(), Array.Empty<string>());

        public MyExamScope(bool isScoped, IReadOnlyList<Guid> examIds, IReadOnlyList<string> examNames)
        {
            IsScoped = isScoped;
            ExamIds = examIds;
            ExamNames = examNames;
        }

        /// <summary>True for an authenticated student. When false, callers apply no scoping at all.</summary>
        public bool IsScoped { get; }

        /// <summary>The student's selected, currently-available exams. Empty = nothing selected
        /// (callers must then return NO exam-specific content, not everything).</summary>
        public IReadOnlyList<Guid> ExamIds { get; }

        /// <summary>Names of the same exams -- only used for legacy rows that were never given a real
        /// ExamId (MockTest.ExamName, Question.ExamName) so they can still be matched.</summary>
        public IReadOnlyList<string> ExamNames { get; }

        /// <summary>Scoped student who has not selected any exam.</summary>
        public bool IsEmpty => IsScoped && ExamIds.Count == 0;

        public bool Allows(Guid examId) => !IsScoped || ExamIds.Contains(examId);

        public bool AllowsName(string? examName) =>
            !IsScoped || (!string.IsNullOrWhiteSpace(examName)
                && ExamNames.Contains(examName.Trim(), StringComparer.OrdinalIgnoreCase));

        /// <summary>Intersects an explicitly requested exam-id filter with the scope, so a client can
        /// NARROW within My Exams but can never widen beyond it. A null/empty request yields the
        /// whole scope. The result may be empty (requested exams all outside the scope) -- callers
        /// must treat that as "match nothing", which EF translates correctly for Contains().</summary>
        public IReadOnlyList<Guid> Narrow(IEnumerable<Guid>? requested)
        {
            var requestedList = requested?.Distinct().ToList();
            if (requestedList == null || requestedList.Count == 0) return ExamIds;
            return ExamIds.Where(id => requestedList.Contains(id)).ToList();
        }

        /// <summary>Same as Narrow but for a single optional exam id.</summary>
        public IReadOnlyList<Guid> Narrow(Guid? requested) =>
            requested.HasValue ? Narrow(new[] { requested.Value }) : ExamIds;
    }

    public class MyExamScopeService : IMyExamScopeService
    {
        private readonly ScoramDbContext _db;
        private MyExamScope? _cached;

        public MyExamScopeService(ScoramDbContext db)
        {
            _db = db;
        }

        public async Task<MyExamScope> GetScopeAsync(ClaimsPrincipal user, CancellationToken ct = default)
        {
            if (_cached != null) return _cached;

            if (user.Identity?.IsAuthenticated != true || !user.IsInRole("Student"))
                return _cached = MyExamScope.Unscoped;

            var userId = user.GetUserId();

            // ONE query for the whole selection (joined to Exams): a selected exam that an admin has
            // since blocked -- or whose Organization was blocked -- silently drops out of scope, the
            // same way it already disappears from every other student-facing exam list.
            var rows = await _db.UserExamPreferences
                .Where(p => p.UserId == userId
                    && !p.Exam!.IsBlocked
                    && (p.Exam.Organization == null || !p.Exam.Organization.IsBlocked))
                .Select(p => new { p.ExamId, ExamName = p.Exam!.Name })
                .ToListAsync(ct);

            return _cached = new MyExamScope(
                true,
                rows.Select(r => r.ExamId).ToList(),
                rows.Select(r => r.ExamName).ToList());
        }
    }
}
