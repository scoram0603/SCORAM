using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Public, read-only side of the Question Bank (section 2-6, 16, 19-20 of the spec). Deliberately
    // separate from QuestionsController/PapersController -- this is the individual-question search
    // engine, independent of the full-paper PYP upload flow (section 21). No [Authorize] here: any
    // visitor can search, same as the existing /api/questions search.
    [ApiController]
    [Route("api/question-bank")]
    public class QuestionBankController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IGamificationService _gamification;

        public QuestionBankController(ScoramDbContext db, IGamificationService gamification)
        {
            _db = db;
            _gamification = gamification;
        }

        // GET /api/question-bank/search?search=&subjectId=&topicId=&examId=&year=&page=&pageSize=
        // Server-side search + filtering + pagination throughout (section 15/16) -- never loads the
        // whole table into memory, works the same whether the bank has 500 questions or 500,000.
        [HttpGet("search")]
        public async Task<ActionResult<PagedResult<QuestionBankQuestionResponseDto>>> Search([FromQuery] QuestionBankSearchQuery query)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);

            var q = _db.QuestionBankQuestions
                .Where(x => x.IsActive)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                // Supports both a short keyword ("Harappa") and a fully-pasted question (section 2) --
                // both are just a Contains() against the raw text; NormalizedQuestionText isn't used
                // here since Contains needs to match mid-word/mid-punctuation too, not just exact
                // normalized equality (that's reserved for duplicate detection).
                var term = query.Search.Trim();
                q = q.Where(x => EF.Functions.Like(x.QuestionText, $"%{term}%"));
            }

            if (query.SubjectId.HasValue) q = q.Where(x => x.SubjectId == query.SubjectId);
            if (query.TopicId.HasValue) q = q.Where(x => x.TopicId == query.TopicId);
            if (query.ExamId.HasValue) q = q.Where(x => x.ExamMappings.Any(m => m.ExamId == query.ExamId));
            if (query.Year.HasValue) q = q.Where(x => x.ExamMappings.Any(m => m.Year == query.Year));

            q = q.OrderByDescending(x => x.CreatedAt);

            var totalCount = await q.CountAsync();
            var items = await q
                .Include(x => x.Subject)
                .Include(x => x.Topic)
                .Include(x => x.ExamMappings).ThenInclude(m => m.Exam)
                .Include(x => x.Solutions)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // FEED REDESIGN -- like/dislike/comment counts (and the caller's own vote) are now shown
            // directly on the search results, not just on the single-question detail page (see
            // GetById below, which used to be the only place this was computed). Batched as grouped
            // queries against just this page's IDs rather than one query per card, so a 100-item page
            // costs 3-4 extra queries total, not 100.
            var ids = items.Select(x => x.Id).ToList();
            var likeCounts = await _db.QuestionVotes
                .Where(v => ids.Contains(v.QuestionBankQuestionId!.Value) && v.IsLike)
                .GroupBy(v => v.QuestionBankQuestionId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Id, g => g.Count);
            var dislikeCounts = await _db.QuestionVotes
                .Where(v => ids.Contains(v.QuestionBankQuestionId!.Value) && !v.IsLike)
                .GroupBy(v => v.QuestionBankQuestionId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Id, g => g.Count);
            var commentCounts = await _db.QuestionComments
                .Where(c => c.QuestionBankQuestionId != null && ids.Contains(c.QuestionBankQuestionId.Value))
                .GroupBy(c => c.QuestionBankQuestionId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Id, g => g.Count);

            Dictionary<Guid, bool> myVotes = new();
            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = User.GetUserId();
                myVotes = await _db.QuestionVotes
                    .Where(v => ids.Contains(v.QuestionBankQuestionId!.Value) && v.UserId == userId)
                    .ToDictionaryAsync(v => v.QuestionBankQuestionId!.Value, v => v.IsLike);
            }

            var mapped = items.Select(MapToResponseDto).ToList();
            foreach (var dto in mapped)
            {
                dto.LikeCount = likeCounts.GetValueOrDefault(dto.Id);
                dto.DislikeCount = dislikeCounts.GetValueOrDefault(dto.Id);
                dto.CommentCount = commentCounts.GetValueOrDefault(dto.Id);
                dto.MyVote = myVotes.TryGetValue(dto.Id, out var vote) ? vote : (bool?)null;
            }

            return Ok(new PagedResult<QuestionBankQuestionResponseDto>
            {
                Items = mapped,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // GET /api/question-bank/{id}
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<QuestionBankQuestionResponseDto>> GetById(Guid id)
        {
            var question = await _db.QuestionBankQuestions
                .Include(x => x.Subject)
                .Include(x => x.Topic)
                .Include(x => x.ExamMappings).ThenInclude(m => m.Exam)
                .Include(x => x.Solutions)
                .FirstOrDefaultAsync(x => x.Id == id && x.IsActive);

            if (question == null) return NotFound(new { message = "Question not found." });

            var dto = MapToResponseDto(question);
            dto.LikeCount = await _db.QuestionVotes.CountAsync(v => v.QuestionBankQuestionId == id && v.IsLike);
            dto.DislikeCount = await _db.QuestionVotes.CountAsync(v => v.QuestionBankQuestionId == id && !v.IsLike);
            dto.CommentCount = await _db.QuestionComments.CountAsync(c => c.QuestionBankQuestionId == id);
            if (User.Identity?.IsAuthenticated == true)
            {
                var myVote = await _db.QuestionVotes.FirstOrDefaultAsync(v => v.QuestionBankQuestionId == id && v.UserId == User.GetUserId());
                dto.MyVote = myVote?.IsLike;
            }

            return Ok(dto);
        }

        // POST /api/question-bank/{id}/solve -- GAMIFICATION: student marks a Question Bank question
        // as solved. This is the only place "solving a question" is tracked anywhere in the app (there
        // was no such record before this module), so a new UserQuestionSolve table backs it. Idempotent
        // per (user, question) -- re-marking an already-solved question just returns the existing
        // record without granting XP/streak credit a second time.
        [HttpPost("{id:guid}/solve")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> MarkSolved(Guid id)
        {
            var questionExists = await _db.QuestionBankQuestions.AnyAsync(q => q.Id == id && q.IsActive);
            if (!questionExists) return NotFound(new { message = "Question not found." });

            var userId = User.GetUserId();
            var already = await _db.UserQuestionSolves.AnyAsync(s => s.UserId == userId && s.QuestionBankQuestionId == id);
            if (already) return Ok(new { alreadySolved = true });

            _db.UserQuestionSolves.Add(new UserQuestionSolve { UserId = userId, QuestionBankQuestionId = id });
            await _db.SaveChangesAsync();

            await _gamification.RecordActivityAsync(
                userId,
                GamificationService.XpFor(GamificationService.Reasons.QuestionSolved),
                GamificationService.Reasons.QuestionSolved);

            return Ok(new { alreadySolved = false });
        }

        // ---------- Filter dropdown data (section 18) — active values only ----------

        [HttpGet("subjects")]
        public async Task<ActionResult<List<QuestionBankSubjectDto>>> GetSubjects()
        {
            var subjects = await _db.QuestionBankSubjects
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .Select(s => new QuestionBankSubjectDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    IsActive = s.IsActive,
                    QuestionCount = s.Questions.Count(q => q.IsActive)
                })
                .ToListAsync();
            return Ok(subjects);
        }

        // GET /api/question-bank/topics?subjectId=... -- Topic dropdown depends on the selected Subject.
        [HttpGet("topics")]
        public async Task<ActionResult<List<QuestionBankTopicDto>>> GetTopics([FromQuery] Guid? subjectId)
        {
            var q = _db.QuestionBankTopics.Where(t => t.IsActive).AsQueryable();
            if (subjectId.HasValue) q = q.Where(t => t.SubjectId == subjectId.Value);

            var topics = await q
                .Include(t => t.Subject)
                .OrderBy(t => t.Name)
                .Select(t => new QuestionBankTopicDto
                {
                    Id = t.Id,
                    SubjectId = t.SubjectId,
                    SubjectName = t.Subject!.Name,
                    Name = t.Name,
                    IsActive = t.IsActive,
                    QuestionCount = t.Questions.Count(q => q.IsActive)
                })
                .ToListAsync();
            return Ok(topics);
        }

        [HttpGet("exams")]
        public async Task<ActionResult<List<object>>> GetExams()
        {
            // Every exam that's actually tagged on at least one Question Bank question -- not the
            // full Exams master list, so the dropdown doesn't show exams with zero results in this
            // feature. Reuses the existing Exam picklist (Models/Exam.cs) rather than a second one.
            var exams = await _db.QuestionBankExamMappings
                .Select(m => m.Exam)
                .Where(e => e != null)
                .Distinct()
                .OrderBy(e => e!.Name)
                .Select(e => new { id = e!.Id, name = e.Name, logoUrl = e.LogoUrl })
                .ToListAsync();
            return Ok(exams);
        }

        [HttpGet("years")]
        public async Task<ActionResult<List<int>>> GetYears()
        {
            var years = await _db.QuestionBankExamMappings
                .Select(m => m.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToListAsync();
            return Ok(years);
        }

        public static QuestionBankQuestionResponseDto MapToResponseDto(Models.QuestionBankQuestion x) => new QuestionBankQuestionResponseDto
        {
            Id = x.Id,
            QuestionText = x.QuestionText,
            OptionA = x.OptionA,
            OptionB = x.OptionB,
            OptionC = x.OptionC,
            OptionD = x.OptionD,
            CorrectOption = x.CorrectOption.ToString(),
            Explanation = x.Explanation,
            Subject = x.Subject?.Name ?? string.Empty,
            Topic = x.Topic?.Name ?? string.Empty,
            SourceReference = x.SourceReference,
            AskedIn = x.ExamMappings
                .OrderByDescending(m => m.Year)
                .Select(m => new QuestionBankExamYearDto
                {
                    ExamId = m.ExamId,
                    ExamName = m.Exam?.Name ?? "Unknown",
                    ExamLogoUrl = m.Exam?.LogoUrl,
                    Year = m.Year
                }).ToList(),
            SolutionCount = x.Solutions?.Count(s => s.IsApproved) ?? 0,
            CreatedAt = x.CreatedAt
        };
    }
}
