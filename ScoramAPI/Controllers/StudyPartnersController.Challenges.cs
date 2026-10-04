using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // STUDY PARTNER CHALLENGES. Progress during a challenge is NOT tracked by a second engine: attempts
    // are the student's ordinary Practice / Mock / PYP / Quiz attempts, and a challenge is simply scored
    // from the StudentTestResults submitted inside [StartDate, EndDate] (see StudyPartnerRules.Evaluate).
    // So nothing to "play" -- keep studying as usual and the challenge counts it.
    public partial class StudyPartnersController
    {
        private const int MaxOpenChallengesPerPair = 3;

        private static readonly StudyChallengeStatus[] OpenStatuses =
            { StudyChallengeStatus.Pending, StudyChallengeStatus.Accepted, StudyChallengeStatus.InProgress };

        private async Task CancelOpenChallengesAsync(Guid a, Guid b)
        {
            var open = await _db.StudyPartnerChallenges
                .Where(c => OpenStatuses.Contains(c.Status) &&
                            ((c.CreatorUserId == a && c.PartnerUserId == b) || (c.CreatorUserId == b && c.PartnerUserId == a)))
                .ToListAsync();
            foreach (var c in open) { c.Status = StudyChallengeStatus.Cancelled; c.CompletedAt = DateTime.UtcNow; }
        }

        private static string Describe(StudyPartnerChallenge c) => c.Type switch
        {
            StudyChallengeType.Practice => $"answer {c.QuestionCount} questions in {c.DurationDays} days",
            StudyChallengeType.Accuracy => $"best accuracy over {c.QuestionCount}+ questions in {c.DurationDays} days",
            StudyChallengeType.Speed => $"fastest attempt of {c.QuestionCount}+ questions within {c.DurationDays} days",
            _ => $"study {c.TargetDays} days within {c.DurationDays} days"
        };

        /// <summary>Create a challenge for one of your Study Partners. The exam (optional) must be in BOTH
        /// students' My Exams. At most 3 open challenges per pair.</summary>
        [HttpPost("challenges")]
        [EnableRateLimiting("content-post")]
        [ProducesResponseType(typeof(SpChallengeDto), StatusCodes.Status201Created)]
        public async Task<ActionResult<SpChallengeDto>> CreateChallenge(SpCreateChallengeDto dto)
        {
            var me = User.GetUserId();
            if (dto.PartnerUserId == me) return BadRequest(new { message = "Pick one of your Study Partners." });
            if (!Enum.TryParse<StudyChallengeType>(dto.Type, true, out var type))
                return BadRequest(new { message = "Challenge type must be Practice, Accuracy, Speed or Streak." });

            if (await IsBlockedEitherWayAsync(me, dto.PartnerUserId))
                return StatusCode(403, new { message = "You can't challenge this student." });
            if (await GetPartnershipAsync(me, dto.PartnerUserId) == null)
                return StatusCode(403, new { message = "You can only challenge your Study Partners." });

            var error = StudyPartnerRules.ValidateChallenge(type, dto.QuestionCount, dto.TargetDays, dto.DurationDays);
            if (error != null) return BadRequest(new { message = error });

            if (dto.ExamId.HasValue && !(await CommonExamsAsync(me, dto.PartnerUserId)).Any(e => e.Id == dto.ExamId.Value))
                return BadRequest(new { message = "Pick an exam you both have in My Exams." });

            var open = await _db.StudyPartnerChallenges.CountAsync(c => OpenStatuses.Contains(c.Status) &&
                ((c.CreatorUserId == me && c.PartnerUserId == dto.PartnerUserId) || (c.CreatorUserId == dto.PartnerUserId && c.PartnerUserId == me)));
            if (open >= MaxOpenChallengesPerPair)
                return Conflict(new { message = "You already have several open challenges with this Study Partner." });

            var challenge = new StudyPartnerChallenge
            {
                CreatorUserId = me,
                PartnerUserId = dto.PartnerUserId,
                Type = type,
                ExamId = dto.ExamId,
                QuestionCount = type == StudyChallengeType.Streak ? null : dto.QuestionCount,
                TargetDays = type == StudyChallengeType.Streak ? dto.TargetDays : null,
                TimeLimitMinutes = dto.TimeLimitMinutes is > 0 and <= 600 ? dto.TimeLimitMinutes : null,
                DurationDays = dto.DurationDays
            };
            _db.StudyPartnerChallenges.Add(challenge);
            await _db.SaveChangesAsync();

            var myName = await _db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync();
            await TryNotifyAsync(dto.PartnerUserId, NotificationType.StudyPartnerChallenge, "New Study Partner challenge",
                $"{myName} suggested a challenge: {Describe(challenge)}.", "challenges", $"StudyPartnerChallenge:{challenge.Id}");

            return StatusCode(201, (await ToChallengeDtosAsync(new[] { challenge }, me))[0]);
        }

        /// <summary>Your challenges, newest first. scope=active (default: pending/accepted/in progress) or
        /// history. Challenges that are due (ended, or pending too long) are settled here, so the numbers
        /// you see are always final once a challenge's end date has passed.</summary>
        [HttpGet("challenges")]
        [ProducesResponseType(typeof(List<SpChallengeDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<SpChallengeDto>>> ListChallenges([FromQuery] string scope = "active", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var me = User.GetUserId();
            var size = Math.Clamp(pageSize, 1, PageSizeMax);
            var history = scope.Equals("history", StringComparison.OrdinalIgnoreCase);

            // Settle anything due that involves me, before listing.
            var due = await _db.StudyPartnerChallenges
                .Where(c => (c.CreatorUserId == me || c.PartnerUserId == me) && OpenStatuses.Contains(c.Status))
                .ToListAsync();
            var changed = false;
            foreach (var c in due) changed |= await SettleIfDueAsync(c);
            if (changed) await _db.SaveChangesAsync();

            var query = _db.StudyPartnerChallenges.AsNoTracking().Where(c => c.CreatorUserId == me || c.PartnerUserId == me);
            query = history ? query.Where(c => !OpenStatuses.Contains(c.Status)) : query.Where(c => OpenStatuses.Contains(c.Status));

            var rows = await query.OrderByDescending(c => c.CreatedAt).Skip((Math.Max(1, page) - 1) * size).Take(size).ToListAsync();
            return Ok(await ToChallengeDtosAsync(rows, me));
        }

        /// <summary>Accept a challenge sent to you; it starts now.</summary>
        [HttpPost("challenges/{id:guid}/accept")]
        [ProducesResponseType(typeof(SpChallengeDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<SpChallengeDto>> AcceptChallenge(Guid id)
        {
            var me = User.GetUserId();
            var c = await _db.StudyPartnerChallenges.FirstOrDefaultAsync(x => x.Id == id && x.PartnerUserId == me);
            if (c == null) return NotFound(new { message = "That challenge isn't available." });
            if (await SettleIfDueAsync(c)) await _db.SaveChangesAsync();

            if (c.Status == StudyChallengeStatus.Expired) return Conflict(new { message = "This challenge has expired." });
            if (c.Status != StudyChallengeStatus.Pending) return Conflict(new { message = "This challenge can no longer be accepted." });
            if (await IsBlockedEitherWayAsync(me, c.CreatorUserId) || await GetPartnershipAsync(me, c.CreatorUserId) == null)
                return Conflict(new { message = "You're no longer Study Partners with this student." });

            var now = DateTime.UtcNow;
            c.Status = StudyChallengeStatus.InProgress;
            c.StartDate = now;
            c.EndDate = now.AddDays(c.DurationDays);
            c.RespondedAt = now;
            await _db.SaveChangesAsync();

            var myName = await _db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync();
            await TryNotifyAsync(c.CreatorUserId, NotificationType.StudyPartnerChallengeAccepted, "Challenge accepted",
                $"{myName} accepted your challenge. It runs until {c.EndDate:d MMM}.", "challenges", $"StudyPartnerChallengeAccepted:{c.Id}");

            return Ok((await ToChallengeDtosAsync(new[] { c }, me))[0]);
        }

        /// <summary>Decline a challenge sent to you.</summary>
        [HttpPost("challenges/{id:guid}/reject")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> RejectChallenge(Guid id)
        {
            var me = User.GetUserId();
            var c = await _db.StudyPartnerChallenges.FirstOrDefaultAsync(x => x.Id == id && x.PartnerUserId == me && x.Status == StudyChallengeStatus.Pending);
            if (c == null) return NotFound(new { message = "That challenge isn't available." });
            c.Status = StudyChallengeStatus.Rejected;
            c.RespondedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Withdraw a challenge you created, while it's still waiting for an answer.</summary>
        [HttpDelete("challenges/{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> CancelChallenge(Guid id)
        {
            var me = User.GetUserId();
            var c = await _db.StudyPartnerChallenges.FirstOrDefaultAsync(x => x.Id == id && x.CreatorUserId == me && x.Status == StudyChallengeStatus.Pending);
            if (c == null) return NotFound(new { message = "That challenge isn't available." });
            c.Status = StudyChallengeStatus.Cancelled;
            c.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Settle a challenge and see the result. 409 while it's still running (nothing is decided
        /// before its end date, so a result can't be claimed early). Safe to call repeatedly.</summary>
        [HttpPost("challenges/{id:guid}/complete")]
        [ProducesResponseType(typeof(SpChallengeDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<SpChallengeDto>> CompleteChallenge(Guid id)
        {
            var me = User.GetUserId();
            var c = await _db.StudyPartnerChallenges.FirstOrDefaultAsync(x => x.Id == id && (x.CreatorUserId == me || x.PartnerUserId == me));
            if (c == null) return NotFound(new { message = "That challenge isn't available." });

            if (await SettleIfDueAsync(c)) await _db.SaveChangesAsync();
            if (c.Status is StudyChallengeStatus.InProgress or StudyChallengeStatus.Accepted)
                return Conflict(new { message = $"This challenge is still running -- it ends on {c.EndDate:d MMM}." });

            return Ok((await ToChallengeDtosAsync(new[] { c }, me))[0]);
        }

        // ---------------------------------------------------------------- settling

        /// <summary>Moves a challenge forward if its time has come: Pending too long -> Expired; Accepted
        /// whose start passed -> InProgress; InProgress past its end -> Completed with the scored result.
        /// Mutates the (tracked) entity; the caller saves. Returns true if anything changed.</summary>
        private async Task<bool> SettleIfDueAsync(StudyPartnerChallenge c)
        {
            var now = DateTime.UtcNow;

            if (c.Status == StudyChallengeStatus.Pending)
            {
                if (now - c.CreatedAt <= StudyPartnerRules.PendingChallengeLifetime) return false;
                c.Status = StudyChallengeStatus.Expired;
                c.CompletedAt = now;
                return true;
            }

            if (c.Status == StudyChallengeStatus.Accepted && c.StartDate <= now)
            {
                c.Status = StudyChallengeStatus.InProgress;
                return true;
            }

            if (c.Status != StudyChallengeStatus.InProgress || c.EndDate is null || c.EndDate > now || c.StartDate is null)
                return false;

            var examScope = c.ExamId.HasValue ? new[] { c.ExamId.Value } : null;
            var attempts = await _progress.GetAttemptsAsync(new[] { c.CreatorUserId, c.PartnerUserId }, examScope, c.StartDate, c.EndDate);

            var outcome = StudyPartnerRules.Evaluate(c.Type, c.QuestionCount, c.TargetDays,
                c.CreatorUserId, BuildSide(attempts, c.CreatorUserId, c.QuestionCount),
                c.PartnerUserId, BuildSide(attempts, c.PartnerUserId, c.QuestionCount));

            c.CreatorResult = outcome.CreatorResult;
            c.PartnerResult = outcome.PartnerResult;
            c.WinnerUserId = outcome.WinnerUserId;
            c.ResultSummary = outcome.Summary;
            c.Status = StudyChallengeStatus.Completed;
            c.CompletedAt = now;

            // Both students are told once, with the result (DedupKey guards against double notification).
            foreach (var userId in new[] { c.CreatorUserId, c.PartnerUserId })
            {
                var body = outcome.WinnerUserId == null ? outcome.Summary
                    : outcome.WinnerUserId == userId ? "You won this challenge. " + outcome.Summary
                    : "This challenge is over. " + outcome.Summary;
                await TryNotifyAsync(userId, NotificationType.StudyPartnerChallengeCompleted, "Study Partner challenge finished",
                    body, "challenges", $"StudyPartnerChallengeCompleted:{c.Id}");
            }
            return true;
        }

        private static StudyPartnerRules.Side BuildSide(List<AttemptRow> all, Guid userId, int? minQuestions)
        {
            var mine = all.Where(a => a.UserId == userId).ToList();
            var answered = mine.Sum(a => a.Answered);
            var correct = mine.Sum(a => a.Correct);

            // Speed: fastest SINGLE attempt that answered at least the target number of questions.
            int? fastest = minQuestions.HasValue
                ? mine.Where(a => a.Answered >= minQuestions && a.TimeTakenSeconds > 0).Select(a => (int?)a.TimeTakenSeconds).Min()
                : null;

            // Study days use the IST day boundary, the same one streaks use.
            var days = mine.Select(a => (a.AttemptedAt + TimeSpan.FromHours(5.5)).Date).Distinct().Count();
            return new StudyPartnerRules.Side(answered, correct, fastest, days);
        }

        private async Task<List<SpChallengeDto>> ToChallengeDtosAsync(IReadOnlyCollection<StudyPartnerChallenge> challenges, Guid me)
        {
            if (challenges.Count == 0) return new List<SpChallengeDto>();
            var userIds = challenges.SelectMany(c => new[] { c.CreatorUserId, c.PartnerUserId }).Distinct().ToList();
            var persons = await PersonsAsync(userIds, await PartnerIdsAsync(me));
            var examIds = challenges.Where(c => c.ExamId.HasValue).Select(c => c.ExamId!.Value).Distinct().ToList();
            var exams = await _db.Exams.AsNoTracking().Where(e => examIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name);

            SpPersonDto P(Guid id) => persons.TryGetValue(id, out var p) ? p : new SpPersonDto { UserId = id, FullName = "Student" };

            return challenges.Select(c => new SpChallengeDto
            {
                Id = c.Id,
                Type = c.Type.ToString(),
                Status = c.Status.ToString(),
                Creator = P(c.CreatorUserId),
                Partner = P(c.PartnerUserId),
                IAmCreator = c.CreatorUserId == me,
                Exam = c.ExamId.HasValue && exams.TryGetValue(c.ExamId.Value, out var n) ? new SpExamRefDto { Id = c.ExamId.Value, Name = n } : null,
                QuestionCount = c.QuestionCount,
                TargetDays = c.TargetDays,
                TimeLimitMinutes = c.TimeLimitMinutes,
                DurationDays = c.DurationDays,
                StartDate = c.StartDate,
                EndDate = c.EndDate,
                CreatorResult = c.CreatorResult,
                PartnerResult = c.PartnerResult,
                WinnerUserId = c.WinnerUserId,
                ResultSummary = c.ResultSummary,
                CreatedAt = c.CreatedAt
            }).ToList();
        }
    }
}
