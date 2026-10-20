using System.Text.Json;
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
    // QUESTION BANK BROWSING PROGRESS -- see Models/QuestionBrowseProgressModels.cs for why this is not a
    // test attempt. Every endpoint works ONLY on the caller's own rows: the user id comes from the JWT
    // (never from the request), and every query is filtered by it, so there is no id a student could
    // guess to read or change somebody else's progress.
    //
    //   GET  /api/question-bank/progress               active session + answers, 204 if none yet
    //   PUT  /api/question-bank/progress               upsert position and/or a batch of answers (idempotent, last-write-wins)
    //   POST /api/question-bank/progress/new-session   "Start fresh": archives the active session (nothing deleted)
    [ApiController]
    [Route("api/question-bank/progress")]
    [Authorize(Roles = "Student")]
    public class QuestionBrowseProgressController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly ILogger<QuestionBrowseProgressController> _logger;

        public QuestionBrowseProgressController(ScoramDbContext db, ILogger<QuestionBrowseProgressController> logger)
        {
            _db = db;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var userId = User.GetUserId();
            var session = await _db.QuestionBrowseSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Scope == QuestionBrowseScopes.QuestionBank && s.IsActive, ct);
            if (session == null) return NoContent();

            var answers = await _db.QuestionBrowseAnswers.AsNoTracking()
                .Where(a => a.SessionId == session.Id)
                .OrderByDescending(a => a.UpdatedAt)
                .Take(QuestionBrowseProgressRules.MaxAnswersReturned)
                .Select(a => new QuestionBrowseAnswerDto
                {
                    QuestionId = a.QuestionBankQuestionId,
                    SelectedOption = a.SelectedOption,
                    UpdatedAt = a.UpdatedAt,
                })
                .ToListAsync(ct);

            return Ok(new QuestionBrowseProgressDto
            {
                SessionId = session.Id,
                Filters = ParseFilters(session.FiltersJson),
                LastQuestionId = session.LastQuestionId,
                LastPosition = session.LastPosition,
                ViewMode = session.ViewMode,
                StartedAt = session.StartedAt,
                UpdatedAt = session.UpdatedAt,
                AnsweredCount = answers.Count,
                Answers = answers,
            });
        }

        [HttpPut]
        public async Task<IActionResult> Sync([FromBody] QuestionBrowseSyncRequestDto request, CancellationToken ct)
        {
            var userId = User.GetUserId();
            var now = DateTime.UtcNow;

            if (request.Answers is { Count: > QuestionBrowseProgressRules.MaxAnswersPerRequest })
                return BadRequest(new { message = $"At most {QuestionBrowseProgressRules.MaxAnswersPerRequest} answers can be saved per request." });

            string? filtersJson = null;
            if (request.Position?.Filters is { } filters && filters.ValueKind != JsonValueKind.Null && filters.ValueKind != JsonValueKind.Undefined)
            {
                if (filters.ValueKind != JsonValueKind.Object)
                    return BadRequest(new { message = "Filters must be a JSON object." });
                filtersJson = filters.GetRawText();
                if (filtersJson.Length > QuestionBrowseProgressRules.MaxFiltersJsonLength)
                    return BadRequest(new { message = "Filters are too large." });
            }

            var session = await GetOrCreateActiveSessionAsync(userId, ct);
            if (request.SessionId is { } expected && expected != session.Id)
                return Conflict(new { message = "A newer browsing session is active.", currentSessionId = session.Id });

            // ---- answers ----
            var applied = 0;
            var ignored = 0;
            var batchSize = request.Answers?.Count ?? 0;
            if (batchSize > 0)
            {
                var latest = QuestionBrowseProgressRules.LatestPerQuestion(request.Answers!, now);
                ignored += batchSize - latest.Count; // invalid options / empty ids / superseded duplicates in the batch

                var ids = latest.Keys.ToList();
                var knownIds = await _db.QuestionBankQuestions.AsNoTracking()
                    .Where(q => ids.Contains(q.Id)).Select(q => q.Id).ToListAsync(ct);
                var known = knownIds.ToHashSet();
                var existing = await _db.QuestionBrowseAnswers
                    .Where(a => a.SessionId == session.Id && ids.Contains(a.QuestionBankQuestionId))
                    .ToDictionaryAsync(a => a.QuestionBankQuestionId, ct);

                foreach (var (questionId, (option, at)) in latest)
                {
                    if (!known.Contains(questionId)) { ignored++; continue; }
                    if (existing.TryGetValue(questionId, out var row))
                    {
                        if (!QuestionBrowseProgressRules.ShouldApply(row.ClientUpdatedAt, at)) { ignored++; continue; }
                        row.SelectedOption = option;
                        row.ClientUpdatedAt = at;
                        row.UpdatedAt = now;
                    }
                    else
                    {
                        _db.QuestionBrowseAnswers.Add(new QuestionBrowseAnswer
                        {
                            SessionId = session.Id,
                            QuestionBankQuestionId = questionId,
                            SelectedOption = option,
                            ClientUpdatedAt = at,
                            UpdatedAt = now,
                        });
                    }
                    applied++;
                }
            }

            // ---- position ----
            if (request.Position is { } pos)
            {
                var at = QuestionBrowseProgressRules.ClampClientTime(pos.ClientUpdatedAt, now);
                if (session.PositionClientUpdatedAt is null || QuestionBrowseProgressRules.ShouldApply(session.PositionClientUpdatedAt.Value, at))
                {
                    if (filtersJson != null) session.FiltersJson = filtersJson;
                    else if (pos.Filters is { ValueKind: JsonValueKind.Null }) session.FiltersJson = null;

                    if (pos.LastQuestionId is { } q && q != Guid.Empty)
                    {
                        var exists = await _db.QuestionBankQuestions.AsNoTracking().AnyAsync(x => x.Id == q, ct);
                        session.LastQuestionId = exists ? q : null;
                    }
                    if (pos.LastPosition is { } p) session.LastPosition = Math.Clamp(p, 0, 1_000_000);
                    if (pos.ViewMode is { } vm)
                        session.ViewMode = vm.Equals("slide", StringComparison.OrdinalIgnoreCase) ? "slide"
                            : vm.Equals("list", StringComparison.OrdinalIgnoreCase) ? "list" : session.ViewMode;
                    session.PositionClientUpdatedAt = at;
                }
            }

            session.UpdatedAt = now;

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // The unique (session, question) index fired: two requests inserted the same answer at once.
                // The client keeps the write queued and replays it; replaying is idempotent.
                _logger.LogInformation(ex, "Question browse progress write raced for user {UserId}; asking client to retry.", userId);
                return Conflict(new { message = "Progress was updated concurrently. Please retry.", currentSessionId = session.Id });
            }

            return Ok(new QuestionBrowseSyncResultDto
            {
                SessionId = session.Id,
                UpdatedAt = session.UpdatedAt,
                AppliedAnswers = applied,
                IgnoredAnswers = ignored,
            });
        }

        [HttpPost("new-session")]
        public async Task<IActionResult> NewSession(CancellationToken ct)
        {
            var userId = User.GetUserId();
            var now = DateTime.UtcNow;

            var active = await _db.QuestionBrowseSessions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Scope == QuestionBrowseScopes.QuestionBank && s.IsActive, ct);

            if (active != null)
            {
                // Repeated taps on "Start fresh": an untouched session is already fresh -- reuse it.
                var untouched = active.LastQuestionId == null && active.FiltersJson == null
                    && !await _db.QuestionBrowseAnswers.AnyAsync(a => a.SessionId == active.Id, ct);
                if (untouched)
                    return Ok(new QuestionBrowseNewSessionResultDto { SessionId = active.Id, StartedAt = active.StartedAt });

                // Archive, never delete: the old answers stay in the database.
                active.IsActive = false;
                active.ArchivedAt = now;
                await _db.SaveChangesAsync(ct);
            }

            var fresh = new QuestionBrowseSession { UserId = userId, StartedAt = now, UpdatedAt = now };
            _db.QuestionBrowseSessions.Add(fresh);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent request already opened the new active session.
                _db.Entry(fresh).State = EntityState.Detached;
                var current = await _db.QuestionBrowseSessions.AsNoTracking()
                    .FirstAsync(s => s.UserId == userId && s.Scope == QuestionBrowseScopes.QuestionBank && s.IsActive, ct);
                return Ok(new QuestionBrowseNewSessionResultDto { SessionId = current.Id, StartedAt = current.StartedAt });
            }

            return Ok(new QuestionBrowseNewSessionResultDto { SessionId = fresh.Id, StartedAt = fresh.StartedAt });
        }

        private async Task<QuestionBrowseSession> GetOrCreateActiveSessionAsync(Guid userId, CancellationToken ct)
        {
            var session = await _db.QuestionBrowseSessions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Scope == QuestionBrowseScopes.QuestionBank && s.IsActive, ct);
            if (session != null) return session;

            session = new QuestionBrowseSession { UserId = userId };
            _db.QuestionBrowseSessions.Add(session);
            try
            {
                await _db.SaveChangesAsync(ct);
                return session;
            }
            catch (DbUpdateException)
            {
                // Lost the race against a parallel request creating the same active session.
                _db.Entry(session).State = EntityState.Detached;
                return await _db.QuestionBrowseSessions
                    .FirstAsync(s => s.UserId == userId && s.Scope == QuestionBrowseScopes.QuestionBank && s.IsActive, ct);
            }
        }

        private static JsonElement? ParseFilters(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
