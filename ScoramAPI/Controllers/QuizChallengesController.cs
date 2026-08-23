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
    // Phase 3 of the "Quizzes" feature -- Challenge a Friend (see Models/QuizChallengeModels.cs).
    // Every endpoint here needs a signed-in student; there's no anonymous-browsing angle the way
    // Quiz/Daily Quiz browsing has, a challenge is inherently between two specific people.
    [ApiController]
    [Route("api/quiz-challenges")]
    [Authorize(Roles = "Student")]
    public class QuizChallengesController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly ITestAttemptService _attemptService;
        private readonly INotificationService _notifications;

        public QuizChallengesController(ScoramDbContext db, ITestAttemptService attemptService, INotificationService notifications)
        {
            _db = db;
            _attemptService = attemptService;
            _notifications = notifications;
        }

        // POST /api/quiz-challenges -- "Challenge a friend" (or several, or a whole Group Chat room)
        // from a completed Quiz result. See QuizChallengeCreateDto for exactly how targets combine.
        [HttpPost]
        public async Task<ActionResult<QuizChallengeBatchResultDto>> Create(QuizChallengeCreateDto dto)
        {
            var userId = User.GetUserId();

            var sourceAttempt = await _db.StudentTestResults
                .FirstOrDefaultAsync(r => r.Id == dto.AttemptId && r.UserId == userId);
            if (sourceAttempt == null) return NotFound(new { message = "Attempt not found." });
            if (sourceAttempt.TestKind != TestKind.Quiz)
                return BadRequest(new { message = "Only a Quiz result can be turned into a challenge." });
            if (sourceAttempt.Status == TestAttemptStatus.InProgress)
                return BadRequest(new { message = "Finish the quiz before challenging anyone to it." });

            // Resolve the full candidate set: individually-picked friends + every current, non-banned
            // member of the chosen Group Chat room, if any (spec: "multiple friends and groups").
            var candidateIds = new HashSet<Guid>(dto.ChallengedUserIds ?? new List<Guid>());
            if (dto.ChallengedGroupId.HasValue)
            {
                var groupMemberIds = await _db.ChatRoomMemberships
                    .Where(m => m.ChatRoomId == dto.ChallengedGroupId.Value && !m.IsBanned)
                    .Select(m => m.UserId)
                    .ToListAsync();
                foreach (var id in groupMemberIds) candidateIds.Add(id);
            }
            if (candidateIds.Count == 0)
                return BadRequest(new { message = "Pick at least one friend or a group to challenge." });

            candidateIds.Remove(userId); // never challenge yourself, even if picked/a group member

            var activeCandidateIds = await _db.Users
                .Where(u => candidateIds.Contains(u.Id) && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync();

            // Don't re-challenge someone to the SAME attempt twice (e.g. they're both in a group
            // that got challenged and were individually picked too, or the sender clicks twice) --
            // silently skip rather than error, same "drop, don't fail the whole batch" spirit as
            // the self-exclusion above.
            var alreadyChallenged = await _db.QuizChallenges
                .Where(c => c.SourceAttemptId == sourceAttempt.Id && activeCandidateIds.Contains(c.ChallengedUserId))
                .Select(c => c.ChallengedUserId)
                .ToListAsync();

            var finalTargetIds = activeCandidateIds.Except(alreadyChallenged).ToList();
            var skippedCount = candidateIds.Count - finalTargetIds.Count;

            if (finalTargetIds.Count == 0)
                return BadRequest(new { message = "Everyone in that selection has already been challenged to this quiz (or couldn't be found)." });

            var batchId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var challenges = finalTargetIds.Select(targetId => new QuizChallenge
            {
                BatchId = batchId,
                ChallengerUserId = userId,
                ChallengedUserId = targetId,
                SourceAttemptId = sourceAttempt.Id,
                Status = QuizChallengeStatus.Pending,
                CreatedAt = now,
                ExpiresAt = now.AddDays(7)
            }).ToList();

            _db.QuizChallenges.AddRange(challenges);
            await _db.SaveChangesAsync();

            var summaries = new List<QuizChallengeSummaryDto>();
            foreach (var c in challenges)
                summaries.Add(await ToSummaryDtoAsync(c.Id, userId, c));

            // Bell + live SignalR push + Web Push, all handled by CreateAsync itself -- see
            // NotificationService.CreateAsync. Fixes the earlier gap where a sent challenge was only
            // ever visible by opening the Quizzes page and happening to notice it there. Reuses each
            // student's NotifyOnDirectMessages preference (CreateAsync's own muted-check) rather than
            // adding a whole separate "notify me about challenges" toggle for one narrow feature.
            var challengerName = summaries.Count > 0 ? summaries[0].ChallengerName : "Someone";
            foreach (var summary in summaries)
            {
                await _notifications.CreateAsync(
                    summary.ChallengedUserId,
                    NotificationType.QuizChallenge,
                    $"{challengerName} challenged you!",
                    $"{summary.QuizTitle} -- beat their score of {summary.ChallengerScore} to win.",
                    "/quizzes"
                );
            }

            return Ok(new QuizChallengeBatchResultDto
            {
                BatchId = batchId,
                Challenges = summaries,
                SkippedCount = skippedCount
            });
        }

        // GET /api/quiz-challenges/batch/{batchId} -- every challenge sent together in one action
        // (spec: "challenge multiple friends and groups"), e.g. for a "you challenged 5 friends, 3
        // finished so far" summary view. Challenger only -- a challenged student sees their own
        // single row via GET /mine instead, not the whole batch's other targets' progress.
        [HttpGet("batch/{batchId:guid}")]
        public async Task<ActionResult<List<QuizChallengeSummaryDto>>> GetBatch(Guid batchId)
        {
            var userId = User.GetUserId();
            var challenges = await _db.QuizChallenges
                .Where(c => c.BatchId == batchId && c.ChallengerUserId == userId)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
            if (challenges.Count == 0) return NotFound();

            foreach (var c in challenges) ExpireIfNeeded(c);
            if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();

            var items = new List<QuizChallengeSummaryDto>();
            foreach (var c in challenges)
                items.Add(await ToSummaryDtoAsync(c.Id, userId, c));
            return Ok(items);
        }

        // GET /api/quiz-challenges/by-attempt/{attemptId} -- every challenge this specific attempt is
        // involved in, whichever side (it's the SourceAttempt someone challenged others to, or it's
        // a ChallengedAttempt someone made to accept a challenge). Backs the "vs [opponent]"
        // comparison card on a Quiz result page. Only returns challenges the caller is a participant
        // of -- an attempt with a batch of 10 challenges only shows the caller's own one(s), not
        // everyone else's.
        [HttpGet("by-attempt/{attemptId:guid}")]
        public async Task<ActionResult<List<QuizChallengeSummaryDto>>> GetByAttempt(Guid attemptId)
        {
            var userId = User.GetUserId();
            var challenges = await _db.QuizChallenges
                .Where(c => (c.SourceAttemptId == attemptId || c.ChallengedAttemptId == attemptId)
                    && (c.ChallengerUserId == userId || c.ChallengedUserId == userId))
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            foreach (var c in challenges) ExpireIfNeeded(c);
            if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();

            var items = new List<QuizChallengeSummaryDto>();
            foreach (var c in challenges)
                items.Add(await ToSummaryDtoAsync(c.Id, userId, c));
            return Ok(items);
        }

        // GET /api/quiz-challenges/mine?direction=received|sent&status=pending
        [HttpGet("mine")]
        public async Task<ActionResult<List<QuizChallengeSummaryDto>>> Mine(
            [FromQuery] string direction = "received", [FromQuery] string? status = null)
        {
            var userId = User.GetUserId();

            var query = direction == "sent"
                ? _db.QuizChallenges.Where(c => c.ChallengerUserId == userId)
                : _db.QuizChallenges.Where(c => c.ChallengedUserId == userId);

            var challenges = await query.OrderByDescending(c => c.CreatedAt).Take(50).ToListAsync();
            foreach (var c in challenges) ExpireIfNeeded(c);
            if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();

            var items = new List<QuizChallengeSummaryDto>();
            foreach (var c in challenges)
                items.Add(await ToSummaryDtoAsync(c.Id, userId, c));

            if (!string.IsNullOrWhiteSpace(status))
                items = items.Where(i => string.Equals(i.Status, status, StringComparison.OrdinalIgnoreCase)).ToList();

            return Ok(items);
        }

        // GET /api/quiz-challenges/{id} -- only the two participants can view it.
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<QuizChallengeSummaryDto>> GetById(Guid id)
        {
            var userId = User.GetUserId();
            var challenge = await _db.QuizChallenges.FirstOrDefaultAsync(c => c.Id == id);
            if (challenge == null) return NotFound();
            if (challenge.ChallengerUserId != userId && challenge.ChallengedUserId != userId) return Forbid();

            ExpireIfNeeded(challenge);
            if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();

            return Ok(await ToSummaryDtoAsync(challenge.Id, userId, challenge));
        }

        // POST /api/quiz-challenges/{id}/start -- challenged student only. Resumes if they've
        // already started, otherwise builds a fresh attempt from the SAME question set as
        // SourceAttempt (see QuizChallenge's own comment on how that's done).
        [HttpPost("{id:guid}/start")]
        public async Task<ActionResult<TestAttemptStartResponseDto>> Start(Guid id)
        {
            var userId = User.GetUserId();
            var challenge = await _db.QuizChallenges
                .Include(c => c.SourceAttempt).ThenInclude(a => a!.Answers)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (challenge == null) return NotFound();
            if (challenge.ChallengedUserId != userId) return Forbid();

            ExpireIfNeeded(challenge);
            if (challenge.Status == QuizChallengeStatus.Declined) return BadRequest(new { message = "This challenge was declined." });
            if (challenge.Status == QuizChallengeStatus.Expired) return BadRequest(new { message = "This challenge has expired." });

            if (challenge.ChallengedAttemptId.HasValue)
            {
                var existing = await _db.StudentTestResults
                    .Include(r => r.Answers).Include(r => r.Quiz)
                    .FirstOrDefaultAsync(r => r.Id == challenge.ChallengedAttemptId.Value);
                if (existing != null) return Ok(TestAttemptsController.ToStartResponse(existing));
            }

            var sourceAttempt = challenge.SourceAttempt!;
            var refs = sourceAttempt.Answers
                .OrderBy(a => a.QuestionOrder)
                .Select(a => new QuestionRef(a.QuestionId, a.QuestionBankQuestionId, a.QuestionOrder));
            var answers = await _attemptService.BuildSnapshotAnswersAsync(_db, refs);

            var attempt = new StudentTestResult
            {
                TestKind = TestKind.Quiz,
                QuizId = sourceAttempt.QuizId, // mirrors a Daily-Quiz-based challenge's title/duration
                UserId = userId,
                QuizDurationMinutes = sourceAttempt.QuizDurationMinutes,
                NegativeMarkingRatio = sourceAttempt.NegativeMarkingRatio,
                Status = TestAttemptStatus.InProgress,
                StartedAt = DateTime.UtcNow
            };
            foreach (var a in answers) attempt.Answers.Add(a);

            _db.StudentTestResults.Add(attempt);
            challenge.ChallengedAttemptId = attempt.Id;
            await _db.SaveChangesAsync();

            if (sourceAttempt.QuizId.HasValue)
                attempt.Quiz = await _db.Quizzes.FindAsync(sourceAttempt.QuizId.Value);

            return Ok(TestAttemptsController.ToStartResponse(attempt));
        }

        // POST /api/quiz-challenges/{id}/decline -- challenged student only, only while still Pending.
        [HttpPost("{id:guid}/decline")]
        public async Task<IActionResult> Decline(Guid id)
        {
            var userId = User.GetUserId();
            var challenge = await _db.QuizChallenges.FirstOrDefaultAsync(c => c.Id == id);
            if (challenge == null) return NotFound();
            if (challenge.ChallengedUserId != userId) return Forbid();
            if (challenge.ChallengedAttemptId.HasValue)
                return BadRequest(new { message = "You've already started this challenge." });

            challenge.Status = QuizChallengeStatus.Declined;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // Lazy expiry -- checked whenever a challenge is read, no background job needed. Only
        // touches challenges that are still Pending AND never started; once someone's actually
        // taken the quiz the clock stops mattering.
        private static void ExpireIfNeeded(QuizChallenge c)
        {
            if (c.Status == QuizChallengeStatus.Pending && !c.ChallengedAttemptId.HasValue && DateTime.UtcNow > c.ExpiresAt)
                c.Status = QuizChallengeStatus.Expired;
        }

        private async Task<QuizChallengeSummaryDto> ToSummaryDtoAsync(Guid challengeId, Guid viewerUserId, QuizChallenge? preloaded = null)
        {
            var c = preloaded ?? await _db.QuizChallenges.FirstAsync(x => x.Id == challengeId);

            var challenger = await _db.Users.FirstAsync(u => u.Id == c.ChallengerUserId);
            var challenged = await _db.Users.FirstAsync(u => u.Id == c.ChallengedUserId);
            var sourceAttempt = await _db.StudentTestResults.Include(r => r.Answers).Include(r => r.Quiz)
                .FirstAsync(r => r.Id == c.SourceAttemptId);

            StudentTestResult? challengedAttempt = null;
            if (c.ChallengedAttemptId.HasValue)
                challengedAttempt = await _db.StudentTestResults.FirstOrDefaultAsync(r => r.Id == c.ChallengedAttemptId.Value);

            var status = c.Status switch
            {
                QuizChallengeStatus.Declined => "Declined",
                QuizChallengeStatus.Expired => "Expired",
                _ => challengedAttempt == null ? "Pending"
                    : challengedAttempt.Status == TestAttemptStatus.InProgress ? "InProgress"
                    : "Completed"
            };

            string? winner = null;
            decimal? challengedScore = null;
            if (status == "Completed" && challengedAttempt != null)
            {
                challengedScore = challengedAttempt.Score;
                winner = sourceAttempt.Score > challengedAttempt.Score ? "Challenger"
                    : challengedAttempt.Score > sourceAttempt.Score ? "Challenged"
                    : "Tie";
            }

            return new QuizChallengeSummaryDto
            {
                Id = c.Id,
                BatchId = c.BatchId,
                ChallengerUserId = c.ChallengerUserId,
                ChallengerName = challenger.FullName,
                ChallengerPhotoUrl = challenger.PhotoUrl,
                ChallengedUserId = c.ChallengedUserId,
                ChallengedName = challenged.FullName,
                ChallengedPhotoUrl = challenged.PhotoUrl,
                QuizTitle = TestAttemptsController.TitleFor(sourceAttempt),
                QuestionCount = sourceAttempt.Answers.Count,
                DurationMinutes = sourceAttempt.Quiz?.DurationMinutes ?? sourceAttempt.QuizDurationMinutes ?? Math.Max(5, sourceAttempt.Answers.Count),
                NegativeMarkingRatio = sourceAttempt.NegativeMarkingRatio,
                ChallengerScore = sourceAttempt.Score,
                ChallengedScore = challengedScore,
                Status = status,
                Winner = winner,
                IAmChallenger = c.ChallengerUserId == viewerUserId,
                ChallengedAttemptId = c.ChallengedAttemptId,
                SourceAttemptId = c.SourceAttemptId,
                CreatedAt = c.CreatedAt,
                ExpiresAt = c.ExpiresAt
            };
        }
    }
}
