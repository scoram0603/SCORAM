
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Public, read-only side of the Question Bank.
    [ApiController]
    [Route("api/question-bank")]
    public class QuestionBankController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IGamificationService _gamification;
        private readonly ILogger<QuestionBankController> _logger;
        private readonly IMyExamScopeService _myExams;
        private readonly IExamTaxonomyService _examTaxonomy;
        private readonly IMemoryCache _cache;

        public QuestionBankController(
            ScoramDbContext db,
            IGamificationService gamification,
            ILogger<QuestionBankController> logger,
            IMyExamScopeService myExams,
            IExamTaxonomyService examTaxonomy,
            IMemoryCache cache)
        {
            _cache = cache;
            _db = db;
            _gamification = gamification;
            _logger = logger;
            _myExams = myExams;
            _examTaxonomy = examTaxonomy;
        }

        // Only active, non-test questions that are not exclusively mirrored
        // from unpublished papers are visible to students.
        private IQueryable<QuestionBankQuestion> VisibleQuestions() =>
            _db.QuestionBankQuestions.Where(x =>
                x.IsActive &&
                !x.IsTestOnly &&
                (
                    !_db.Questions.Any(src =>
                        src.MirroredToQuestionBankQuestionId == x.Id)
                    ||
                    _db.Questions.Any(src =>
                        src.MirroredToQuestionBankQuestionId == x.Id &&
                        src.Paper!.Status == PaperStatus.Published)
                ));

        // PERFORMANCE (API-01): TTL-only cache for the 4 catalog endpoints below (Subjects/Topics/
        // Exams/Years). No version stamp, no SaveChanges hook -- an admin change (publish/unpublish,
        // import, subject merge, ...) can take up to CatalogCacheTtl to reach students. Deliberate,
        // reviewed tradeoff for a smaller, lower-blast-radius change (see the performance round's
        // plan/report). GetTodaysChallenge's own, separate cache (to-midnight TTL) is untouched.
        private static readonly TimeSpan CatalogCacheTtl = TimeSpan.FromMinutes(3);

        // One lock per cache key so concurrent first-requests for the same key (e.g. the several
        // client calls that fire together on screen open, or many students opening at once) compute
        // once, not N times, instead of all missing the cache and hitting the DB simultaneously.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _catalogCacheLocks = new();

        // PERFORMANCE (API-06): the wait for the per-key gate observes the caller's token, so a
        // cancelled request queued behind a slow first computation leaves immediately instead of
        // parking until the gate frees. WaitAsync stays OUTSIDE the try on purpose: if it throws
        // (cancelled), the gate was never acquired, so the finally below must not Release() it.
        // A factory that throws/cancels caches nothing (Set is only reached on success), and the
        // finally releases the gate, so a cancelled request can neither poison the cache nor leave
        // the gate held. Each factory is a per-call closure over its own caller's token.
        private async Task<T> GetOrAddCatalogCacheAsync<T>(
            string cacheKey, Func<Task<T>> factory, CancellationToken ct = default)
        {
            if (_cache.TryGetValue(cacheKey, out T? cached))
                return cached!;

            var gate = _catalogCacheLocks.GetOrAdd(
                cacheKey, _ => new SemaphoreSlim(1, 1));

            await gate.WaitAsync(ct);
            try
            {
                // Re-check: another request may have just populated it while we waited.
                if (_cache.TryGetValue(cacheKey, out cached))
                    return cached!;

                var value = await factory();

                _cache.Set(cacheKey, value, CatalogCacheTtl);

                return value;
            }
            finally
            {
                gate.Release();
            }
        }

        // Same scope-key derivation GetTodaysChallenge already uses below -- kept as its own copy
        // rather than a refactor of that endpoint, so this round touches nothing about Today's
        // Challenge. Unscoped (anonymous/admin) callers and a scoped student with an empty My-Exams
        // selection intentionally get DIFFERENT keys (empty string vs. the hash of an empty exam-id
        // list), since they must never share a cached entry.
        private static string ScopeCacheKeySuffix(MyExamScope scope) =>
            scope.IsScoped
                ? "-" + Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(
                            string.Join(",", scope.ExamIds.OrderBy(id => id)))))
                    [..16]
                : string.Empty;

        // GET /api/question-bank/search
        [HttpGet("search")]
        public async Task<ActionResult<PagedResult<QuestionBankQuestionResponseDto>>> Search(
            [FromQuery] QuestionBankSearchQuery query,
            CancellationToken ct = default)
        {
            var totalTimer = Stopwatch.StartNew();
            var stageTimer = Stopwatch.StartNew();

            var stageTimes = new Dictionary<string, long>();

            void RecordStage(string name)
            {
                stageTimes[name] = stageTimes.GetValueOrDefault(name)
                    + stageTimer.ElapsedMilliseconds;

                stageTimer.Restart();
            }

            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);

            var hasKeyword = !string.IsNullOrWhiteSpace(query.Search);
            var term = hasKeyword ? query.Search!.Trim() : null;

            // Whitespace-insensitive LIKE pattern.
            var likePattern = hasKeyword
                ? "%" + string.Join("%",
                    term!.Split((char[]?)null,
                        StringSplitOptions.RemoveEmptyEntries)) + "%"
                : null;

            // Full-text search is used only for single-word queries.
            var useFullText = hasKeyword &&
                              !term!.Any(char.IsWhiteSpace);

            var searchMode = useFullText
                ? "FullText"
                : hasKeyword ? "LIKE" : "NoKeyword";

            // MY EXAMS SCOPE
            var scope = await _myExams.GetScopeAsync(User, ct);

            var scopedExamIds = scope.IsScoped
                ? scope.Narrow(query.ExamIds).ToList()
                : null;

            RecordStage("scope");

            // Builds the complete filtered query.
            IQueryable<QuestionBankQuestion> BuildQuery(
                bool useFullTextForKeyword)
            {
                var filtered = VisibleQuestions();

                if (hasKeyword)
                {
                    filtered = useFullTextForKeyword
                        ? filtered.Where(x =>
                            EF.Functions.Contains(x.QuestionText, term!))
                        : filtered.Where(x =>
                            EF.Functions.Like(x.QuestionText, likePattern!));
                }

                if (query.SubjectIds is { Count: > 0 } subjectIds)
                {
                    filtered = filtered.Where(x =>
                        subjectIds.Contains(x.SubjectId));
                }

                if (query.TopicIds is { Count: > 0 } topicIds)
                {
                    filtered = filtered.Where(x =>
                        topicIds.Contains(x.TopicId));
                }

                if (query.ExamSubjectIds is { Count: > 0 } examSubjectIds)
                {
                    filtered = _examTaxonomy.FilterByExamSubjects(
                        filtered, examSubjectIds);
                }

                if (query.ExamTopicIds is { Count: > 0 } examTopicIds)
                {
                    filtered = _examTaxonomy.FilterByExamTopics(
                        filtered, examTopicIds);
                }

                if (scopedExamIds != null)
                {
                    filtered = filtered.Where(x =>
                        x.ExamMappings.Any(m =>
                            scopedExamIds.Contains(m.ExamId)));
                }
                else if (query.ExamIds is { Count: > 0 } examIds)
                {
                    filtered = filtered.Where(x =>
                        x.ExamMappings.Any(m =>
                            examIds.Contains(m.ExamId)));
                }

                if (query.Years is { Count: > 0 } years)
                {
                    filtered = filtered.Where(x =>
                        x.ExamMappings.Any(m =>
                            years.Contains(m.Year)));
                }

                if (query.Languages is { Count: > 0 } rawLanguages)
                {
                    var languageFilters = rawLanguages
                        .Select(l =>
                            Enum.TryParse<PaperLanguage>(
                                l, true, out var parsed)
                                ? (PaperLanguage?)parsed
                                : null)
                        .Where(l => l.HasValue)
                        .Select(l => l!.Value)
                        .ToList();

                    if (languageFilters.Count > 0)
                    {
                        filtered = filtered.Where(x =>
                            x.Language != null &&
                            languageFilters.Contains(x.Language.Value));
                    }
                }

                return filtered.OrderByDescending(x => x.CreatedAt);
            }

            // Executes COUNT and page-data queries separately so we can
            // measure their individual execution times.
            async Task<(int TotalCount, List<QuestionBankQuestion> Items)>
                ExecuteAsync(IQueryable<QuestionBankQuestion> source)
            {
                stageTimer.Restart();

                var count = await source.CountAsync(ct);

                RecordStage("count");

                stageTimer.Restart();

                // PERFORMANCE (API-03): AsNoTracking -- this is a read-only GET, nothing here is
                // saved back through these entities in the same request. AsSplitQuery -- ExamMappings
                // is a sibling collection include; without it EF cross-joins ExamMappings rows into
                // every row, duplicating the (wide) QuestionBankQuestion columns per mapping. Solutions
                // is no longer included at all -- SolutionCount is now a correlated count fetched in
                // the aggregates block below (see solutionCounts), instead of loading every Solution
                // row (including its full text/image columns) just to count the approved ones.
                var pageItems = await source
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Include(x => x.Subject)
                    .Include(x => x.Topic)
                    .Include(x => x.ExamMappings)
                        .ThenInclude(m => m.Exam)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(ct);

                RecordStage("page");

                return (count, pageItems);
            }

            int totalCount;
            List<QuestionBankQuestion> items;

            if (hasKeyword)
            {
                try
                {
                    (totalCount, items) = await ExecuteAsync(
                        BuildQuery(useFullTextForKeyword: useFullText));
                }
                // PERFORMANCE (API-05): narrowed from a blanket `catch (Exception ex)` -- only a
                // genuine full-text failure re-runs the whole query as LIKE. Cancellation/timeout
                // propagate instead of silently paying for a second, full LIKE scan; any other bug
                // (e.g. in this method itself) is no longer misread as "full-text is broken". No
                // change to search results themselves in any case this didn't already cover.
                catch (Exception ex) when (useFullText && IsFullTextUnavailable(ex))
                {
                    // Fallback to LIKE if SQL Full-Text search fails.
                    searchMode = "LIKE-Fallback";

                    _logger.LogWarning(
                        ex,
                        "SQL Full-Text search unavailable for question bank. " +
                        "Falling back to LIKE for query {Query}",
                        term);

                    (totalCount, items) = await ExecuteAsync(
                        BuildQuery(useFullTextForKeyword: false));
                }
            }
            else
            {
                (totalCount, items) = await ExecuteAsync(
                    BuildQuery(useFullTextForKeyword: false));
            }

            // Aggregate data for only the current page.
            var ids = items.Select(x => x.Id).ToList();

            var likeCounts = new Dictionary<Guid, int>();
            var dislikeCounts = new Dictionary<Guid, int>();
            var commentCounts = new Dictionary<Guid, int>();
            var solutionCounts = new Dictionary<Guid, int>();
            Dictionary<Guid, bool> myVotes = new();

            stageTimer.Restart();

            // Avoid unnecessary database calls when there are no results.
            if (ids.Count > 0)
            {
                // PERFORMANCE (API-02): likes and dislikes used to be two separate grouped queries
                // over the same QuestionVotes rows; one grouped query with conditional counts reads
                // that table once instead of twice. Same grouping, same two resulting dictionaries --
                // no change to what LikeCount/DislikeCount end up being.
                var voteCounts = await _db.QuestionVotes
                    .Where(v => ids.Contains(v.QuestionBankQuestionId!.Value))
                    .GroupBy(v => v.QuestionBankQuestionId!.Value)
                    .Select(g => new
                    {
                        Id = g.Key,
                        Likes = g.Count(v => v.IsLike),
                        Dislikes = g.Count(v => !v.IsLike)
                    })
                    .ToListAsync(ct);

                likeCounts = voteCounts.ToDictionary(v => v.Id, v => v.Likes);
                dislikeCounts = voteCounts.ToDictionary(v => v.Id, v => v.Dislikes);

                // PERFORMANCE (API-03): replaces loading every Solution row via Include (see
                // ExecuteAsync above) just to count the approved ones in memory.
                solutionCounts = await _db.QuestionSolutions
                    .Where(s =>
                        s.QuestionBankQuestionId != null &&
                        ids.Contains(s.QuestionBankQuestionId.Value) &&
                        s.IsApproved)
                    .GroupBy(s => s.QuestionBankQuestionId!.Value)
                    .Select(g => new
                    {
                        Id = g.Key,
                        Count = g.Count()
                    })
                    .ToDictionaryAsync(g => g.Id, g => g.Count, ct);

                commentCounts = await _db.QuestionComments
                    .Where(c =>
                        c.QuestionBankQuestionId != null &&
                        ids.Contains(c.QuestionBankQuestionId.Value))
                    .GroupBy(c => c.QuestionBankQuestionId!.Value)
                    .Select(g => new
                    {
                        Id = g.Key,
                        Count = g.Count()
                    })
                    .ToDictionaryAsync(g => g.Id, g => g.Count, ct);

                if (User.Identity?.IsAuthenticated == true)
                {
                    var userId = User.GetUserId();

                    myVotes = await _db.QuestionVotes
                        .Where(v =>
                            ids.Contains(v.QuestionBankQuestionId!.Value) &&
                            v.UserId == userId)
                        .ToDictionaryAsync(
                            v => v.QuestionBankQuestionId!.Value,
                            v => v.IsLike,
                            ct);
                }
            }

            RecordStage("aggregates");

            // DTO mapping
            stageTimer.Restart();

            var mapped = items.Select(MapToResponseDto).ToList();

            RecordStage("mapping");

            // EXAM-WISE NAMES
            var examForNames =
                (scopedExamIds ?? query.ExamIds) is { Count: 1 } single
                    ? single[0]
                    : (Guid?)null;

            if (examForNames.HasValue && items.Count > 0)
            {
                stageTimer.Restart();

                await ApplyExamNamesAsync(
                    examForNames.Value,
                    items,
                    mapped,
                    ct);

                RecordStage("examNames");
            }

            // Add aggregated values to response DTOs.
            foreach (var dto in mapped)
            {
                dto.LikeCount = likeCounts.GetValueOrDefault(dto.Id);
                dto.DislikeCount = dislikeCounts.GetValueOrDefault(dto.Id);
                dto.CommentCount = commentCounts.GetValueOrDefault(dto.Id);
                dto.SolutionCount = solutionCounts.GetValueOrDefault(dto.Id);

                dto.MyVote = myVotes.TryGetValue(dto.Id, out var vote)
                    ? vote
                    : (bool?)null;
            }

            // Final performance log: one summary per search request.
            totalTimer.Stop();

            _logger.LogInformation(
                "Question bank search performance | " +
                "Mode={SearchMode} | Page={Page} | PageSize={PageSize} | " +
                "Results={ResultCount} | TotalCount={TotalCount} | " +
                "TotalMs={TotalMs} | Stages={Stages}",
                searchMode,
                page,
                pageSize,
                items.Count,
                totalCount,
                totalTimer.ElapsedMilliseconds,
                string.Join(", ", stageTimes.Select(
                    x => $"{x.Key}={x.Value}ms")));

            return Ok(new PagedResult<QuestionBankQuestionResponseDto>
            {
                Items = mapped,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // PERFORMANCE (API-05): true only for a genuine SQL Server full-text failure -- catalog
        // missing/rebuilding, full-text not installed/enabled, or a CONTAINS syntax error from the
        // search term. These are the standard, Microsoft-documented error numbers for those
        // conditions; they were NOT empirically verified against a live database from this sandbox
        // (no DB access here) -- flagged in the performance round's report as something to confirm,
        // e.g. by temporarily disabling full-text on a non-prod copy and checking ex.Number logged
        // by the warning this guards. Never true for cancellation, which must propagate.
        private static bool IsFullTextUnavailable(Exception ex)
        {
            if (ex is OperationCanceledException)
                return false;

            var sql = FindSqlException(ex);

            // 7601 = table not full-text indexed; 7609/7630 = CONTAINS syntax error;
            // 7619 = full-text search disabled for this database; 7626 = catalog in an unusable
            // state; 30046 = Full-Text Search not installed; 30077 = catalog unavailable/rebuilding.
            return sql is not null &&
                sql.Number is 7601 or 7609 or 7619 or 7626 or 7630 or 30046 or 30077;
        }

        // EF query-execution failures are not wrapped the way SaveChanges failures are (see the
        // DbUpdateException/InnerException checks elsewhere in this project, e.g.
        // Services/SubjectManagementService.cs), but checking one level of InnerException too,
        // the same way those do, is a cheap safety net in case a provider ever wraps it.
        private static SqlException? FindSqlException(Exception ex) =>
            ex as SqlException ?? ex.InnerException as SqlException;

        // GET /api/question-bank/today
        [HttpGet("today")]
        public async Task<ActionResult<QuestionBankQuestionResponseDto>>
            GetTodaysChallenge(CancellationToken ct = default)
        {
            var scope = await _myExams.GetScopeAsync(User, ct);

            if (scope.IsEmpty)
                return NotFound(new
                {
                    message = "Choose your exams to get a daily challenge."
                });

            var istNow = DateTime.UtcNow.AddHours(5).AddMinutes(30);
            var istToday = DateOnly.FromDateTime(istNow);

            var scopeKey = scope.IsScoped
                ? "-" + Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(
                            string.Join(",", scope.ExamIds.OrderBy(id => id)))))
                    [..16]
                : string.Empty;

            var cacheKey =
                $"qb-todays-challenge-{istToday:yyyy-MM-dd}{scopeKey}";

            if (!_cache.TryGetValue(cacheKey, out Guid pickedId))
            {
                // PERFORMANCE (API-08): the first request of each IST day per scope used to be a stampede --
                // every concurrent miss ran the same count + skip query over the visibility predicate. Now one
                // request computes it per scope and the others wait for the cache (same gate pattern as
                // GetOrAddCatalogCacheAsync above: WaitAsync outside the try so a cancelled waiter never
                // Releases a gate it did not acquire; the finally releases it on every other path; nothing is
                // cached unless the pick succeeded, so "No questions yet." is still recomputed next time).
                // The gate key has NO date in it, so the (static) gate dictionary stays bounded by the number of
                // distinct scopes rather than growing by one entry per day; the cache key still carries the date.
                var gate = _catalogCacheLocks.GetOrAdd(
                    "qb-todays-challenge-gate" + scopeKey, _ => new SemaphoreSlim(1, 1));

                await gate.WaitAsync(ct);
                try
                {
                    // Re-check: another request may have just populated it while we waited.
                    if (!_cache.TryGetValue(cacheKey, out pickedId))
                    {
                        var candidates = VisibleQuestions();

                        if (scope.IsScoped)
                        {
                            var scopedExamIds = scope.ExamIds.ToList();

                            candidates = candidates.Where(x =>
                                x.ExamMappings.Any(m =>
                                    scopedExamIds.Contains(m.ExamId)));
                        }

                        var count = await candidates.CountAsync(ct);

                        if (count == 0)
                            return NotFound(new { message = "No questions yet." });

                        var index = istToday.DayNumber % count;

                        // Dependent on `count` (the index is DayNumber % count), so these two stay separate.
                        pickedId = await candidates
                            .OrderBy(x => x.Id)
                            .Skip(index)
                            .Select(x => x.Id)
                            .FirstAsync(ct);

                        var untilMidnight =
                            istToday.AddDays(1).ToDateTime(TimeOnly.MinValue) - istNow;

                        _cache.Set(
                            cacheKey,
                            pickedId,
                            untilMidnight + TimeSpan.FromMinutes(1));
                    }
                }
                finally
                {
                    gate.Release();
                }
            }

            // PERFORMANCE (API-03): AsNoTracking/AsSplitQuery as in Search above; Solutions is no
            // longer included -- SolutionCount below is now a correlated count, same pattern already
            // used here for LikeCount/DislikeCount/CommentCount.
            var question = await VisibleQuestions()
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Subject)
                .Include(x => x.Topic)
                .Include(x => x.ExamMappings)
                    .ThenInclude(m => m.Exam)
                .FirstOrDefaultAsync(x => x.Id == pickedId, ct);

            if (question == null)
            {
                _cache.Remove(cacheKey);

                return NotFound(new
                {
                    message = "Today's challenge is being refreshed. Please try again."
                });
            }

            var dto = MapToResponseDto(question);

            // PERFORMANCE (API-08): the four counts + my-vote were five sequential round trips; now one.
            // Today deliberately never reports IsBookmarked (it never did), so the bookmark is not asked for.
            var engagement = await LoadEngagementAsync(
                pickedId,
                User.Identity?.IsAuthenticated == true ? User.GetUserId() : null,
                includeBookmark: false,
                ct);

            dto.LikeCount = engagement.Likes;
            dto.DislikeCount = engagement.Dislikes;
            dto.CommentCount = engagement.Comments;
            dto.SolutionCount = engagement.Solutions;

            if (User.Identity?.IsAuthenticated == true)
                dto.MyVote = engagement.MyVote;

            return Ok(dto);
        }

        // GET /api/question-bank/{id}
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<QuestionBankQuestionResponseDto>>
            GetById(Guid id, [FromQuery] Guid? examId = null, CancellationToken ct = default)
        {
            // PERFORMANCE (API-03): AsNoTracking/AsSplitQuery as in Search above; Solutions is no
            // longer included -- SolutionCount below is now a correlated count, same pattern already
            // used here for LikeCount/DislikeCount/CommentCount.
            var question = await VisibleQuestions()
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Subject)
                .Include(x => x.Topic)
                .Include(x => x.ExamMappings)
                    .ThenInclude(m => m.Exam)
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (question == null)
                return NotFound(new { message = "Question not found." });

            var dto = MapToResponseDto(question);

            if (examId.HasValue)
            {
                await ApplyExamNamesAsync(
                    examId.Value,
                    new[] { question },
                    new[] { dto },
                    ct);
            }

            // PERFORMANCE (API-08): four counts + my-vote + bookmark were six sequential round trips; now one.
            var isAuthenticated = User.Identity?.IsAuthenticated == true;

            var engagement = await LoadEngagementAsync(
                id,
                isAuthenticated ? User.GetUserId() : null,
                includeBookmark: true,
                ct);

            dto.LikeCount = engagement.Likes;
            dto.DislikeCount = engagement.Dislikes;
            dto.CommentCount = engagement.Comments;
            dto.SolutionCount = engagement.Solutions;

            if (isAuthenticated)
            {
                dto.MyVote = engagement.MyVote;
                dto.IsBookmarked = engagement.IsBookmarked;
            }

            return Ok(dto);
        }

        // POST /api/question-bank/{id}/solve
        [HttpPost("{id:guid}/solve")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> MarkSolved(Guid id)
        {
            var questionExists = await VisibleQuestions()
                .AnyAsync(q => q.Id == id);

            if (!questionExists)
                return NotFound(new { message = "Question not found." });

            var userId = User.GetUserId();

            var already = await _db.UserQuestionSolves.AnyAsync(
                s => s.UserId == userId &&
                     s.QuestionBankQuestionId == id);

            if (already)
                return Ok(new { alreadySolved = true });

            _db.UserQuestionSolves.Add(new UserQuestionSolve
            {
                UserId = userId,
                QuestionBankQuestionId = id
            });

            await _db.SaveChangesAsync();

            await _gamification.RecordActivityAsync(
                userId,
                GamificationService.XpFor(
                    GamificationService.Reasons.QuestionSolved),
                GamificationService.Reasons.QuestionSolved);

            return Ok(new { alreadySolved = false });
        }

        // GET /api/question-bank/subjects
        [HttpGet("subjects")]
        public async Task<ActionResult<List<QuestionBankSubjectDto>>>
            GetSubjects([FromQuery] Guid? examId = null, CancellationToken ct = default)
        {
            var scope = await _myExams.GetScopeAsync(User, ct);

            var scoped = scope.IsScoped;
            var allowedExamIds = scope.ExamIds.ToList();

            if (examId.HasValue &&
                (!scoped || allowedExamIds.Contains(examId.Value)) &&
                await _examTaxonomy.IsInitializedAsync(examId.Value, ct))
            {
                // PERFORMANCE (API-01): cached -- GetStudentSubjectsAsync does multiple queries
                // (taxonomy + per-subject counts) on every call today.
                var examSubjectDtos = await GetOrAddCatalogCacheAsync(
                    $"qb:subjects:exam:{examId.Value}{ScopeCacheKeySuffix(scope)}",
                    async () =>
                    {
                        var examSubjects =
                            await _examTaxonomy.GetStudentSubjectsAsync(
                                examId.Value,
                                VisibleQuestions(),
                                ct);

                        return examSubjects
                            .Where(s => !scoped || s.QuestionCount > 0)
                            .Select(s => new QuestionBankSubjectDto
                            {
                                Id = s.Id,
                                Name = s.Name,
                                IsActive = true,
                                QuestionCount = s.QuestionCount,
                                IsExamSpecific = true
                            })
                            .ToList();
                    },
                    ct);

                return Ok(examSubjectDtos);
            }

            if (examId.HasValue &&
                scoped &&
                !allowedExamIds.Contains(examId.Value))
            {
                return Ok(new List<QuestionBankSubjectDto>());
            }

            // PERFORMANCE (API-01): cached -- this recomputed the per-subject question count over
            // the whole Question Bank on every call; this is also the catalog endpoint's heaviest
            // caller of VisibleQuestions' visibility logic (API-04), so caching it is the fix there.
            var subjects = await GetOrAddCatalogCacheAsync(
                $"qb:subjects{ScopeCacheKeySuffix(scope)}",
                async () =>
                {
                    var result = await _db.QuestionBankSubjects
                        .Where(s => s.IsActive)
                        .OrderBy(s => s.Name)
                        .Select(s => new QuestionBankSubjectDto
                        {
                            Id = s.Id,
                            Name = s.Name,
                            IsActive = s.IsActive,

                            QuestionCount = s.Questions.Count(q =>
                                q.IsActive &&
                                !q.IsTestOnly &&
                                (
                                    !_db.Questions.Any(src =>
                                        src.MirroredToQuestionBankQuestionId == q.Id)
                                    ||
                                    _db.Questions.Any(src =>
                                        src.MirroredToQuestionBankQuestionId == q.Id &&
                                        src.Paper!.Status == PaperStatus.Published)
                                ) &&
                                (!scoped ||
                                 q.ExamMappings.Any(m =>
                                     allowedExamIds.Contains(m.ExamId)))
                            )
                        })
                        .ToListAsync(ct);

                    if (scoped)
                        result = result
                            .Where(s => s.QuestionCount > 0)
                            .ToList();

                    return result;
                },
                ct);

            return Ok(subjects);
        }

        // GET /api/question-bank/topics
        [HttpGet("topics")]
        public async Task<ActionResult<List<QuestionBankTopicDto>>>
            GetTopics(
                [FromQuery] Guid? subjectId,
                [FromQuery] Guid? examId = null,
                CancellationToken ct = default)
        {
            var scope = await _myExams.GetScopeAsync(User, ct);

            var scoped = scope.IsScoped;
            var allowedExamIds = scope.ExamIds.ToList();

            if (examId.HasValue &&
                (!scoped || allowedExamIds.Contains(examId.Value)) &&
                await _examTaxonomy.IsInitializedAsync(examId.Value, ct))
            {
                // PERFORMANCE (API-01): cached -- GetStudentTopicsAsync does multiple queries
                // (taxonomy + per-topic counts) on every call today.
                var examTopicDtos = await GetOrAddCatalogCacheAsync(
                    $"qb:topics:exam:{examId.Value}:{subjectId?.ToString() ?? "all"}{ScopeCacheKeySuffix(scope)}",
                    async () =>
                    {
                        var examTopics =
                            await _examTaxonomy.GetStudentTopicsAsync(
                                examId.Value,
                                subjectId,
                                VisibleQuestions(),
                                ct);

                        return examTopics
                            .Where(t => !scoped || t.QuestionCount > 0)
                            .Select(t => new QuestionBankTopicDto
                            {
                                Id = t.Id,
                                SubjectId = t.SubjectId,
                                SubjectName = t.SubjectName,
                                Name = t.Name,
                                IsActive = true,
                                QuestionCount = t.QuestionCount,
                                IsExamSpecific = true
                            })
                            .ToList();
                    },
                    ct);

                return Ok(examTopicDtos);
            }

            if (examId.HasValue &&
                scoped &&
                !allowedExamIds.Contains(examId.Value))
            {
                return Ok(new List<QuestionBankTopicDto>());
            }

            // PERFORMANCE (API-01): cached -- see GetSubjects above for why.
            var topics = await GetOrAddCatalogCacheAsync(
                $"qb:topics:{subjectId?.ToString() ?? "all"}{ScopeCacheKeySuffix(scope)}",
                async () =>
                {
                    var q = _db.QuestionBankTopics
                        .Where(t => t.IsActive)
                        .AsQueryable();

                    if (subjectId.HasValue)
                        q = q.Where(t => t.SubjectId == subjectId.Value);

                    var result = await q
                        .Include(t => t.Subject)
                        .OrderBy(t => t.Name)
                        .Select(t => new QuestionBankTopicDto
                        {
                            Id = t.Id,
                            SubjectId = t.SubjectId,
                            SubjectName = t.Subject!.Name,
                            Name = t.Name,
                            IsActive = t.IsActive,

                            QuestionCount = t.Questions.Count(q =>
                                q.IsActive &&
                                !q.IsTestOnly &&
                                (
                                    !_db.Questions.Any(src =>
                                        src.MirroredToQuestionBankQuestionId == q.Id)
                                    ||
                                    _db.Questions.Any(src =>
                                        src.MirroredToQuestionBankQuestionId == q.Id &&
                                        src.Paper!.Status == PaperStatus.Published)
                                ) &&
                                (!scoped ||
                                 q.ExamMappings.Any(m =>
                                     allowedExamIds.Contains(m.ExamId)))
                            )
                        })
                        .ToListAsync(ct);

                    if (scoped)
                        result = result
                            .Where(t => t.QuestionCount > 0)
                            .ToList();

                    return result;
                },
                ct);

            return Ok(topics);
        }

        // GET /api/question-bank/exams
        [HttpGet("exams")]
        public async Task<ActionResult<List<object>>> GetExams(CancellationToken ct = default)
        {
            var scope = await _myExams.GetScopeAsync(User, ct);

            var scoped = scope.IsScoped;
            var allowedExamIds = scope.ExamIds.ToList();

            // PERFORMANCE (API-01): cached -- see GetSubjects above for why.
            var exams = await GetOrAddCatalogCacheAsync(
                $"qb:exams{ScopeCacheKeySuffix(scope)}",
                async () => await _db.QuestionBankExamMappings
                    .Where(m =>
                        (!scoped || allowedExamIds.Contains(m.ExamId)) &&
                        VisibleQuestions().Any(v =>
                            v.Id == m.QuestionBankQuestionId))
                    .Select(m => m.Exam)
                    .Where(e => e != null)
                    .Distinct()
                    .OrderBy(e => e!.Name)
                    .Select(e => new
                    {
                        id = e!.Id,
                        name = e.Name,
                        logoUrl = e.LogoUrl
                    })
                    .ToListAsync(ct),
                ct);

            return Ok(exams);
        }

        // GET /api/question-bank/years
        [HttpGet("years")]
        public async Task<ActionResult<List<int>>> GetYears(CancellationToken ct = default)
        {
            var scope = await _myExams.GetScopeAsync(User, ct);

            var scoped = scope.IsScoped;
            var allowedExamIds = scope.ExamIds.ToList();

            // PERFORMANCE (API-01): cached -- see GetSubjects above for why.
            var years = await GetOrAddCatalogCacheAsync(
                $"qb:years{ScopeCacheKeySuffix(scope)}",
                async () => await _db.QuestionBankExamMappings
                    .Where(m =>
                        (!scoped || allowedExamIds.Contains(m.ExamId)) &&
                        VisibleQuestions().Any(v =>
                            v.Id == m.QuestionBankQuestionId))
                    .Select(m => m.Year)
                    .Distinct()
                    .OrderByDescending(y => y)
                    .ToListAsync(ct),
                ct);

            return Ok(years);
        }

        // PERFORMANCE (API-08): the per-question engagement numbers behind GetById and Today, as ONE command
        // (correlated scalar subqueries over the Votes / Comments / Solutions navigations -- the same pattern
        // GetSubjects/GetTopics already use) instead of 4-6 sequential round trips. The values are exactly what the
        // separate queries returned: likes / dislikes from QuestionVotes, all comments, APPROVED solutions only,
        // the caller's own vote (null = none) and whether the caller bookmarked it. `userId` null (anonymous)
        // is passed as Guid.Empty, which matches no vote or bookmark. A question that vanished between the
        // entity load and this read yields zeros. The entity query with Includes stays separate on purpose
        // (AsSplitQuery cannot be folded into a projection).
        private sealed record Engagement(int Likes, int Dislikes, int Comments, int Solutions, bool? MyVote, bool IsBookmarked);

        private async Task<Engagement> LoadEngagementAsync(
            Guid questionId, Guid? userId, bool includeBookmark, CancellationToken ct)
        {
            var uid = userId ?? Guid.Empty;

            var row = await _db.QuestionBankQuestions
                .AsNoTracking()
                .Where(q => q.Id == questionId)
                .Select(q => new
                {
                    Likes = q.Votes.Count(v => v.IsLike),
                    Dislikes = q.Votes.Count(v => !v.IsLike),
                    Comments = q.Comments.Count(),
                    Solutions = q.Solutions.Count(s => s.IsApproved),
                    MyVote = q.Votes
                        .Where(v => v.UserId == uid)
                        .Select(v => (bool?)v.IsLike)
                        .FirstOrDefault(),
                    IsBookmarked = includeBookmark &&
                        _db.Bookmarks.Any(b => b.QuestionBankQuestionId == questionId && b.UserId == uid)
                })
                .FirstOrDefaultAsync(ct);

            return row == null
                ? new Engagement(0, 0, 0, 0, null, false)
                : new Engagement(row.Likes, row.Dislikes, row.Comments, row.Solutions, row.MyVote, row.IsBookmarked);
        }

        private async Task ApplyExamNamesAsync(
            Guid examId,
            IEnumerable<QuestionBankQuestion> questions,
            IEnumerable<QuestionBankQuestionResponseDto> dtos,
            CancellationToken ct = default)
        {
            var names = await _examTaxonomy.ResolveNamesAsync(
                examId,
                questions.Select(x =>
                    (x.Id, x.SubjectId, x.TopicId)).ToList(),
                ct);

            if (names.Count == 0)
                return;

            foreach (var dto in dtos)
            {
                if (!names.TryGetValue(dto.Id, out var n))
                    continue;

                dto.Subject = n.Subject ?? dto.Subject;
                dto.Topic = n.Topic ?? dto.Topic;
            }
        }

        public static QuestionBankQuestionResponseDto MapToResponseDto(
            Models.QuestionBankQuestion x) =>
            new QuestionBankQuestionResponseDto
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

                ContentBlocks =
                    ContentBlocksJsonHelper.Parse(x.ContentBlocksJson),

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
                    })
                    .ToList(),

                SolutionCount =
                    x.Solutions?.Count(s => s.IsApproved) ?? 0,

                CreatedAt = x.CreatedAt
            };
    }
}
