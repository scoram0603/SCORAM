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
    // SCORAM_TESTS -- Practice Tests. Two ways to end up with an attempt:
    //   (A) Generate: student picks filters themselves, no template involved (POST /generate).
    //   (B) Template: admin-curated, browsable list (GET /templates), student starts one as-is
    //       (POST /templates/{id}/start).
    // Both hand back the exact same TestAttemptStartResponseDto shape that TestAttemptsController's
    // answer/submit/resume endpoints then operate on identically regardless of which path was used.
    [ApiController]
    [Route("api/practice-tests")]
    public class PracticeTestsController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly ITestAttemptService _attemptService;

        public PracticeTestsController(ScoramDbContext db, ITestAttemptService attemptService)
        {
            _db = db;
            _attemptService = attemptService;
        }

        // GET /api/practice-tests/templates?subjectId=&examId=&page=&pageSize= -- published only,
        // public (matches Question Bank search's own "browse without logging in" behavior).
        [HttpGet("templates")]
        public async Task<ActionResult<PagedResult<PracticeTestTemplateDto>>> ListTemplates(
            [FromQuery] Guid? subjectId, [FromQuery] Guid? examId, [FromQuery] List<Guid>? examIds,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.PracticeTestTemplates
                .Include(t => t.Subject).Include(t => t.Topic).Include(t => t.Exam).Include(t => t.Questions)
                .Where(t => t.Status == TestPublishStatus.Published)
                .AsQueryable();

            if (subjectId.HasValue) query = query.Where(t => t.SubjectId == subjectId);
            // Filter precedence (spec section 37): explicit single examId wins over examIds
            // (plural), the "My Exams" default -- same pattern as StudentPapersController.Browse.
            if (examId.HasValue) query = query.Where(t => t.ExamId == examId);
            else if (examIds is { Count: > 0 }) query = query.Where(t => t.ExamId != null && examIds.Contains(t.ExamId.Value));

            query = query.OrderByDescending(t => t.CreatedAt);

            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new PagedResult<PracticeTestTemplateDto>
            {
                Items = items.Select(ToTemplateDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // GET /api/practice-tests/templates/{id}
        [HttpGet("templates/{id:guid}")]
        public async Task<ActionResult<PracticeTestTemplateDto>> GetTemplate(Guid id)
        {
            var template = await _db.PracticeTestTemplates
                .Include(t => t.Subject).Include(t => t.Topic).Include(t => t.Exam).Include(t => t.Questions)
                .FirstOrDefaultAsync(t => t.Id == id && t.Status == TestPublishStatus.Published);
            if (template == null) return NotFound(new { message = "Practice Test not found." });

            return Ok(ToTemplateDto(template));
        }

        // POST /api/practice-tests/generate -- option (A), ad-hoc. Always produces a fresh
        // TestAttemptStatus.InProgress attempt; there's no "reuse an existing ad-hoc attempt" concept
        // the way Mock Tests dedupe in-progress attempts (see the unique index on
        // (UserId, MockTestId, Status) in ScoramDbContext) -- generating again is always allowed.
        [HttpPost("generate")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<TestAttemptStartResponseDto>> Generate(PracticeTestGenerateDto dto)
        {
            var validationError = ValidateGenerateRequest(dto.QuestionCount, dto.DurationMinutes, dto.NegativeMarkingRatio);
            if (validationError != null) return BadRequest(new { message = validationError });

            DifficultyLevel? difficulty = null;
            if (!string.IsNullOrWhiteSpace(dto.Difficulty))
            {
                if (!Enum.TryParse<DifficultyLevel>(dto.Difficulty, true, out var parsed))
                    return BadRequest(new { message = $"'{dto.Difficulty}' isn't a valid difficulty." });
                difficulty = parsed;
            }

            var userId = User.GetUserId();
            var languageFilter = MockTestsController.ParseLanguage(dto.Language);
            var refs = await _attemptService.SelectPracticeQuestionsAsync(
                _db, userId, dto.SubjectId, dto.TopicId, dto.ExamId, dto.YearFrom, dto.YearTo, difficulty, dto.QuestionCount, languageFilter);

            if (refs.Count == 0)
                return BadRequest(new { message = "No questions match those filters yet. Try widening Subject/Topic/Exam/Year/Difficulty." });

            var answers = await _attemptService.BuildSnapshotAnswersAsync(_db, refs.Select(r => new QuestionRef(r.QuestionId, r.QuestionBankQuestionId, r.Order)));
            if (dto.IsRandomOrder) Reorder(answers);

            var attempt = new StudentTestResult
            {
                TestKind = TestKind.Practice,
                UserId = userId,
                PracticeSubjectId = dto.SubjectId,
                PracticeTopicId = dto.TopicId,
                PracticeExamId = dto.ExamId,
                PracticeYearFrom = dto.YearFrom,
                PracticeYearTo = dto.YearTo,
                PracticeDifficulty = difficulty,
                PracticeDurationMinutes = dto.DurationMinutes,
                NegativeMarkingRatio = dto.NegativeMarkingRatio,
                Status = TestAttemptStatus.InProgress,
                StartedAt = DateTime.UtcNow
            };
            foreach (var a in answers) attempt.Answers.Add(a);

            _db.StudentTestResults.Add(attempt);
            await _db.SaveChangesAsync();

            return Ok(TestAttemptsController.ToStartResponse(attempt));
        }

        // POST /api/practice-tests/templates/{id}/start -- option (B). Curated (has fixed Questions)
        // uses that exact list; FilterBased (no fixed Questions) generates a fresh pool from the
        // template's own stored filters every time, same as Generate above.
        [HttpPost("templates/{id:guid}/start")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<TestAttemptStartResponseDto>> StartFromTemplate(Guid id)
        {
            var template = await _db.PracticeTestTemplates
                .Include(t => t.Questions)
                .FirstOrDefaultAsync(t => t.Id == id && t.Status == TestPublishStatus.Published);
            if (template == null) return NotFound(new { message = "Practice Test not found." });

            var userId = User.GetUserId();
            List<QuestionRef> refs;
            if (template.Questions.Count > 0)
            {
                refs = template.Questions
                    .OrderBy(q => q.QuestionOrder)
                    .Select((q, i) => new QuestionRef(q.QuestionId, q.QuestionBankQuestionId, i + 1))
                    .ToList();
            }
            else
            {
                refs = await _attemptService.SelectPracticeQuestionsAsync(
                    _db, userId, template.SubjectId, template.TopicId, template.ExamId,
                    template.YearFrom, template.YearTo, template.Difficulty, template.QuestionCount);
                if (refs.Count == 0)
                    return BadRequest(new { message = "This Practice Test's question pool is empty right now. Please try again later." });
            }

            var answers = await _attemptService.BuildSnapshotAnswersAsync(_db, refs);
            if (template.IsRandomOrder) Reorder(answers);

            var attempt = new StudentTestResult
            {
                TestKind = TestKind.Practice,
                UserId = userId,
                PracticeTestTemplateId = template.Id,
                NegativeMarkingRatio = template.NegativeMarkingRatio,
                Status = TestAttemptStatus.InProgress,
                StartedAt = DateTime.UtcNow
            };
            foreach (var a in answers) attempt.Answers.Add(a);

            _db.StudentTestResults.Add(attempt);
            await _db.SaveChangesAsync();

            attempt.PracticeTestTemplate = template; // avoid a round-trip reload just for the title/duration
            return Ok(TestAttemptsController.ToStartResponse(attempt));
        }

        // ---------- helpers ----------

        private static string? ValidateGenerateRequest(int questionCount, int durationMinutes, decimal negativeMarkingRatio)
        {
            if (questionCount < 1 || questionCount > 200) return "Number of Questions must be between 1 and 200.";
            if (durationMinutes < 1 || durationMinutes > 600) return "Duration must be between 1 and 600 minutes.";
            if (negativeMarkingRatio < 0 || negativeMarkingRatio > 1) return "Negative marking ratio must be between 0 and 1.";
            return null;
        }

        // Shuffles DISPLAY order only (QuestionOrder), never which questions are included.
        private static void Reorder(List<StudentAnswer> answers)
        {
            var shuffled = answers.OrderBy(_ => Random.Shared.Next()).ToList();
            for (var i = 0; i < shuffled.Count; i++) shuffled[i].QuestionOrder = i + 1;
        }

        private static PracticeTestTemplateDto ToTemplateDto(PracticeTestTemplate t) => new PracticeTestTemplateDto
        {
            Id = t.Id,
            Title = t.Title,
            Description = t.Description,
            Subject = t.Subject?.Name,
            Topic = t.Topic?.Name,
            ExamName = t.Exam?.Name,
            YearFrom = t.YearFrom,
            YearTo = t.YearTo,
            Difficulty = t.Difficulty?.ToString(),
            QuestionCount = t.Questions.Count > 0 ? t.Questions.Count : t.QuestionCount,
            DurationMinutes = t.DurationMinutes,
            NegativeMarkingRatio = t.NegativeMarkingRatio,
            IsCurated = t.Questions.Count > 0
        };
    }
}
