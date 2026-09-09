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

        // VISIBILITY FIX -- IQuestionBankMirrorService mirrors a PYP question into the Question Bank
        // the moment it's created (QuestionsController.Create), while its Paper is still Draft --
        // Create() only ever runs on a Draft paper (see the BadRequest guard there), and the mirror
        // row defaults to IsActive = true with nothing else gating it. That means every student-facing
        // endpoint below, filtering on IsActive alone, was showing unreviewed Draft/PendingReview
        // paper content in the public "PYQs" page before an admin ever clicked Publish -- exactly the
        // leak BackfillQuestionBankMirrors's own comment says shouldn't happen ("a Draft/PendingReview
        // paper's questions shouldn't leak into Question Bank search before the paper itself is
        // public"). This is also very likely why subject/exam counts on this page can look "off" --
        // they were including whatever's mid-edit in Draft right now, not just reviewed, live content.
        //
        // Fixed at query time rather than by flipping IsActive on Publish, so it self-corrects through
        // Unpublish -> edit -> Publish cycles automatically and never needs a migration: a bank
        // question is visible here when EITHER it was never mirrored from any Paper question at all
        // (added straight to the Bank -- always visible, unaffected by any Paper's status) OR at least
        // one of its mirror-source Questions belongs to a Published paper. IsActive is untouched and
        // still means exactly what it always did (an admin's own manual enable/disable of a Bank
        // question), just ANDed with this new "not still-only-a-draft" condition.
        private IQueryable<QuestionBankQuestion> VisibleQuestions() =>
            _db.QuestionBankQuestions.Where(x => x.IsActive
                && (!_db.Questions.Any(src => src.MirroredToQuestionBankQuestionId == x.Id)
                    || _db.Questions.Any(src => src.MirroredToQuestionBankQuestionId == x.Id && src.Paper!.Status == PaperStatus.Published)));

        // GET /api/question-bank/search?search=&subjectIds=&topicIds=&examIds=&years=&languages=&page=&pageSize=
        // Server-side search + filtering + pagination throughout (section 15/16) -- never loads the
        // whole table into memory, works the same whether the bank has 500 questions or 500,000.
        [HttpGet("search")]
        public async Task<ActionResult<PagedResult<QuestionBankQuestionResponseDto>>> Search([FromQuery] QuestionBankSearchQuery query)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);

            var q = VisibleQuestions();

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                // Supports both a short keyword ("Harappa") and a fully-pasted question (section 2) --
                // both are just a Contains() against the raw text; NormalizedQuestionText isn't used
                // here since Contains needs to match mid-word/mid-punctuation too, not just exact
                // normalized equality (that's reserved for duplicate detection).
                var term = query.Search.Trim();
                q = q.Where(x => EF.Functions.Like(x.QuestionText, $"%{term}%"));
            }

            // Multi-select: each non-empty filter narrows the result (AND across filters), matching
            // ANY of its own selected values (OR within that one filter) -- e.g. examIds=[SSC CGL,
            // RRB NTPC] AND subjectIds=[Reasoning] returns Reasoning questions asked in either exam.
            // A student who only ever picks one value per filter (the old single-select experience)
            // gets identical results to before -- .Contains() against a 1-item list is just "==".
            if (query.SubjectIds is { Count: > 0 } subjectIds)
                q = q.Where(x => subjectIds.Contains(x.SubjectId));
            if (query.TopicIds is { Count: > 0 } topicIds)
                q = q.Where(x => topicIds.Contains(x.TopicId));
            if (query.ExamIds is { Count: > 0 } examIds)
                q = q.Where(x => x.ExamMappings.Any(m => examIds.Contains(m.ExamId)));
            if (query.Years is { Count: > 0 } years)
                q = q.Where(x => x.ExamMappings.Any(m => years.Contains(m.Year)));
            if (query.Languages is { Count: > 0 } rawLanguages)
            {
                var languageFilters = rawLanguages
                    .Select(l => Enum.TryParse<PaperLanguage>(l, ignoreCase: true, out var parsed) ? (PaperLanguage?)parsed : null)
                    .Where(l => l.HasValue)
                    .Select(l => l!.Value)
                    .ToList();
                if (languageFilters.Count > 0)
                    q = q.Where(x => x.Language != null && languageFilters.Contains(x.Language.Value));
            }

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
            var question = await VisibleQuestions()
                .Include(x => x.Subject)
                .Include(x => x.Topic)
                .Include(x => x.ExamMappings).ThenInclude(m => m.Exam)
                .Include(x => x.Solutions)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (question == null) return NotFound(new { message = "Question not found." });

            var dto = MapToResponseDto(question);
            dto.LikeCount = await _db.QuestionVotes.CountAsync(v => v.QuestionBankQuestionId == id && v.IsLike);
            dto.DislikeCount = await _db.QuestionVotes.CountAsync(v => v.QuestionBankQuestionId == id && !v.IsLike);
            dto.CommentCount = await _db.QuestionComments.CountAsync(c => c.QuestionBankQuestionId == id);
            if (User.Identity?.IsAuthenticated == true)
            {
                var myVote = await _db.QuestionVotes.FirstOrDefaultAsync(v => v.QuestionBankQuestionId == id && v.UserId == User.GetUserId());
                dto.MyVote = myVote?.IsLike;
                dto.IsBookmarked = await _db.Bookmarks.AnyAsync(b => b.QuestionBankQuestionId == id && b.UserId == User.GetUserId());
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
            var questionExists = await VisibleQuestions().AnyAsync(q => q.Id == id);
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
                    // Same visibility rule as VisibleQuestions() above (kept inline here since this
                    // runs inside a Select projection over QuestionBankSubjects, not QuestionBankQuestions) --
                    // otherwise a subject's count would include questions still sitting in an
                    // unpublished paper's Draft, which is exactly the leak this fixes.
                    QuestionCount = s.Questions.Count(q => q.IsActive
                        && (!_db.Questions.Any(src => src.MirroredToQuestionBankQuestionId == q.Id)
                            || _db.Questions.Any(src => src.MirroredToQuestionBankQuestionId == q.Id && src.Paper!.Status == PaperStatus.Published)))
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
                    // Same visibility rule as VisibleQuestions() above, see GetSubjects's own comment
                    // on why this is inlined here instead of reused directly.
                    QuestionCount = t.Questions.Count(q => q.IsActive
                        && (!_db.Questions.Any(src => src.MirroredToQuestionBankQuestionId == q.Id)
                            || _db.Questions.Any(src => src.MirroredToQuestionBankQuestionId == q.Id && src.Paper!.Status == PaperStatus.Published)))
                })
                .ToListAsync();
            return Ok(topics);
        }

        [HttpGet("exams")]
        public async Task<ActionResult<List<object>>> GetExams()
        {
            // Every exam that's actually tagged on at least one VISIBLE Question Bank question -- not
            // the full Exams master list (so the dropdown doesn't show exams with zero visible
            // results in this feature), and not just "at least one mapping" as before, which could
            // surface an exam whose only tagged question is still sitting in a Draft paper (see
            // VisibleQuestions' own comment). Reuses the existing Exam picklist (Models/Exam.cs)
            // rather than a second one.
            var exams = await _db.QuestionBankExamMappings
                .Where(m => VisibleQuestions().Any(v => v.Id == m.QuestionBankQuestionId))
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
            // Same visibility rule as GetExams above -- a year should only show up here if it has at
            // least one question a student can actually see.
            var years = await _db.QuestionBankExamMappings
                .Where(m => VisibleQuestions().Any(v => v.Id == m.QuestionBankQuestionId))
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
            QuestionImageUrl = x.QuestionImageUrl,
            OptionA = x.OptionA,
            OptionAImageUrl = x.OptionAImageUrl,
            OptionB = x.OptionB,
            OptionBImageUrl = x.OptionBImageUrl,
            OptionC = x.OptionC,
            OptionCImageUrl = x.OptionCImageUrl,
            OptionD = x.OptionD,
            OptionDImageUrl = x.OptionDImageUrl,
            CorrectOption = x.CorrectOption.ToString(),
            Explanation = x.Explanation,
            ExplanationImageUrl = x.ExplanationImageUrl,
            ContentBlocks = ContentBlocksJsonHelper.Parse(x.ContentBlocksJson),
            Subject = x.Subject?.Name ?? string.Empty,
            Topic = x.Topic?.Name ?? string.Empty,
            SourceReference = x.SourceReference,
            Language = x.Language?.ToString(),
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
