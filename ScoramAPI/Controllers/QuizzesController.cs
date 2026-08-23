using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Quizzes (Phase 1: Weak Topics Quiz) -- short, auto-generated attempts that reuse the exact same
    // TestAttemptService/StudentTestResult engine as Practice/Mock/Previous Year Paper (see
    // TestKind.Quiz). Deliberately NOT modeled after PracticeTestsController.Generate's filter form --
    // there's nothing for the student to configure here. The whole point is "one tap, we already know
    // what you're weak at" (see TestAttemptService.SelectWeakTopicQuestionsAsync). Later phases
    // (admin-curated Daily Quiz, Challenge a Friend) get their own endpoints alongside these, once
    // built -- see the "Quizzes" feature discussion for the full plan.
    [ApiController]
    [Route("api/quizzes")]
    public class QuizzesController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly ITestAttemptService _attemptService;

        // No negative marking by default for a Weak Topics Quiz -- meant to be low-pressure,
        // daily-habit practice, unlike a real Paper/Mock attempt. A Daily Quiz (Phase 2) sets its
        // own NegativeMarkingRatio per quiz instead, same as MockTest/Paper do.
        private const decimal DefaultNegativeMarkingRatio = 0m;

        public QuizzesController(ScoramDbContext db, ITestAttemptService attemptService)
        {
            _db = db;
            _attemptService = attemptService;
        }

        // GET /api/quizzes/weak-topics/preview -- which subjects this student is currently weak in,
        // so the Quizzes page can show "Your weak areas: Reasoning (42%), Quant (58%)" before they
        // commit to starting. Never generates an attempt; safe to call as often as the page wants.
        [HttpGet("weak-topics/preview")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<List<WeakSubjectDto>>> PreviewWeakTopics()
        {
            var userId = User.GetUserId();
            var subjects = await _attemptService.GetWeakSubjectsAsync(_db, userId);

            return Ok(subjects.Take(3).Select(s => new WeakSubjectDto
            {
                Subject = s.Subject,
                Accuracy = Math.Round(s.Accuracy * 100, 0),
                AnswersConsidered = s.Attempts
            }).ToList());
        }

        // POST /api/quizzes/weak-topics/generate -- always produces a fresh InProgress attempt, same
        // "no dedupe, generating again is always allowed" rule as PracticeTestsController.Generate.
        [HttpPost("weak-topics/generate")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<TestAttemptStartResponseDto>> GenerateWeakTopicsQuiz(QuizGenerateDto dto)
        {
            var userId = User.GetUserId();
            var refs = await _attemptService.SelectWeakTopicQuestionsAsync(_db, userId, dto.QuestionCount);
            if (refs.Count == 0)
                return BadRequest(new { message = "The Question Bank doesn't have any active questions yet -- check back once some are added." });

            var answers = await _attemptService.BuildSnapshotAnswersAsync(_db, refs);

            // ~1 minute per question, floor of 5 -- short enough that a hard timer doesn't feel
            // punitive for a 5-question quiz, generous enough not to feel rushed either.
            var durationMinutes = Math.Max(5, refs.Count);

            var attempt = new StudentTestResult
            {
                TestKind = TestKind.Quiz,
                UserId = userId,
                QuizDurationMinutes = durationMinutes,
                NegativeMarkingRatio = DefaultNegativeMarkingRatio,
                Status = TestAttemptStatus.InProgress,
                StartedAt = DateTime.UtcNow
            };
            foreach (var a in answers) attempt.Answers.Add(a);

            _db.StudentTestResults.Add(attempt);
            await _db.SaveChangesAsync();

            return Ok(TestAttemptsController.ToStartResponse(attempt));
        }

        // ============================================================================================
        // Phase 2 -- admin-curated Daily Quiz (see Models/QuizModels.cs)
        // ============================================================================================

        // GET /api/quizzes/daily -- every currently-Live (or Upcoming, so a student can see what's
        // coming) admin-curated quiz. No auth required to browse, same as MockTestsController.List/
        // StudentPapersController.Browse -- MyAttemptCount is only populated when authenticated.
        [HttpGet("daily")]
        public async Task<ActionResult<List<QuizSummaryDto>>> ListDaily()
        {
            var quizzes = await _db.Quizzes
                .Include(q => q.QuizQuestions)
                .Where(q => q.Status == TestPublishStatus.Published)
                .OrderByDescending(q => q.AvailableFrom ?? q.CreatedAt)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var items = quizzes.Select(q => QuizzesAdminController.ToSummaryDto(q, now)).ToList();

            var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            if (isAuthenticated && items.Count > 0)
            {
                var userId = User.GetUserId();
                var quizIds = quizzes.Select(q => q.Id).ToList();
                var myAttemptCounts = await _db.StudentTestResults
                    .Where(r => r.UserId == userId && r.QuizId != null && quizIds.Contains(r.QuizId.Value))
                    .GroupBy(r => r.QuizId!.Value)
                    .Select(g => new { QuizId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.QuizId, g => g.Count);

                foreach (var item in items)
                    item.MyAttemptCount = myAttemptCounts.GetValueOrDefault(item.Id, 0);
            }

            // Upcoming/Completed clutter a small "today's quizzes" list -- only Live (or Draft
            // that slipped through, which shouldn't happen since the query above is Published-only)
            // actually matters to a student browsing right now.
            return Ok(items.Where(i => i.AvailabilityStatus is "Live" or "Upcoming").ToList());
        }

        // GET /api/quizzes/{id} -- single-quiz metadata lookup, for the Pre-Exam Instructions screen
        // (see PreExamInstructions.jsx) when a student navigates there directly rather than via the
        // /daily list that already has this data in hand. No question payload here, same as
        // QuizSummaryDto everywhere else -- there's nothing heavier to accidentally over-fetch for a
        // Quiz the way MockTestsController.GetById risks for a Mock Test.
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<QuizSummaryDto>> GetById(Guid id)
        {
            var quiz = await _db.Quizzes.Include(q => q.QuizQuestions).FirstOrDefaultAsync(q => q.Id == id);
            if (quiz == null || quiz.Status != TestPublishStatus.Published) return NotFound();

            var summary = QuizzesAdminController.ToSummaryDto(quiz, DateTime.UtcNow);
            if (User.Identity?.IsAuthenticated ?? false)
            {
                var userId = User.GetUserId();
                summary.MyAttemptCount = await _db.StudentTestResults.CountAsync(r => r.UserId == userId && r.QuizId == id);
            }
            return Ok(summary);
        }

        // POST /api/quizzes/{id}/start -- same "resume an existing InProgress attempt if one exists,
        // otherwise create a fresh one" flow as MockTestsController.Start, respecting MaxAttempts and
        // the AvailableFrom/AvailableTo window.
        [HttpPost("{id:guid}/start")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<TestAttemptStartResponseDto>> StartDailyQuiz(Guid id)
        {
            var quiz = await _db.Quizzes.Include(q => q.QuizQuestions).FirstOrDefaultAsync(q => q.Id == id);
            if (quiz == null || quiz.Status != TestPublishStatus.Published)
                return NotFound(new { message = "Quiz not found." });

            var now = DateTime.UtcNow;
            if (quiz.AvailableFrom.HasValue && now < quiz.AvailableFrom.Value)
                return BadRequest(new { message = "This quiz hasn't started yet." });
            if (quiz.AvailableTo.HasValue && now > quiz.AvailableTo.Value)
                return BadRequest(new { message = "This quiz's window has closed." });

            var userId = User.GetUserId();

            var existing = await _db.StudentTestResults
                .Include(r => r.Answers)
                .FirstOrDefaultAsync(r => r.UserId == userId && r.QuizId == id && r.Status == TestAttemptStatus.InProgress);
            if (existing != null)
            {
                existing.Quiz = quiz;
                return Ok(TestAttemptsController.ToStartResponse(existing));
            }

            if (quiz.MaxAttempts.HasValue)
            {
                var usedAttempts = await _db.StudentTestResults.CountAsync(r => r.UserId == userId && r.QuizId == id);
                if (usedAttempts >= quiz.MaxAttempts.Value)
                    return BadRequest(new { message = $"You've used all {quiz.MaxAttempts.Value} attempt(s) for this quiz." });
            }

            if (quiz.QuizQuestions.Count == 0)
                return BadRequest(new { message = "This quiz doesn't have any questions yet." });

            var refs = quiz.QuizQuestions.OrderBy(qq => qq.QuestionOrder)
                .Select((qq, i) => new QuestionRef(null, qq.QuestionBankQuestionId, i + 1));
            var answers = await _attemptService.BuildSnapshotAnswersAsync(_db, refs);

            var attempt = new StudentTestResult
            {
                TestKind = TestKind.Quiz,
                QuizId = quiz.Id,
                UserId = userId,
                NegativeMarkingRatio = quiz.NegativeMarkingRatio,
                Status = TestAttemptStatus.InProgress,
                StartedAt = now
            };
            foreach (var a in answers) attempt.Answers.Add(a);

            _db.StudentTestResults.Add(attempt);
            await _db.SaveChangesAsync();

            attempt.Quiz = quiz;
            return Ok(TestAttemptsController.ToStartResponse(attempt));
        }
    }
}
