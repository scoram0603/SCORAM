using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    /// <summary>
    /// STUDY PARTNER -- connections, profile/progress/compare, leaderboard, privacy and blocking.
    /// (Challenges live in StudyPartnersController.Challenges.cs -- same controller, same route.)
    ///
    /// Security model, applied to every action:
    ///  * The caller is ALWAYS the JWT's user (User.GetUserId()); no body/query field names "me".
    ///  * Anything about ANOTHER student passes StudyPartnerRules.CanView against THEIR privacy
    ///    settings, server-side. Hiding it in React/Flutter is never relied on.
    ///  * A block in either direction makes the other student look like they don't exist (404) -- the
    ///    response never reveals who blocked whom.
    ///  * Ids in the URL are checked against ownership / the partner relationship before use.
    ///
    /// Reuses (no parallel copies): Users, UserExamPreferences/Exams (My Exams), StudentTestResults via
    /// StudentProgressService (same definitions as the Progress page), UserStreak/UserXP/UserBadge,
    /// NotificationService (push + bell), and the existing Direct Messages for "Message".
    /// </summary>
    [ApiController]
    [Route("api/study-partners")]
    [Authorize(Roles = "Student")]
    public partial class StudyPartnersController : ControllerBase
    {
        private const int PageSizeMax = 50;

        private readonly ScoramDbContext _db;
        private readonly IStudentProgressService _progress;
        private readonly INotificationService _notifications;
        private readonly IMyExamScopeService _myExams;

        public StudyPartnersController(ScoramDbContext db, IStudentProgressService progress,
            INotificationService notifications, IMyExamScopeService myExams)
        {
            _db = db;
            _progress = progress;
            _notifications = notifications;
            _myExams = myExams;
        }

        // ------------------------------------------------------------------ shared helpers

        private async Task<bool> IsBlockedEitherWayAsync(Guid a, Guid b) =>
            await _db.UserBlocks.AnyAsync(x =>
                (x.BlockerUserId == a && x.BlockedUserId == b) || (x.BlockerUserId == b && x.BlockedUserId == a));

        private async Task<StudyPartnership?> GetPartnershipAsync(Guid a, Guid b)
        {
            var (x, y) = StudyPartnerRules.Canonical(a, b);
            return await _db.StudyPartnerships.FirstOrDefaultAsync(p => p.UserAId == x && p.UserBId == y);
        }

        private async Task<UserPrivacySettings> GetPrivacyAsync(Guid userId) =>
            await _db.PrivacySettings.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId)
            ?? StudyPartnerRules.DefaultPrivacy(userId);

        private async Task<HashSet<Guid>> PartnerIdsAsync(Guid me)
        {
            var rows = await _db.StudyPartnerships.AsNoTracking()
                .Where(p => p.UserAId == me || p.UserBId == me)
                .Select(p => p.UserAId == me ? p.UserBId : p.UserAId)
                .ToListAsync();
            return rows.ToHashSet();
        }

        private async Task<List<Guid>> BlockedEitherWayIdsAsync(Guid me)
        {
            var rows = await _db.UserBlocks.AsNoTracking()
                .Where(b => b.BlockerUserId == me || b.BlockedUserId == me)
                .Select(b => b.BlockerUserId == me ? b.BlockedUserId : b.BlockerUserId)
                .ToListAsync();
            return rows;
        }

        /// <summary>Exams BOTH students have in My Exams (the only valid scopes for compare/challenges).</summary>
        private async Task<List<SpExamRefDto>> CommonExamsAsync(Guid a, Guid b)
        {
            var aExams = _db.UserExamPreferences.Where(p => p.UserId == a).Select(p => p.ExamId);
            return await _db.UserExamPreferences.AsNoTracking()
                .Where(p => p.UserId == b && aExams.Contains(p.ExamId))
                .Select(p => new SpExamRefDto { Id = p.ExamId, Name = p.Exam!.Name })
                .OrderBy(e => e.Name)
                .ToListAsync();
        }

        /// <summary>Basic identity for many students in a fixed number of queries (no N+1). The primary
        /// exam is only filled in where that student's Profile visibility lets this viewer see it.</summary>
        private async Task<Dictionary<Guid, SpPersonDto>> PersonsAsync(IReadOnlyCollection<Guid> ids, HashSet<Guid> partnerIds)
        {
            var users = await _db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id) && u.IsActive)
                .Select(u => new { u.Id, u.Username, u.FullName, u.PhotoUrl })
                .ToListAsync();
            var userIds = users.Select(u => u.Id).ToList();

            var privacy = await _db.PrivacySettings.AsNoTracking()
                .Where(p => userIds.Contains(p.UserId))
                .ToDictionaryAsync(p => p.UserId, p => p.ProfileVisibility);

            var prefs = await _db.UserExamPreferences.AsNoTracking()
                .Where(p => userIds.Contains(p.UserId))
                .Select(p => new { p.UserId, p.IsPrimary, Name = p.Exam!.Name })
                .ToListAsync();
            var primary = prefs.GroupBy(p => p.UserId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.IsPrimary).ThenBy(p => p.Name).First().Name);

            return users.ToDictionary(u => u.Id, u =>
            {
                var vis = privacy.TryGetValue(u.Id, out var v) ? v : VisibilityLevel.Everyone;
                var canSeeExam = StudyPartnerRules.CanView(vis, false, partnerIds.Contains(u.Id));
                return new SpPersonDto
                {
                    UserId = u.Id,
                    Username = u.Username,
                    FullName = u.FullName,
                    PhotoUrl = u.PhotoUrl,
                    PrimaryExam = canSeeExam && primary.TryGetValue(u.Id, out var ex) ? ex : null
                };
            });
        }

        private static SpProgressDto ToDto(ProgressSummary s) => new()
        {
            QuestionsAttempted = s.QuestionsAttempted,
            QuestionsCorrect = s.QuestionsCorrect,
            AccuracyPercent = s.AccuracyPercent,
            PracticeTests = s.PracticeTests,
            MockTests = s.MockTests,
            PypAttempts = s.PypAttempts,
            Quizzes = s.Quizzes,
            TestsCompleted = s.TestsCompleted,
            CurrentStreak = s.CurrentStreak,
            LongestStreak = s.LongestStreak,
            TotalXp = s.TotalXp,
            Badges = s.Badges,
            QuestionsLast7Days = s.QuestionsLast7Days
        };

        private async Task<bool> TryNotifyAsync(Guid userId, NotificationType type, string title, string body, string tab, string dedup)
        {
            // Notifications are an add-on: a failure here must never fail the action the student took.
            try
            {
                await _notifications.CreateAsync(userId, new NotificationRequest
                {
                    Type = type,
                    Title = title,
                    Body = body,
                    LinkUrl = "/study-partners",
                    EntityType = "StudyPartner",
                    EntityId = tab, // "requests" | "partners" | "challenges" -- the mobile router's tab name
                    DedupKey = dedup
                });
                return true;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ search

        /// <summary>Find students by username or name (min 2 characters). Students who blocked you, or whose
        /// Profile visibility is "Only Me" (and aren't already your partner), never appear.</summary>
        [HttpGet("search")]
        [ProducesResponseType(typeof(List<SpSearchResultDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<SpSearchResultDto>>> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return Ok(new List<SpSearchResultDto>());
            var me = User.GetUserId();
            var term = q.Trim().ToLowerInvariant();

            var blockedMe = _db.UserBlocks.Where(b => b.BlockedUserId == me).Select(b => b.BlockerUserId);
            var candidates = await _db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.Id != me && !blockedMe.Contains(u.Id))
                .Where(u => u.Username.Contains(term) || u.FullName.ToLower().Contains(term))
                .OrderBy(u => u.Username)
                .Take(40)
                .Select(u => u.Id)
                .ToListAsync();
            if (candidates.Count == 0) return Ok(new List<SpSearchResultDto>());

            var partnerIds = await PartnerIdsAsync(me);
            var settings = await _db.PrivacySettings.AsNoTracking().Where(p => candidates.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId);
            candidates = candidates
                .Where(id => partnerIds.Contains(id) || !settings.TryGetValue(id, out var s) || s.ProfileVisibility != VisibilityLevel.OnlyMe)
                .Take(20).ToList();

            var persons = await PersonsAsync(candidates, partnerIds);
            var iBlocked = (await _db.UserBlocks.AsNoTracking().Where(b => b.BlockerUserId == me && candidates.Contains(b.BlockedUserId))
                .Select(b => b.BlockedUserId).ToListAsync()).ToHashSet();
            var requests = await _db.StudyPartnerRequests.AsNoTracking()
                .Where(r => (r.SenderUserId == me && candidates.Contains(r.ReceiverUserId)) || (r.ReceiverUserId == me && candidates.Contains(r.SenderUserId)))
                .OrderByDescending(r => r.CreatedAt).ToListAsync();

            var result = new List<SpSearchResultDto>();
            foreach (var id in candidates)
            {
                if (!persons.TryGetValue(id, out var p)) continue;
                var dto = new SpSearchResultDto { UserId = p.UserId, Username = p.Username, FullName = p.FullName, PhotoUrl = p.PhotoUrl, PrimaryExam = p.PrimaryExam };

                var sent = requests.FirstOrDefault(r => r.SenderUserId == me && r.ReceiverUserId == id);
                var received = requests.FirstOrDefault(r => r.ReceiverUserId == me && r.SenderUserId == id && r.Status == StudyPartnerRequestStatus.Pending);

                if (iBlocked.Contains(id)) dto.ConnectionState = "Blocked";
                else if (partnerIds.Contains(id)) dto.ConnectionState = "StudyPartner";
                else if (sent is { Status: StudyPartnerRequestStatus.Pending }) { dto.ConnectionState = "RequestSent"; dto.RequestId = sent.Id; }
                else if (received != null) { dto.ConnectionState = "RequestReceived"; dto.RequestId = received.Id; }
                else if (sent is { Status: StudyPartnerRequestStatus.Rejected } && DateTime.UtcNow - (sent.RespondedAt ?? sent.CreatedAt) < StudyPartnerRules.ResendCooldown)
                    dto.ConnectionState = "RequestRejected";
                else if (settings.TryGetValue(id, out var s) && !s.AllowStudyPartnerRequests) dto.ConnectionState = "Restricted";
                result.Add(dto);
            }
            return Ok(result);
        }

        // ------------------------------------------------------------------ requests

        /// <summary>Send a Study Partner request. 409 if already partners / already requested / they already
        /// asked you; 403 if blocked either way or they restrict requests; 429 right after a rejection.</summary>
        [HttpPost("requests")]
        [EnableRateLimiting("content-post")]
        [ProducesResponseType(typeof(SpRequestDto), StatusCodes.Status201Created)]
        public async Task<ActionResult<SpRequestDto>> SendRequest(SpSendRequestDto dto)
        {
            var me = User.GetUserId();
            if (dto.ReceiverUserId == me) return BadRequest(new { message = "You can't add yourself as a Study Partner." });

            var receiver = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == dto.ReceiverUserId && u.IsActive);
            if (receiver == null) return NotFound(new { message = "That student wasn't found." });

            if (await IsBlockedEitherWayAsync(me, receiver.Id))
                return StatusCode(403, new { message = "You can't send a Study Partner request to this student." });

            if (await GetPartnershipAsync(me, receiver.Id) != null)
                return Conflict(new { message = "You're already Study Partners." });

            if (!(await GetPrivacyAsync(receiver.Id)).AllowStudyPartnerRequests)
                return StatusCode(403, new { message = "This student isn't accepting Study Partner requests." });

            var existing = await _db.StudyPartnerRequests
                .Where(r => (r.SenderUserId == me && r.ReceiverUserId == receiver.Id) || (r.SenderUserId == receiver.Id && r.ReceiverUserId == me))
                .OrderByDescending(r => r.CreatedAt).ToListAsync();

            if (existing.Any(r => r.SenderUserId == me && r.Status == StudyPartnerRequestStatus.Pending))
                return Conflict(new { message = "You've already sent this student a request." });
            if (existing.Any(r => r.SenderUserId == receiver.Id && r.Status == StudyPartnerRequestStatus.Pending))
                return Conflict(new { message = "This student already sent you a request -- check Study Partner Requests to accept it." });

            var lastRejected = existing.FirstOrDefault(r => r.SenderUserId == me && r.Status == StudyPartnerRequestStatus.Rejected);
            if (lastRejected != null && DateTime.UtcNow - (lastRejected.RespondedAt ?? lastRejected.CreatedAt) < StudyPartnerRules.ResendCooldown)
                return StatusCode(429, new { message = "You can send this student another request in a few days." });

            var request = new StudyPartnerRequest { SenderUserId = me, ReceiverUserId = receiver.Id };
            _db.StudyPartnerRequests.Add(request);
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Two taps at once -- the unique (sender, receiver, pending) index caught the second.
                return Conflict(new { message = "You've already sent this student a request." });
            }

            var senderName = await _db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync();
            await TryNotifyAsync(receiver.Id, NotificationType.StudyPartnerRequest, "New Study Partner request",
                $"{senderName} would like to prepare with you.", "requests", $"StudyPartnerRequest:{request.Id}");

            var persons = await PersonsAsync(new[] { receiver.Id }, new HashSet<Guid>());
            return StatusCode(201, new SpRequestDto { Id = request.Id, Person = persons[receiver.Id], CreatedAt = request.CreatedAt });
        }

        /// <summary>Your pending requests: ones you received (Incoming) and ones you sent (Outgoing).</summary>
        [HttpGet("requests")]
        [ProducesResponseType(typeof(SpRequestsResponseDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<SpRequestsResponseDto>> ListRequests()
        {
            var me = User.GetUserId();
            var blocked = await BlockedEitherWayIdsAsync(me);

            var rows = await _db.StudyPartnerRequests.AsNoTracking()
                .Where(r => r.Status == StudyPartnerRequestStatus.Pending && (r.SenderUserId == me || r.ReceiverUserId == me))
                .Where(r => !blocked.Contains(r.SenderUserId == me ? r.ReceiverUserId : r.SenderUserId))
                .OrderByDescending(r => r.CreatedAt)
                .Take(PageSizeMax * 2)
                .ToListAsync();

            var otherIds = rows.Select(r => r.SenderUserId == me ? r.ReceiverUserId : r.SenderUserId).Distinct().ToList();
            var persons = await PersonsAsync(otherIds, new HashSet<Guid>());

            SpRequestDto Map(StudyPartnerRequest r, Guid otherId) => new() { Id = r.Id, Person = persons[otherId], CreatedAt = r.CreatedAt };
            return Ok(new SpRequestsResponseDto
            {
                Incoming = rows.Where(r => r.ReceiverUserId == me && persons.ContainsKey(r.SenderUserId)).Select(r => Map(r, r.SenderUserId)).ToList(),
                Outgoing = rows.Where(r => r.SenderUserId == me && persons.ContainsKey(r.ReceiverUserId)).Select(r => Map(r, r.ReceiverUserId)).ToList()
            });
        }

        /// <summary>Accept a request you received. Creates the single mutual relationship row.</summary>
        [HttpPost("requests/{id:guid}/accept")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Accept(Guid id)
        {
            var me = User.GetUserId();
            var request = await _db.StudyPartnerRequests.FirstOrDefaultAsync(r => r.Id == id && r.ReceiverUserId == me && r.Status == StudyPartnerRequestStatus.Pending);
            if (request == null) return NotFound(new { message = "That request is no longer available." });

            if (await IsBlockedEitherWayAsync(me, request.SenderUserId))
                return NotFound(new { message = "That request is no longer available." });

            var now = DateTime.UtcNow;
            request.Status = StudyPartnerRequestStatus.Accepted;
            request.RespondedAt = now;

            // If both of you had asked each other, the crossed request is settled too.
            var crossed = await _db.StudyPartnerRequests
                .Where(r => r.SenderUserId == me && r.ReceiverUserId == request.SenderUserId && r.Status == StudyPartnerRequestStatus.Pending)
                .ToListAsync();
            foreach (var c in crossed) { c.Status = StudyPartnerRequestStatus.Accepted; c.RespondedAt = now; }

            if (await GetPartnershipAsync(me, request.SenderUserId) == null)
            {
                var (a, b) = StudyPartnerRules.Canonical(me, request.SenderUserId);
                _db.StudyPartnerships.Add(new StudyPartnership { UserAId = a, UserBId = b });
            }

            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // The unique pair index fired: the partnership already exists (double tap) -- that's the
                // outcome the student wanted, so make sure the request itself is marked and succeed.
                _db.ChangeTracker.Clear();
                await _db.StudyPartnerRequests.Where(r => r.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, StudyPartnerRequestStatus.Accepted).SetProperty(r => r.RespondedAt, now));
            }

            var myName = await _db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync();
            await TryNotifyAsync(request.SenderUserId, NotificationType.StudyPartnerAccepted, "Study Partner request accepted",
                $"{myName} is now your Study Partner.", "partners", $"StudyPartnerAccepted:{id}");
            return NoContent();
        }

        /// <summary>Decline a request you received. The sender isn't notified.</summary>
        [HttpPost("requests/{id:guid}/reject")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Reject(Guid id)
        {
            var me = User.GetUserId();
            var request = await _db.StudyPartnerRequests.FirstOrDefaultAsync(r => r.Id == id && r.ReceiverUserId == me && r.Status == StudyPartnerRequestStatus.Pending);
            if (request == null) return NotFound(new { message = "That request is no longer available." });
            request.Status = StudyPartnerRequestStatus.Rejected;
            request.RespondedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Cancel a request you sent (only while it's still pending).</summary>
        [HttpDelete("requests/{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> CancelRequest(Guid id)
        {
            var me = User.GetUserId();
            var request = await _db.StudyPartnerRequests.FirstOrDefaultAsync(r => r.Id == id && r.SenderUserId == me && r.Status == StudyPartnerRequestStatus.Pending);
            if (request == null) return NotFound(new { message = "That request is no longer available." });
            request.Status = StudyPartnerRequestStatus.Cancelled;
            request.RespondedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ------------------------------------------------------------------ partners

        /// <summary>My Study Partners (paged). Streak and recent activity appear only for partners whose
        /// Progress visibility allows you.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<SpPartnerDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<SpPartnerDto>>> ListPartners([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var me = User.GetUserId();
            var size = Math.Clamp(pageSize, 1, PageSizeMax);

            var rows = await _db.StudyPartnerships.AsNoTracking()
                .Where(p => p.UserAId == me || p.UserBId == me)
                .OrderByDescending(p => p.CreatedAt)
                .Skip((Math.Max(1, page) - 1) * size).Take(size)
                .Select(p => new { OtherId = p.UserAId == me ? p.UserBId : p.UserAId, p.CreatedAt })
                .ToListAsync();
            if (rows.Count == 0) return Ok(new List<SpPartnerDto>());

            var ids = rows.Select(r => r.OtherId).ToList();
            var all = await PartnerIdsAsync(me);
            var persons = await PersonsAsync(ids, all);
            var privacy = await _db.PrivacySettings.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, p => p.ProgressVisibility);
            var visible = ids.Where(id => StudyPartnerRules.CanView(privacy.TryGetValue(id, out var v) ? v : VisibilityLevel.StudyPartnersOnly, false, true)).ToList();
            var summaries = await _progress.GetSummariesAsync(visible, null, DateTime.UtcNow.AddDays(-7));

            var result = new List<SpPartnerDto>();
            foreach (var r in rows)
            {
                if (!persons.TryGetValue(r.OtherId, out var p)) continue; // deactivated
                var dto = new SpPartnerDto { UserId = p.UserId, Username = p.Username, FullName = p.FullName, PhotoUrl = p.PhotoUrl, PrimaryExam = p.PrimaryExam, PartnersSince = r.CreatedAt };
                if (summaries.TryGetValue(r.OtherId, out var s)) { dto.CurrentStreak = s.CurrentStreak; dto.QuestionsLast7Days = s.QuestionsLast7Days; }
                result.Add(dto);
            }
            return Ok(result);
        }

        /// <summary>A student's Study Partner profile, trimmed to what THEIR privacy settings allow you to see.</summary>
        [HttpGet("{userId:guid}")]
        [ProducesResponseType(typeof(SpProfileDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<SpProfileDto>> GetProfile(Guid userId)
        {
            var me = User.GetUserId();
            var isOwner = userId == me;
            if (!isOwner && await IsBlockedEitherWayAsync(me, userId)) return NotFound(new { message = "That student wasn't found." });

            var partnership = isOwner ? null : await GetPartnershipAsync(me, userId);
            var isPartner = partnership != null;
            var privacy = await GetPrivacyAsync(userId);

            if (!isOwner && !isPartner && privacy.ProfileVisibility == VisibilityLevel.OnlyMe)
                return NotFound(new { message = "That student wasn't found." });

            var persons = await PersonsAsync(new[] { userId }, isPartner ? new HashSet<Guid> { userId } : new HashSet<Guid>());
            if (!persons.TryGetValue(userId, out var person)) return NotFound(new { message = "That student wasn't found." });

            var dto = new SpProfileDto
            {
                UserId = person.UserId, Username = person.Username, FullName = person.FullName, PhotoUrl = person.PhotoUrl,
                PrimaryExam = person.PrimaryExam, IsPartner = isPartner, PartnersSince = partnership?.CreatedAt
            };

            if (StudyPartnerRules.CanView(privacy.ProfileVisibility, isOwner, isPartner))
            {
                dto.Exams = await _db.UserExamPreferences.AsNoTracking().Where(p => p.UserId == userId)
                    .Select(p => new SpExamRefDto { Id = p.ExamId, Name = p.Exam!.Name }).OrderBy(e => e.Name).ToListAsync();
            }

            if (StudyPartnerRules.CanView(privacy.ProgressVisibility, isOwner, isPartner))
                dto.Progress = ToDto((await _progress.GetSummariesAsync(new[] { userId }))[userId]);
            else
                dto.ProgressRestricted = true;

            return Ok(dto);
        }

        /// <summary>Remove a Study Partner. Any challenge still open between you is cancelled; the
        /// partner's private progress stops being visible to you at once (checks are per-request).
        /// Existing Direct Messages history follows the app's existing retention (it is untouched).</summary>
        [HttpDelete("{userId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> RemovePartner(Guid userId)
        {
            var me = User.GetUserId();
            var partnership = await GetPartnershipAsync(me, userId);
            if (partnership == null) return NotFound(new { message = "That student isn't one of your Study Partners." });

            _db.StudyPartnerships.Remove(partnership);
            await CancelOpenChallengesAsync(me, userId);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ------------------------------------------------------------------ progress / activity / compare

        /// <summary>A partner's (or yourself's) progress, optionally for one exam. 403 if their Progress
        /// visibility hides it from you.</summary>
        [HttpGet("{userId:guid}/progress")]
        [ProducesResponseType(typeof(SpProgressDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<SpProgressDto>> GetProgress(Guid userId, [FromQuery] Guid? examId = null)
        {
            var me = User.GetUserId();
            var isOwner = userId == me;
            if (!isOwner && await IsBlockedEitherWayAsync(me, userId)) return NotFound(new { message = "That student wasn't found." });

            var isPartner = !isOwner && await GetPartnershipAsync(me, userId) != null;
            var privacy = await GetPrivacyAsync(userId);
            if (!StudyPartnerRules.CanView(privacy.ProgressVisibility, isOwner, isPartner))
                return StatusCode(403, new { message = "This student keeps their progress private." });

            if (examId.HasValue && !isOwner && !(await CommonExamsAsync(me, userId)).Any(e => e.Id == examId.Value))
                return BadRequest(new { message = "Pick an exam you both have in My Exams." });

            var scope = examId.HasValue ? new[] { examId.Value } : null;
            return Ok(ToDto((await _progress.GetSummariesAsync(new[] { userId }, scope))[userId]));
        }

        /// <summary>Recent study activity (last 10 completed attempts). Hidden -> Restricted=true, no items.</summary>
        [HttpGet("{userId:guid}/activity")]
        [ProducesResponseType(typeof(SpActivityResponseDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<SpActivityResponseDto>> GetActivity(Guid userId)
        {
            var me = User.GetUserId();
            var isOwner = userId == me;
            if (!isOwner && await IsBlockedEitherWayAsync(me, userId)) return NotFound(new { message = "That student wasn't found." });

            var isPartner = !isOwner && await GetPartnershipAsync(me, userId) != null;
            var privacy = await GetPrivacyAsync(userId);
            if (!StudyPartnerRules.CanView(privacy.ActivityVisibility, isOwner, isPartner))
                return Ok(new SpActivityResponseDto { Restricted = true });

            // Only the last 30 days are scanned -- "recent activity", not history.
            var attempts = await _progress.GetAttemptsAsync(new[] { userId }, null, DateTime.UtcNow.AddDays(-30));
            var recent = attempts.OrderByDescending(a => a.AttemptedAt).Take(10).ToList();
            var examIds = recent.Where(a => a.ExamId.HasValue).Select(a => a.ExamId!.Value).Distinct().ToList();
            var names = await _db.Exams.AsNoTracking().Where(e => examIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name);

            return Ok(new SpActivityResponseDto
            {
                Items = recent.Select(a => new SpActivityItemDto
                {
                    Kind = a.Kind.ToString(),
                    ExamName = a.ExamId.HasValue && names.TryGetValue(a.ExamId.Value, out var n) ? n : null,
                    QuestionsAnswered = a.Answered,
                    At = a.AttemptedAt
                }).ToList()
            });
        }

        /// <summary>Side-by-side progress with a Study Partner, overall or for one exam you BOTH have in My
        /// Exams. Same numbers as the Progress page (shared StudentProgressService).</summary>
        [HttpGet("{userId:guid}/compare")]
        [ProducesResponseType(typeof(SpCompareDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<SpCompareDto>> Compare(Guid userId, [FromQuery] Guid? examId = null)
        {
            var me = User.GetUserId();
            if (userId == me) return BadRequest(new { message = "Pick one of your Study Partners to compare with." });
            if (await IsBlockedEitherWayAsync(me, userId)) return NotFound(new { message = "That student wasn't found." });
            if (await GetPartnershipAsync(me, userId) == null)
                return StatusCode(403, new { message = "You can compare progress with your Study Partners." });

            var privacy = await GetPrivacyAsync(userId);
            if (!StudyPartnerRules.CanView(privacy.ProgressVisibility, false, true))
                return StatusCode(403, new { message = "This Study Partner keeps their progress private." });

            var common = await CommonExamsAsync(me, userId);
            SpExamRefDto? exam = null;
            if (examId.HasValue)
            {
                exam = common.FirstOrDefault(e => e.Id == examId.Value);
                if (exam == null) return BadRequest(new { message = "Pick an exam you both have in My Exams." });
            }

            var scope = exam != null ? new[] { exam.Id } : null;
            var summaries = await _progress.GetSummariesAsync(new[] { me, userId }, scope);
            var persons = await PersonsAsync(new[] { userId }, new HashSet<Guid> { userId });
            if (!persons.TryGetValue(userId, out var partner)) return NotFound(new { message = "That student wasn't found." });

            return Ok(new SpCompareDto
            {
                Exam = exam,
                CommonExams = common,
                Partner = partner,
                Me = ToDto(summaries[me]),
                PartnerProgress = ToDto(summaries[userId])
            });
        }

        // ------------------------------------------------------------------ leaderboard

        /// <summary>Study Partner leaderboard: you and your partners, ranked on a transparent metric (no
        /// invented points). A partner appears only if they chose to be on leaderboards AND their Progress
        /// visibility allows you -- the leaderboard never exposes numbers their settings hide.
        /// period: today|week|month|all. metric: questions|accuracy|tests|streak|xp. XP, streak and
        /// accuracy rank use the same rows as the Progress page; streak/XP are whole-account (not period-limited).</summary>
        [HttpGet("leaderboard")]
        [ProducesResponseType(typeof(SpLeaderboardDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<SpLeaderboardDto>> Leaderboard([FromQuery] string period = "week",
            [FromQuery] string metric = "questions", [FromQuery] Guid? examId = null)
        {
            var me = User.GetUserId();
            period = period.ToLowerInvariant();
            metric = metric.ToLowerInvariant();

            if (!new[] { "today", "week", "month", "all" }.Contains(period))
                return BadRequest(new { message = "Unknown period." });
            if (!new[] { "questions", "accuracy", "tests", "streak", "xp" }.Contains(metric))
                return BadRequest(new { message = "Unknown ranking metric." });

            DateTime? from = period switch
            {
                "today" => _progress.StartOfTodayUtc(),
                "week" => DateTime.UtcNow.AddDays(-7),
                "month" => DateTime.UtcNow.AddDays(-30),
                _ => null
            };

            // Exam filter: only exams in the caller's own My Exams (same rule as the global exam board).
            if (examId.HasValue && !(await _myExams.GetScopeAsync(User)).Allows(examId.Value))
                return BadRequest(new { message = "That exam isn't in your My Exams." });

            var partnerIds = await PartnerIdsAsync(me);
            var blocked = await BlockedEitherWayIdsAsync(me);
            var candidates = partnerIds.Where(id => !blocked.Contains(id)).ToList();

            var settings = await _db.PrivacySettings.AsNoTracking().Where(p => candidates.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId);
            var participants = candidates.Where(id =>
            {
                var set = settings.TryGetValue(id, out var v) ? v : StudyPartnerRules.DefaultPrivacy(id);
                return set.ShowOnLeaderboard && StudyPartnerRules.CanView(set.ProgressVisibility, false, true);
            }).ToList();
            participants.Add(me);

            var scope = examId.HasValue ? new[] { examId.Value } : null;
            var summaries = await _progress.GetSummariesAsync(participants, scope, from);
            var persons = await PersonsAsync(participants, partnerIds);
            var self = await _db.Users.AsNoTracking().Where(u => u.Id == me).Select(u => new { u.Username, u.FullName, u.PhotoUrl }).FirstAsync();

            var rows = participants
                .Select(id => new { Id = id, S = summaries[id] })
                // "Complete some practice to appear": for activity metrics, no activity in the period = not listed.
                .Where(x => metric is "streak" or "xp" || x.S.QuestionsAttempted > 0)
                .ToList();

            decimal Key(ProgressSummary s) => metric switch
            {
                "accuracy" => s.QuestionsAttempted >= StudyPartnerRules.MinAnsweredForAccuracyRank ? s.AccuracyPercent : -1,
                "tests" => s.TestsCompleted,
                "streak" => s.CurrentStreak,
                "xp" => s.TotalXp,
                _ => s.QuestionsAttempted
            };

            var entries = rows
                .OrderByDescending(x => Key(x.S)).ThenByDescending(x => x.S.QuestionsAttempted)
                .Take(PageSizeMax)
                .Select((x, i) =>
                {
                    persons.TryGetValue(x.Id, out var p);
                    return new SpLeaderboardEntryDto
                    {
                        Rank = i + 1,
                        UserId = x.Id,
                        IsMe = x.Id == me,
                        Username = x.Id == me ? self.Username : p?.Username ?? string.Empty,
                        FullName = x.Id == me ? self.FullName : p?.FullName ?? string.Empty,
                        PhotoUrl = x.Id == me ? self.PhotoUrl : p?.PhotoUrl,
                        QuestionsAttempted = x.S.QuestionsAttempted,
                        AccuracyPercent = x.S.AccuracyPercent,
                        TestsCompleted = x.S.TestsCompleted,
                        CurrentStreak = x.S.CurrentStreak,
                        TotalXp = x.S.TotalXp
                    };
                })
                .ToList();

            return Ok(new SpLeaderboardDto { Period = period, Metric = metric, ExamId = examId, Entries = entries });
        }

        // ------------------------------------------------------------------ privacy settings

        /// <summary>Your Study Partner privacy settings (privacy-conscious defaults if never changed).</summary>
        [HttpGet("privacy")]
        [ProducesResponseType(typeof(SpPrivacyDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<SpPrivacyDto>> GetPrivacy()
        {
            var p = await GetPrivacyAsync(User.GetUserId());
            return Ok(new SpPrivacyDto
            {
                ProfileVisibility = p.ProfileVisibility.ToString(),
                ProgressVisibility = p.ProgressVisibility.ToString(),
                ActivityVisibility = p.ActivityVisibility.ToString(),
                ShowOnLeaderboard = p.ShowOnLeaderboard,
                AllowStudyPartnerRequests = p.AllowStudyPartnerRequests
            });
        }

        [HttpPut("privacy")]
        [ProducesResponseType(typeof(SpPrivacyDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<SpPrivacyDto>> UpdatePrivacy(SpPrivacyDto dto)
        {
            if (!Enum.TryParse<VisibilityLevel>(dto.ProfileVisibility, true, out var profile) ||
                !Enum.TryParse<VisibilityLevel>(dto.ProgressVisibility, true, out var progress) ||
                !Enum.TryParse<VisibilityLevel>(dto.ActivityVisibility, true, out var activity))
                return BadRequest(new { message = "Visibility must be Everyone, StudyPartnersOnly or OnlyMe." });

            var me = User.GetUserId();
            var row = await _db.PrivacySettings.FirstOrDefaultAsync(p => p.UserId == me);
            if (row == null) { row = StudyPartnerRules.DefaultPrivacy(me); _db.PrivacySettings.Add(row); }

            row.ProfileVisibility = profile;
            row.ProgressVisibility = progress;
            row.ActivityVisibility = activity;
            row.ShowOnLeaderboard = dto.ShowOnLeaderboard;
            row.AllowStudyPartnerRequests = dto.AllowStudyPartnerRequests;
            row.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return await GetPrivacy();
        }

        // ------------------------------------------------------------------ blocking

        /// <summary>Block a student: ends any partnership, cancels pending requests and open challenges
        /// between you, and from then on you can't find, message or request each other (Direct Messages
        /// and user search honour the same block).</summary>
        [HttpPost("blocks/{userId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Block(Guid userId)
        {
            var me = User.GetUserId();
            if (userId == me) return BadRequest(new { message = "You can't block yourself." });
            if (!await _db.Users.AnyAsync(u => u.Id == userId && u.IsActive)) return NotFound(new { message = "That student wasn't found." });

            if (!await _db.UserBlocks.AnyAsync(b => b.BlockerUserId == me && b.BlockedUserId == userId))
                _db.UserBlocks.Add(new UserBlock { BlockerUserId = me, BlockedUserId = userId });

            var partnership = await GetPartnershipAsync(me, userId);
            if (partnership != null) _db.StudyPartnerships.Remove(partnership);

            var now = DateTime.UtcNow;
            var pending = await _db.StudyPartnerRequests
                .Where(r => r.Status == StudyPartnerRequestStatus.Pending &&
                            ((r.SenderUserId == me && r.ReceiverUserId == userId) || (r.SenderUserId == userId && r.ReceiverUserId == me)))
                .ToListAsync();
            foreach (var r in pending) { r.Status = StudyPartnerRequestStatus.Cancelled; r.RespondedAt = now; }

            await CancelOpenChallengesAsync(me, userId);
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException) { /* double tap: the block already exists */ }
            return NoContent();
        }

        [HttpDelete("blocks/{userId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Unblock(Guid userId)
        {
            var me = User.GetUserId();
            await _db.UserBlocks.Where(b => b.BlockerUserId == me && b.BlockedUserId == userId).ExecuteDeleteAsync();
            return NoContent();
        }

        /// <summary>Students you've blocked (so you can unblock them).</summary>
        [HttpGet("blocks")]
        [ProducesResponseType(typeof(List<SpPersonDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<SpPersonDto>>> ListBlocks()
        {
            var me = User.GetUserId();
            return Ok(await _db.UserBlocks.AsNoTracking()
                .Where(b => b.BlockerUserId == me)
                .OrderByDescending(b => b.CreatedAt).Take(PageSizeMax)
                .Select(b => new SpPersonDto { UserId = b.BlockedUserId, Username = b.Blocked!.Username, FullName = b.Blocked!.FullName, PhotoUrl = b.Blocked!.PhotoUrl })
                .ToListAsync());
        }
    }
}
