using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QuestionsController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IFileStorageService _fileStorage;
        private readonly IInstantSearchService _instantSearch;
        private readonly IFallbackSearchService _fallbackSearch;
        private readonly IMemoryCache _cache;
        private readonly ILogger<QuestionsController> _logger;
        private readonly IAuditLogService _audit;
        private readonly IQuestionBankMirrorService _mirror;

        public QuestionsController(
            ScoramDbContext db, IAdminPermissionService permissions, IFileStorageService fileStorage,
            IInstantSearchService instantSearch, IFallbackSearchService fallbackSearch, IMemoryCache cache,
            ILogger<QuestionsController> logger, IAuditLogService audit, IQuestionBankMirrorService mirror)
        {
            _db = db;
            _permissions = permissions;
            _fileStorage = fileStorage;
            _instantSearch = instantSearch;
            _fallbackSearch = fallbackSearch;
            _cache = cache;
            _logger = logger;
            _audit = audit;
            _mirror = mirror;
        }

        // GET /api/questions/today -- "Today's Challenge" on the student home page. Deterministic pick
        // (same question all day, for every student, rotating daily) rather than per-user state, so
        // there's nothing to track and the result is trivially cacheable -- with potentially thousands
        // of students hitting the home page, recomputing this per-request would be wasteful for a value
        // that's identical for everyone until midnight UTC.
        [HttpGet("today")]
        public async Task<ActionResult<QuestionDetailDto>> GetTodaysChallenge()
        {
            var cacheKey = $"todays-challenge-{DateTime.UtcNow:yyyy-MM-dd}";
            if (_cache.TryGetValue(cacheKey, out QuestionDetailDto? cached) && cached != null)
                return Ok(cached);

            var publishedIds = await _db.Questions
                .Where(q => q.PaperId != null && q.Paper!.Status == PaperStatus.Published)
                .Select(q => q.Id)
                .ToListAsync();

            if (publishedIds.Count == 0)
                return NotFound(new { message = "No published questions yet." });

            // Deterministic, not random -- the same seed always picks the same index for a given day,
            // so every student (and every server instance, if this ever runs behind more than one)
            // sees the identical "today's challenge" without needing shared/distributed state.
            var seed = DateTime.UtcNow.Date.DayOfYear + DateTime.UtcNow.Year * 1000;
            var questionId = publishedIds[seed % publishedIds.Count];

            var question = await _db.Questions
                .Include(q => q.Solutions)
                .Include(q => q.Paper).ThenInclude(p => p!.Exam)
                .FirstAsync(q => q.Id == questionId);

            var dto = MapToDetailDto(question);
            _cache.Set(cacheKey, dto, TimeSpan.FromHours(6));
            return Ok(dto);
        }

        // GET /api/questions/instant-search?q=... -- the student-facing search bar. Backed by
        // Meilisearch (typo-tolerant, fast even with a large question bank) rather than the database --
        // see Services/InstantSearchService.cs. Only ever contains Published-paper questions, since
        // that's the only thing ever indexed (see PapersController's publish/unpublish/delete hooks).
        //
        // Falls back to a SQL-based search (Services/FallbackSearchService.cs -- Full-Text Search, then
        // plain LIKE) if Meilisearch is unreachable, rather than failing the request outright. Results
        // are cached briefly regardless of which backend answered: popular queries (a trending exam
        // name right after results are announced, say) shouldn't need a fresh round trip every time
        // within the same few seconds.
        [HttpGet("instant-search")]
        public async Task<ActionResult<List<QuestionSearchDocument>>> InstantSearch([FromQuery] string q, [FromQuery] int limit = 20)
        {
            if (string.IsNullOrWhiteSpace(q)) return Ok(new List<QuestionSearchDocument>());

            var normalizedQuery = q.Trim().ToLowerInvariant();
            var cacheKey = $"instant-search:{normalizedQuery}:{limit}";
            if (_cache.TryGetValue(cacheKey, out List<QuestionSearchDocument>? cached) && cached != null)
                return Ok(cached);

            try
            {
                var results = await _instantSearch.SearchAsync(q, Math.Clamp(limit, 1, 50));
                _cache.Set(cacheKey, results, TimeSpan.FromSeconds(30));
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Meilisearch unreachable, falling back to SQL for query {Query}", q);
            }

            try
            {
                var (results, source) = await _fallbackSearch.SearchAsync(q, Math.Clamp(limit, 1, 50));
                _logger.LogInformation("Instant search answered by fallback ({Source}) for query {Query}", source, q);
                _cache.Set(cacheKey, results, TimeSpan.FromSeconds(30));
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Both Meilisearch and the SQL fallback failed for query {Query}", q);
                return StatusCode(502, new { message = "Search is temporarily unavailable. Try again in a moment." });
            }
        }

        // GET /api/questions -- student-facing search. Only ever returns questions belonging to a
        // Published paper, or legacy questions with no PaperId (created before the Paper/review system
        // existed, so there was never a Draft/PendingReview state for them to be stuck in).
        [HttpGet]
        public async Task<ActionResult<PagedResult<QuestionResponseDto>>> Search([FromQuery] QuestionSearchQuery query)
        {
            var q = _db.Questions
                .Include(x => x.Exam)
                .Include(x => x.Paper).ThenInclude(p => p!.Exam)
                .Where(x => x.PaperId == null || x.Paper!.Status == PaperStatus.Published)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.ExamName))
                q = q.Where(x => x.ExamName == query.ExamName || (x.Paper != null && x.Paper.Exam!.Name == query.ExamName));

            if (query.ExamId.HasValue)
                q = q.Where(x => x.ExamId == query.ExamId || (x.Paper != null && x.Paper.ExamId == query.ExamId));

            if (query.PaperId.HasValue)
                q = q.Where(x => x.PaperId == query.PaperId);

            if (query.Year.HasValue)
                q = q.Where(x => (x.PaperId == null && x.Year == query.Year) || (x.Paper != null && x.Paper.Year == query.Year));

            if (!string.IsNullOrWhiteSpace(query.PaperCode))
                q = q.Where(x => x.Paper != null && x.Paper.PaperCode != null && x.Paper.PaperCode.Contains(query.PaperCode));

            if (query.QuestionNumber.HasValue)
                q = q.Where(x => x.QuestionNumber == query.QuestionNumber);

            if (!string.IsNullOrWhiteSpace(query.Language))
            {
                var languageParsed = Enum.TryParse<PaperLanguage>(query.Language, true, out var parsedLang) ? (PaperLanguage?)parsedLang : null;
                q = q.Where(x =>
                    (x.PaperId == null && x.Language == query.Language) ||
                    (x.Paper != null && languageParsed != null && x.Paper.Language == languageParsed));
            }

            if (!string.IsNullOrWhiteSpace(query.Subject))
                q = q.Where(x => x.Subject == query.Subject);

            if (!string.IsNullOrWhiteSpace(query.Topic))
                q = q.Where(x => x.Topic == query.Topic);

            if (query.DifficultyLevel.HasValue)
                q = q.Where(x => x.DifficultyLevel == query.DifficultyLevel);

            if (!string.IsNullOrWhiteSpace(query.Keyword))
                q = q.Where(x => x.QuestionText.Contains(query.Keyword));

            var totalCount = await q.CountAsync();

            var page = Math.Max(query.Page, 1);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);

            // Viewing one specific paper (query.PaperId set) reconstructs it in its original
            // Question No. order; a general search across many papers stays newest-first.
            var ordered = query.PaperId.HasValue
                ? q.OrderBy(x => x.QuestionNumber)
                : q.OrderByDescending(x => x.CreatedAt);

            var entities = await ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new PagedResult<QuestionResponseDto>
            {
                Items = entities.Select(MapToResponseDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // GET /api/questions/8f14e45f-ceea-467e-add1-000000000001
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<QuestionDetailDto>> GetById(Guid id)
        {
            var question = await _db.Questions
                .Include(x => x.Solutions)
                .Include(x => x.Exam)
                .Include(x => x.Paper).ThenInclude(p => p!.Exam)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (question == null) return NotFound();

            // Same visibility rule as Search -- a direct/shared link to a question in a Draft or
            // PendingReview paper shouldn't work for students either.
            if (question.PaperId != null && question.Paper!.Status != PaperStatus.Published)
                return NotFound();

            var dto = MapToDetailDto(question);

            // Like/Dislike counts + the current viewer's own vote (SCORAM_QUESTION_BANK's
            // QuestionVote table, reused here for the legacy question too -- see Models/QuestionModels.cs).
            dto.LikeCount = await _db.QuestionVotes.CountAsync(v => v.QuestionId == id && v.IsLike);
            dto.DislikeCount = await _db.QuestionVotes.CountAsync(v => v.QuestionId == id && !v.IsLike);
            if (User.Identity?.IsAuthenticated == true)
            {
                var myVote = await _db.QuestionVotes.FirstOrDefaultAsync(v => v.QuestionId == id && v.UserId == User.GetUserId());
                dto.MyVote = myVote?.IsLike;
            }

            return Ok(dto);
        }

        // POST /api/questions  (Admin only, requires UploadPaper permission)
        // multipart/form-data -- see QuestionCreateDto for the six optional image slots.
        [HttpPost]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<QuestionDetailDto>> Create([FromForm] QuestionCreateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper))
                return Forbid();

            var paper = await _db.Papers.FindAsync(dto.PaperId);
            if (paper == null)
                return BadRequest(new { message = "That paper doesn't exist. Create it first via POST /api/admin/papers." });

            if (paper.Status != PaperStatus.Draft)
                return BadRequest(new { message = NotEditableMessage(paper.Status) });

            var duplicateNumber = await _db.Questions.AnyAsync(x => x.PaperId == dto.PaperId && x.QuestionNumber == dto.QuestionNumber);
            if (duplicateNumber)
                return Conflict(new { message = $"Question number {dto.QuestionNumber} already exists in this paper." });

            var question = new Question
            {
                PaperId = dto.PaperId,
                QuestionNumber = dto.QuestionNumber,
                Subject = dto.Subject,
                Topic = dto.Topic,
                DifficultyLevel = dto.DifficultyLevel,
                QuestionText = dto.QuestionText,
                OptionA = dto.OptionA,
                OptionB = dto.OptionB,
                OptionC = dto.OptionC,
                OptionD = dto.OptionD,
                CorrectOption = dto.CorrectOption,
                Explanation = dto.Explanation,
                SourceReference = dto.SourceReference,
                CreatedByAdminId = User.GetAdminId(),
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                question.QuestionImageUrl = await _fileStorage.SaveImageAsync(dto.QuestionImage, "question-images");
                question.OptionAImageUrl = await _fileStorage.SaveImageAsync(dto.OptionAImage, "question-images");
                question.OptionBImageUrl = await _fileStorage.SaveImageAsync(dto.OptionBImage, "question-images");
                question.OptionCImageUrl = await _fileStorage.SaveImageAsync(dto.OptionCImage, "question-images");
                question.OptionDImageUrl = await _fileStorage.SaveImageAsync(dto.OptionDImage, "question-images");
                question.ExplanationImageUrl = await _fileStorage.SaveImageAsync(dto.ExplanationImage, "question-images");
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            _db.Questions.Add(question);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Safety net for a race between two concurrent uploads landing on the same Q.No --
                // the pre-check above catches the common case, this catches the rest.
                return Conflict(new { message = $"Question number {dto.QuestionNumber} already exists in this paper." });
            }

            // Auto-mirror into the Question Bank (see IQuestionBankMirrorService) -- a separate
            // SaveChangesAsync so a mirror hiccup can never roll back the PYQ question that just
            // succeeded above.
            var mirrorId = await _mirror.MirrorFromPyqAsync(_db, question, paper.ExamId, paper.Year, User.GetAdminId());
            if (mirrorId.HasValue)
            {
                question.MirroredToQuestionBankQuestionId = mirrorId;
                try { await _db.SaveChangesAsync(); } catch { /* non-critical, see MirrorFromPyqAsync's own comment */ }
            }

            var saved = await _db.Questions.Include(x => x.Paper).ThenInclude(p => p!.Exam).FirstAsync(x => x.Id == question.Id);
            return CreatedAtAction(nameof(GetById), new { id = question.Id }, MapToDetailDto(saved));
        }

        // PATCH /api/questions/{id}  (Admin only, requires EditPaper permission)
        // Works while the paper is Draft or PendingReview -- a Published paper must be unpublished
        // first (see PapersController.Unpublish). PendingReview is allowed so a reviewer (or the
        // submitter) can fix a small mistake -- wrong option, missing explanation -- without having to
        // Reject the whole paper and lose the review queue position over it.
        [HttpPatch("{id:guid}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<QuestionDetailDto>> Update(Guid id, [FromForm] QuestionUpdateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.EditPaper))
                return Forbid();

            var question = await _db.Questions
                .Include(x => x.Paper).ThenInclude(p => p!.Exam)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (question == null) return NotFound();

            if (question.Paper == null)
                return BadRequest(new { message = "This question predates the Paper system and can't be edited here." });

            if (question.Paper.Status != PaperStatus.Draft && question.Paper.Status != PaperStatus.PendingReview)
                return BadRequest(new { message = NotEditableMessage(question.Paper.Status) });

            if (dto.QuestionNumber != question.QuestionNumber)
            {
                var duplicateNumber = await _db.Questions.AnyAsync(x =>
                    x.PaperId == question.PaperId && x.QuestionNumber == dto.QuestionNumber && x.Id != id);
                if (duplicateNumber)
                    return Conflict(new { message = $"Question number {dto.QuestionNumber} already exists in this paper." });
            }

            question.QuestionNumber = dto.QuestionNumber;
            question.Subject = dto.Subject;
            question.Topic = dto.Topic;
            question.DifficultyLevel = dto.DifficultyLevel;
            question.QuestionText = dto.QuestionText;
            question.OptionA = dto.OptionA;
            question.OptionB = dto.OptionB;
            question.OptionC = dto.OptionC;
            question.OptionD = dto.OptionD;
            question.CorrectOption = dto.CorrectOption;
            question.Explanation = dto.Explanation;
            question.SourceReference = dto.SourceReference;

            try
            {
                question.QuestionImageUrl = await ApplyImageUpdate(dto.QuestionImage, dto.RemoveQuestionImage, question.QuestionImageUrl);
                question.OptionAImageUrl = await ApplyImageUpdate(dto.OptionAImage, dto.RemoveOptionAImage, question.OptionAImageUrl);
                question.OptionBImageUrl = await ApplyImageUpdate(dto.OptionBImage, dto.RemoveOptionBImage, question.OptionBImageUrl);
                question.OptionCImageUrl = await ApplyImageUpdate(dto.OptionCImage, dto.RemoveOptionCImage, question.OptionCImageUrl);
                question.OptionDImageUrl = await ApplyImageUpdate(dto.OptionDImage, dto.RemoveOptionDImage, question.OptionDImageUrl);
                question.ExplanationImageUrl = await ApplyImageUpdate(dto.ExplanationImage, dto.RemoveExplanationImage, question.ExplanationImageUrl);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            await _db.SaveChangesAsync();

            if (question.MirroredToQuestionBankQuestionId.HasValue)
            {
                await _mirror.SyncMirrorAsync(_db, question.MirroredToQuestionBankQuestionId.Value, question);
                try { await _db.SaveChangesAsync(); } catch { /* non-critical, see SyncMirrorAsync's own comment */ }
            }

            return Ok(MapToDetailDto(question));
        }

        // DELETE /api/questions/{id}  (Admin only, requires DeletePaper permission)
        // Same Draft-or-PendingReview rule as Update -- see the comment there.
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.DeletePaper))
                return Forbid();

            var question = await _db.Questions.Include(x => x.Paper).FirstOrDefaultAsync(x => x.Id == id);
            if (question == null) return NotFound();

            if (question.Paper != null && question.Paper.Status != PaperStatus.Draft && question.Paper.Status != PaperStatus.PendingReview)
                return BadRequest(new { message = NotEditableMessage(question.Paper.Status) });

            var imageUrls = new[]
            {
                question.QuestionImageUrl, question.OptionAImageUrl, question.OptionBImageUrl,
                question.OptionCImageUrl, question.OptionDImageUrl, question.ExplanationImageUrl
            };

            _db.Questions.Remove(question);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "Question.Delete", "Question", id);

            foreach (var url in imageUrls) _fileStorage.DeleteImage(url);

            return NoContent();
        }

        private static string NotEditableMessage(PaperStatus status) =>
            status == PaperStatus.Published
                ? "This paper is Published and can't be edited directly -- unpublish it first."
                : $"This paper is {status} and can't be edited right now.";

        private async Task<string?> ApplyImageUpdate(IFormFile? newFile, bool remove, string? currentUrl)
        {
            if (newFile != null)
            {
                var newUrl = await _fileStorage.SaveImageAsync(newFile, "question-images");
                _fileStorage.DeleteImage(currentUrl);
                return newUrl;
            }
            if (remove)
            {
                _fileStorage.DeleteImage(currentUrl);
                return null;
            }
            return currentUrl;
        }

        // Shared by Search/GetById here and PapersController.GetById -- prefers Paper-sourced
        // exam/year/language when the question has a Paper, falling back to the legacy flat fields for
        // questions created before the Paper system existed.
        public static QuestionResponseDto MapToResponseDto(Question x) => new QuestionResponseDto
        {
            Id = x.Id,
            ExamId = x.PaperId != null ? x.Paper?.ExamId : x.ExamId,
            ExamName = (x.PaperId != null ? x.Paper?.Exam?.Name : x.ExamName) ?? string.Empty,
            ExamLogoUrl = x.PaperId != null ? x.Paper?.Exam?.LogoUrl : x.Exam?.LogoUrl,
            Language = x.PaperId != null ? x.Paper?.Language.ToString() : x.Language,
            Year = x.PaperId != null ? (x.Paper?.Year ?? 0) : x.Year,
            PaperId = x.PaperId,
            QuestionNumber = x.QuestionNumber,
            Subject = x.Subject,
            Topic = x.Topic,
            DifficultyLevel = x.DifficultyLevel.ToString(),
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
            SolutionCount = x.Solutions?.Count ?? 0
        };

        public static QuestionDetailDto MapToDetailDto(Question x)
        {
            var baseDto = MapToResponseDto(x);
            return new QuestionDetailDto
            {
                Id = baseDto.Id,
                ExamId = baseDto.ExamId,
                ExamName = baseDto.ExamName,
                ExamLogoUrl = baseDto.ExamLogoUrl,
                Language = baseDto.Language,
                Year = baseDto.Year,
                PaperId = baseDto.PaperId,
                QuestionNumber = baseDto.QuestionNumber,
                Subject = baseDto.Subject,
                Topic = baseDto.Topic,
                DifficultyLevel = baseDto.DifficultyLevel,
                QuestionText = baseDto.QuestionText,
                QuestionImageUrl = baseDto.QuestionImageUrl,
                OptionA = baseDto.OptionA,
                OptionAImageUrl = baseDto.OptionAImageUrl,
                OptionB = baseDto.OptionB,
                OptionBImageUrl = baseDto.OptionBImageUrl,
                OptionC = baseDto.OptionC,
                OptionCImageUrl = baseDto.OptionCImageUrl,
                OptionD = baseDto.OptionD,
                OptionDImageUrl = baseDto.OptionDImageUrl,
                SolutionCount = baseDto.SolutionCount,
                CorrectOption = x.CorrectOption.ToString(),
                Explanation = x.Explanation,
                ExplanationImageUrl = x.ExplanationImageUrl,
                SourceReference = x.SourceReference
            };
        }
    }
}
