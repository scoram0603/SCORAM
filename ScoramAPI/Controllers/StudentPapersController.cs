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
    // Backs the student Previous Year Paper Practice page: Browse/GetFilterOptions power the
    // filterable card grid (spec section 32); GetYears/GetLanguages/GetSets remain for the older
    // step-by-step Exam -> Year -> Language drill-down some callers may still use; MyAttempts backs
    // the "Continue Attempting" / "Completed Papers" tabs. Every endpoint here only ever considers
    // Published papers -- Draft/PendingReview stay invisible to students, same rule as
    // QuestionsController.Search/GetById.
    //
    // MASTER PROMPT -- Previous Year Paper Practice: GetPaper/Start below let a Published, fully
    // CONFIGURED paper (DurationMinutes + NegativeMarkingRatio + RequiredQuestionCount all set, see
    // Paper.IsConfiguredForPractice) be attempted as a real timed paper, reusing the exact same
    // attempt engine as Mock/Practice Tests (see TestAttemptService/TestAttemptsController) instead
    // of building a second one. A paper that ISN'T configured for practice still works exactly as
    // before -- browsable via GetSets/GetYears/GetLanguages + QuestionsController, just not
    // "Start"-able as a timed attempt.
    [ApiController]
    [Route("api/papers")]
    public class StudentPapersController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly ITestAttemptService _attemptService;

        public StudentPapersController(ScoramDbContext db, ITestAttemptService attemptService)
        {
            _db = db;
            _attemptService = attemptService;
        }

        // GET /api/papers -- the main student browse/filter grid (spec section 32, "Search and
        // Filter"). Exam isn't required here (an empty examId just returns everything Published,
        // useful for an initial "browse everything" view) -- every other filter narrows further.
        [HttpGet]
        public async Task<ActionResult<PagedResult<PaperResponseDto>>> Browse(
            [FromQuery] Guid? examId, [FromQuery] List<Guid>? examIds, [FromQuery] int? year, [FromQuery] string? tier,
            [FromQuery] DateOnly? examDate, [FromQuery] string? shift, [FromQuery] string? paperLabel,
            [FromQuery] PaperLanguage? language, [FromQuery] string? search,
            [FromQuery] string sort = "newest", [FromQuery] int page = 1, [FromQuery] int pageSize = 12)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var query = _db.Papers.Include(p => p.Exam).Include(p => p.CreatedByAdmin)
                .Where(p => p.Status == PaperStatus.Published)
                .AsQueryable();

            // Filter precedence (spec section 37): a single explicit examId -- what every existing
            // caller already sends -- always wins over examIds (plural), the new "My Exams" default.
            if (examId.HasValue) query = query.Where(p => p.ExamId == examId.Value);
            else if (examIds is { Count: > 0 }) query = query.Where(p => examIds.Contains(p.ExamId));
            if (year.HasValue) query = query.Where(p => p.Year == year.Value);
            if (!string.IsNullOrWhiteSpace(tier)) query = query.Where(p => p.Tier == tier);
            if (examDate.HasValue) query = query.Where(p => p.ExamDate == examDate.Value);
            if (!string.IsNullOrWhiteSpace(shift)) query = query.Where(p => p.Shift == shift);
            if (!string.IsNullOrWhiteSpace(paperLabel)) query = query.Where(p => p.PaperLabel == paperLabel);
            if (language.HasValue) query = query.Where(p => p.Language == language.Value);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(p =>
                    p.Exam!.Name.Contains(s) ||
                    (p.Tier != null && p.Tier.Contains(s)) ||
                    (p.PaperLabel != null && p.PaperLabel.Contains(s)));
            }

            query = sort switch
            {
                "oldest" => query.OrderBy(p => p.PublishedAt),
                "questions" => query.OrderByDescending(p => p.RequiredQuestionCount),
                _ => query.OrderByDescending(p => p.PublishedAt), // "newest" (default)
            };

            var totalCount = await query.CountAsync();
            var pageRows = await query.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(p => new { Paper = p, QuestionCount = p.Questions.Count + p.QuestionBankLinks.Count })
                .ToListAsync();

            // One batch lookup for this page's worth of papers instead of a query per row -- same
            // null-guard as DiscussionsController.TopDiscussions for a request with no logged-in user.
            var userId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : (Guid?)null;
            var pagePaperIds = pageRows.Select(x => x.Paper.Id).ToList();
            var bookmarkedIds = userId == null
                ? new HashSet<Guid>()
                : (await _db.Bookmarks.Where(b => b.UserId == userId && b.PaperId != null && pagePaperIds.Contains(b.PaperId!.Value))
                    .Select(b => b.PaperId!.Value).ToListAsync()).ToHashSet();

            // Distinct students per paper, not raw attempt rows (a student re-attempting the same
            // paper shouldn't inflate "Attempted by X students" -- see AttemptCount's comment in
            // PaperDTOs.cs). Grouping on (PaperId, UserId) first collapses each student down to one
            // row before the outer GroupBy counts students per paper.
            var attemptCounts = await _db.StudentTestResults
                .Where(r => r.PaperId != null && pagePaperIds.Contains(r.PaperId.Value)
                    && (r.Status == TestAttemptStatus.Submitted || r.Status == TestAttemptStatus.AutoSubmitted))
                .Select(r => new { r.PaperId, r.UserId })
                .Distinct()
                .GroupBy(r => r.PaperId!.Value)
                .Select(g => new { PaperId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.PaperId, g => g.Count);

            return Ok(new PagedResult<PaperResponseDto>
            {
                Items = pageRows.Select(x => PapersController.MapToDto(
                    x.Paper, x.QuestionCount, bookmarkedIds.Contains(x.Paper.Id), attemptCounts.GetValueOrDefault(x.Paper.Id))).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // GET /api/papers/filter-options?examId=&year= -- which Tier/Date/Shift/Paper-label/Language
        // values actually exist right now, so the browse page only shows a dropdown for a filter
        // that's genuinely meaningful for the current exam instead of a fixed set of fields forced
        // onto every exam (spec section 4). Passing no examId returns options across everything
        // Published, for the page's very first load before an exam is chosen.
        [HttpGet("filter-options")]
        public async Task<ActionResult<PaperFilterOptionsDto>> GetFilterOptions(
            [FromQuery] Guid? examId, [FromQuery] List<Guid>? examIds, [FromQuery] int? year)
        {
            var query = _db.Papers.Where(p => p.Status == PaperStatus.Published).AsQueryable();
            if (examId.HasValue) query = query.Where(p => p.ExamId == examId.Value);
            else if (examIds is { Count: > 0 }) query = query.Where(p => examIds.Contains(p.ExamId));
            if (year.HasValue) query = query.Where(p => p.Year == year.Value);

            var rows = await query
                .Select(p => new { p.Tier, p.ExamDate, p.Shift, p.PaperLabel, p.Language })
                .ToListAsync();

            return Ok(new PaperFilterOptionsDto
            {
                Tiers = rows.Where(r => r.Tier != null).Select(r => r.Tier!).Distinct().OrderBy(t => t).ToList(),
                ExamDates = rows.Where(r => r.ExamDate.HasValue).Select(r => r.ExamDate!.Value).Distinct().OrderByDescending(d => d).ToList(),
                Shifts = rows.Where(r => r.Shift != null).Select(r => r.Shift!).Distinct().OrderBy(s => s).ToList(),
                PaperLabels = rows.Where(r => r.PaperLabel != null).Select(r => r.PaperLabel!).Distinct().OrderBy(l => l).ToList(),
                Languages = rows.Select(r => r.Language.ToString()).Distinct().OrderBy(l => l).ToList()
            });
        }

        // GET /api/papers/my-attempts?status=InProgress|Completed -- backs the "Continue Attempting"
        // / "Completed Papers" tabs. Deliberately separate from TestAttemptsController.Mine (which
        // covers Mock/Practice/Paper generically) because these cards need paper-specific metadata
        // (Tier/Shift/question-answered progress) a Mock or Practice attempt doesn't have.
        [HttpGet("my-attempts")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<PagedResult<MyPaperAttemptDto>>> MyAttempts(
            [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var userId = User.GetUserId();
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.StudentTestResults
                .Include(r => r.Paper).ThenInclude(p => p!.Exam)
                .Where(r => r.UserId == userId && r.TestKind == TestKind.PreviousYearPaper)
                .AsQueryable();

            if (string.Equals(status, "InProgress", StringComparison.OrdinalIgnoreCase))
                query = query.Where(r => r.Status == TestAttemptStatus.InProgress);
            else if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
                query = query.Where(r => r.Status != TestAttemptStatus.InProgress);

            query = query.OrderByDescending(r => r.StartedAt);
            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var attemptIds = items.Select(r => r.Id).ToList();
            var answeredCounts = await _db.StudentAnswers
                .Where(a => attemptIds.Contains(a.StudentTestResultId) && a.SelectedOption != null)
                .GroupBy(a => a.StudentTestResultId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);
            // Falls back to StartedAt for an attempt with no saved answers yet -- there's no
            // dedicated "last saved" column on StudentTestResult itself, only per-answer AnsweredAt.
            var lastActivity = await _db.StudentAnswers
                .Where(a => attemptIds.Contains(a.StudentTestResultId) && a.AnsweredAt != null)
                .GroupBy(a => a.StudentTestResultId)
                .Select(g => new { g.Key, LastAt = g.Max(a => a.AnsweredAt) })
                .ToDictionaryAsync(x => x.Key, x => x.LastAt);

            var mapped = items.Select(r => new MyPaperAttemptDto
            {
                AttemptId = r.Id,
                PaperId = r.PaperId ?? Guid.Empty,
                ExamName = r.Paper?.Exam?.Name ?? "",
                Year = r.Paper?.Year ?? 0,
                Tier = r.Paper?.Tier,
                Shift = r.Paper?.Shift,
                ExamDate = r.Paper?.ExamDate,
                TotalQuestions = r.Paper?.RequiredQuestionCount ?? 0,
                AnsweredCount = answeredCounts.GetValueOrDefault(r.Id, 0),
                DurationMinutes = r.Paper?.DurationMinutes,
                Status = r.Status.ToString(),
                Score = r.Status == TestAttemptStatus.InProgress ? null : r.Score,
                LastActivityAt = lastActivity.GetValueOrDefault(r.Id) ?? r.StartedAt,
                CanResume = r.Status == TestAttemptStatus.InProgress
            }).ToList();

            return Ok(new PagedResult<MyPaperAttemptDto> { Items = mapped, TotalCount = totalCount, Page = page, PageSize = pageSize });
        }

        // GET /api/papers/years?examId=
        [HttpGet("years")]
        public async Task<ActionResult<List<int>>> GetYears([FromQuery] Guid examId)
        {
            var years = await _db.Papers
                .Where(p => p.ExamId == examId && p.Status == PaperStatus.Published)
                .Select(p => p.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToListAsync();

            return Ok(years);
        }

        // GET /api/papers/languages?examId=&year=
        [HttpGet("languages")]
        public async Task<ActionResult<List<string>>> GetLanguages([FromQuery] Guid examId, [FromQuery] int year)
        {
            var languages = await _db.Papers
                .Where(p => p.ExamId == examId && p.Year == year && p.Status == PaperStatus.Published)
                .Select(p => p.Language)
                .Distinct()
                .ToListAsync();

            return Ok(languages.Select(l => l.ToString()).OrderBy(l => l).ToList());
        }

        // GET /api/papers/sets?examId=&year=&language=
        // Usually returns exactly one paper -- more than one means this Exam/Year/Language has multiple
        // question Sets (Set A / Set B / ...), so the student needs to pick which one. Each result
        // now also carries IsConfiguredForPractice/IsComplete (see PaperResponseDto) so the frontend
        // paper card can show "Start Paper" only when it's genuinely ready to attempt.
        [HttpGet("sets")]
        public async Task<ActionResult<List<PaperResponseDto>>> GetSets(
            [FromQuery] Guid examId, [FromQuery] int year, [FromQuery] PaperLanguage language)
        {
            var papers = await _db.Papers.Include(p => p.Exam).Include(p => p.CreatedByAdmin)
                .Where(p => p.ExamId == examId && p.Year == year && p.Status == PaperStatus.Published && p.Language == language)
                .Select(p => new { Paper = p, QuestionCount = p.Questions.Count + p.QuestionBankLinks.Count })
                .ToListAsync();

            return Ok(papers.Select(x => PapersController.MapToDto(x.Paper, x.QuestionCount)));
        }

        // GET /api/papers/{id} -- single paper's info screen (spec section 31, "Paper Information"
        // before Start Paper: question count / duration / marks / negative marking). Published only.
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<PaperResponseDto>> GetPaper(Guid id)
        {
            var paper = await _db.Papers.Include(p => p.Exam).Include(p => p.CreatedByAdmin)
                .FirstOrDefaultAsync(p => p.Id == id && p.Status == PaperStatus.Published);
            if (paper == null) return NotFound(new { message = "Paper not found." });

            var questionCount = await PapersController.GetCombinedQuestionCountAsync(_db, id);
            var isBookmarked = User.Identity?.IsAuthenticated == true
                && await _db.Bookmarks.AnyAsync(b => b.PaperId == id && b.UserId == User.GetUserId());
            var attemptCount = await _db.StudentTestResults
                .Where(r => r.PaperId == id && (r.Status == TestAttemptStatus.Submitted || r.Status == TestAttemptStatus.AutoSubmitted))
                .Select(r => r.UserId).Distinct().CountAsync();
            return Ok(PapersController.MapToDto(paper, questionCount, isBookmarked, attemptCount));
        }

        // POST /api/papers/{id}/start -- begin (or resume) a Previous Year Paper Practice attempt.
        // Mirrors MockTestsController.Start exactly: one in-progress attempt per (student, paper) --
        // a second call while one is already running just hands back the same attempt (resume).
        [HttpPost("{id:guid}/start")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<TestAttemptStartResponseDto>> Start(Guid id)
        {
            var paper = await _db.Papers.Include(p => p.Exam)
                .FirstOrDefaultAsync(p => p.Id == id && p.Status == PaperStatus.Published);
            if (paper == null) return NotFound(new { message = "Paper not found." });

            if (!paper.DurationMinutes.HasValue || !paper.RequiredQuestionCount.HasValue)
                return BadRequest(new { message = "This paper isn't set up for Previous Year Paper Practice yet." });

            var userId = User.GetUserId();

            var existing = await _db.StudentTestResults
                .Include(r => r.Answers)
                .Include(r => r.Paper).ThenInclude(p => p!.Exam)
                .FirstOrDefaultAsync(r => r.UserId == userId && r.PaperId == id && r.Status == TestAttemptStatus.InProgress);
            if (existing != null) return Ok(TestAttemptsController.ToStartResponse(existing));

            // "Exact paper integrity" (spec section 8) -- refuse to generate an incomplete paper as
            // if it were the real thing rather than silently starting a 93/100-question attempt.
            var totalQuestions = await PapersController.GetCombinedQuestionCountAsync(_db, id);
            if (totalQuestions < paper.RequiredQuestionCount.Value)
            {
                return BadRequest(new
                {
                    message = "Paper is currently unavailable because all questions have not been added yet."
                });
            }

            var pyqRefs = await _db.Questions
                .Where(q => q.PaperId == id)
                .Select(q => new { q.Id, QuestionNumber = q.QuestionNumber ?? 0, q.Subject })
                .ToListAsync();
            var qbRefs = await _db.PaperQuestionBankLinks
                .Include(l => l.QuestionBankQuestion).ThenInclude(q => q!.Subject)
                .Where(l => l.PaperId == id)
                .Select(l => new { Id = l.QuestionBankQuestionId, l.QuestionNumber, l.IsNumberExact, Subject = l.QuestionBankQuestion!.Subject!.Name })
                .ToListAsync();

            // A legacy PYQ Question's Subject is a free-text, optional field (unlike a Question Bank
            // question's, which always has a real QuestionBankSubject) -- null/blank is valid data for
            // an older row. Coalescing it here, rather than leaving it null, is what actually matters:
            // the subject-grouping fallback below builds a Dictionary keyed by Subject, and a null key
            // throws ArgumentNullException the moment ONE question on the paper has no subject set --
            // which would fail this entire Start() call (500, no attempt created) for every student,
            // for a paper that is otherwise completely fine to attempt.
            var merged = pyqRefs
                .Select(q => (QuestionId: (Guid?)q.Id, QuestionBankId: (Guid?)null, q.QuestionNumber,
                    Subject: string.IsNullOrWhiteSpace(q.Subject) ? "Unspecified" : q.Subject, IsNumberExact: true))
                .Concat(qbRefs.Select(q => (QuestionId: (Guid?)null, QuestionBankId: (Guid?)q.Id, q.QuestionNumber, q.Subject, q.IsNumberExact)))
                .ToList();

            // Spec section 9, "Question Order" -- normally never shuffled, sorted by the ORIGINAL
            // paper's question number. But a bulk-added Question Bank question (see
            // PapersController.MapQuestionsBulk) never had a real position to begin with -- its
            // QuestionNumber is just "next free slot", not a fact about the actual paper. So if even
            // ONE question on this paper has an approximate number, fall back to a subject-grouped
            // order (each subject's questions kept together, in the order that subject first appears
            // in the Q.No-sorted list) instead of presenting a merged number sequence as if it were the
            // real exam layout it isn't.
            var hasApproximateNumbering = merged.Any(r => !r.IsNumberExact);

            IEnumerable<(Guid? QuestionId, Guid? QuestionBankId, int QuestionNumber, string Subject, bool IsNumberExact)> orderedRefs;
            if (hasApproximateNumbering)
            {
                var subjectRank = merged.OrderBy(r => r.QuestionNumber).Select(r => r.Subject).Distinct()
                    .Select((s, i) => new { Subject = s, Rank = i })
                    .ToDictionary(x => x.Subject, x => x.Rank);

                orderedRefs = merged
                    .OrderBy(r => subjectRank[r.Subject])
                    .ThenBy(r => r.QuestionNumber);
            }
            else
            {
                orderedRefs = merged.OrderBy(r => r.QuestionNumber);
            }

            // re-sequence 1..N for StudentAnswer.QuestionOrder -- original numbering was only for
            // merge-ordering (or subject-grouping above).
            var refs = orderedRefs
                .Select((r, i) => new QuestionRef(r.QuestionId, r.QuestionBankId, i + 1))
                .ToList();

            var answers = await _attemptService.BuildSnapshotAnswersAsync(_db, refs);

            var attempt = new StudentTestResult
            {
                TestKind = TestKind.PreviousYearPaper,
                PaperId = paper.Id,
                UserId = userId,
                NegativeMarkingRatio = paper.NegativeMarkingRatio ?? 0m,
                Status = TestAttemptStatus.InProgress,
                StartedAt = DateTime.UtcNow
            };
            foreach (var a in answers) attempt.Answers.Add(a);

            _db.StudentTestResults.Add(attempt);
            await _db.SaveChangesAsync();

            attempt.Paper = paper;
            return Ok(TestAttemptsController.ToStartResponse(attempt));
        }
    }
}

