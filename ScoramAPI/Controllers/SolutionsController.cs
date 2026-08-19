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
    // "Multiple Solutions" per question: Official/Teacher solutions come from admins (auto-approved,
    // since staff-authored); Shortcut/Alternative/Community solutions come from students and need
    // approval before anyone besides the submitter can see them -- see ListForQuestion's visibility
    // rule below. This used to auto-approve every student submission because there was no moderation
    // UI to review them; now that ModerateSolutions exists as a real permission with a real queue
    // (GetPending), that workaround is gone.
    [ApiController]
    [Route("api")]
    public class SolutionsController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IAuditLogService _audit;
        private readonly IGamificationService _gamification;

        public SolutionsController(ScoramDbContext db, IAdminPermissionService permissions, IAuditLogService audit, IGamificationService gamification)
        {
            _db = db;
            _permissions = permissions;
            _audit = audit;
            _gamification = gamification;
        }

        // GET /api/questions/{questionId}/solutions -- publicly readable (no [Authorize]), same as
        // the question itself. Ordered so the most useful solution surfaces first: the admin-marked
        // "easiest method", then admin-set Priority, then upvotes, then newest first.
        [HttpGet("questions/{questionId:guid}/solutions")]
        public async Task<ActionResult<List<SolutionResponseDto>>> ListForQuestion(Guid questionId)
        {
            var questionExists = await _db.Questions.AnyAsync(q => q.Id == questionId);
            if (!questionExists) return NotFound(new { message = "Question not found." });

            return Ok(await ListSolutionsAsync(s => s.QuestionId == questionId));
        }

        // GET /api/question-bank/{questionId}/solutions -- same "Alternative Solution" feature
        // (section 28-B), reusing this exact table/ordering/visibility logic for Question Bank
        // questions instead of duplicating a parallel endpoint (section 28-E).
        [HttpGet("question-bank/{questionId:guid}/solutions")]
        public async Task<ActionResult<List<SolutionResponseDto>>> ListForQuestionBankQuestion(Guid questionId)
        {
            var questionExists = await _db.QuestionBankQuestions.AnyAsync(q => q.Id == questionId && q.IsActive);
            if (!questionExists) return NotFound(new { message = "Question not found." });

            return Ok(await ListSolutionsAsync(s => s.QuestionBankQuestionId == questionId));
        }

        private async Task<List<SolutionResponseDto>> ListSolutionsAsync(System.Linq.Expressions.Expression<Func<QuestionSolution, bool>> matchesQuestion)
        {
            // Anonymous visitors and other students never see a solution pending approval -- except
            // the student who submitted it, so their own submission doesn't just vanish from their
            // view with no feedback while it's waiting on a moderator.
            var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            Guid? currentUserId = isAuthenticated ? User.GetUserId() : null;

            var solutions = await _db.QuestionSolutions
                .Where(matchesQuestion)
                .Where(s => s.IsApproved || s.SubmittedByUserId == currentUserId)
                .Include(s => s.SubmittedByUser)
                .Include(s => s.SubmittedByAdmin)
                .OrderByDescending(s => s.IsEasiestMethod)
                .ThenByDescending(s => s.Priority)
                .ThenByDescending(s => s.UpvoteCount)
                .ThenByDescending(s => s.CreatedAt)
                .ToListAsync();

            return solutions.Select(ToResponseDto).ToList();
        }

        // POST /api/questions/{questionId}/solutions -- student submission. Starts unapproved; only
        // visible to its author (see ListForQuestion) until an admin approves it via the queue below.
        [HttpPost("questions/{questionId:guid}/solutions")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<SolutionResponseDto>> Create(Guid questionId, SolutionCreateDto dto)
        {
            var questionExists = await _db.Questions.AnyAsync(q => q.Id == questionId);
            if (!questionExists) return NotFound(new { message = "Question not found." });

            return await CreateSolutionAsync(dto, s => s.QuestionId = questionId);
        }

        // POST /api/question-bank/{questionId}/solutions -- student "Suggest Alternative Method"
        // submission on a Question Bank question (section 28-B).
        [HttpPost("question-bank/{questionId:guid}/solutions")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<SolutionResponseDto>> CreateForQuestionBank(Guid questionId, SolutionCreateDto dto)
        {
            var questionExists = await _db.QuestionBankQuestions.AnyAsync(q => q.Id == questionId && q.IsActive);
            if (!questionExists) return NotFound(new { message = "Question not found." });

            return await CreateSolutionAsync(dto, s => s.QuestionBankQuestionId = questionId);
        }

        private async Task<ActionResult<SolutionResponseDto>> CreateSolutionAsync(SolutionCreateDto dto, Action<QuestionSolution> assignQuestion)
        {
            if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { message = "A short title is required." });
            if (string.IsNullOrWhiteSpace(dto.SolutionText)) return BadRequest(new { message = "Solution text is required." });

            var userId = User.GetUserId();
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return Unauthorized();

            var solution = new QuestionSolution
            {
                Title = dto.Title.Trim(),
                SolutionType = dto.SolutionType,
                SubmittedByUserId = userId,
                SolutionText = dto.SolutionText,
                ImageUrl = dto.ImageUrl,
                IsApproved = false,
                CreatedAt = DateTime.UtcNow
            };
            assignQuestion(solution);

            _db.QuestionSolutions.Add(solution);
            await _db.SaveChangesAsync();

            // GAMIFICATION -- streak credit for today's contribution happens now, at submission time.
            // The XP itself is deliberately deferred to Approve() below, once a moderator has actually
            // looked at it -- otherwise spamming low-effort "solutions" would be a free XP farm.
            await _gamification.TouchStreakOnlyAsync(userId);

            solution.SubmittedByUser = user;
            return Ok(ToResponseDto(solution));
        }

        // POST /api/admin/questions/{questionId}/solutions -- admin submission (Official/Teacher
        // solutions). Auto-approved: it's already staff-authored, so there's nothing to moderate.
        [HttpPost("admin/questions/{questionId:guid}/solutions")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<SolutionResponseDto>> CreateByAdmin(Guid questionId, SolutionCreateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateSolutions))
                return Forbid();

            var questionExists = await _db.Questions.AnyAsync(q => q.Id == questionId);
            if (!questionExists) return NotFound(new { message = "Question not found." });

            var result = await CreateSolutionByAdminAsync(dto, s => s.QuestionId = questionId);
            if (result.Value != null)
                await _audit.LogAsync(User.GetAdminId(), "Solution.CreateByAdmin", "Question", questionId, dto.Title);
            return result;
        }

        // POST /api/admin/question-bank/{questionId}/solutions -- admin/official solution on a
        // Question Bank question. Same auto-approved rule as the legacy endpoint above.
        [HttpPost("admin/question-bank/{questionId:guid}/solutions")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<SolutionResponseDto>> CreateByAdminForQuestionBank(Guid questionId, SolutionCreateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateSolutions))
                return Forbid();

            var questionExists = await _db.QuestionBankQuestions.AnyAsync(q => q.Id == questionId && q.IsActive);
            if (!questionExists) return NotFound(new { message = "Question not found." });

            var result = await CreateSolutionByAdminAsync(dto, s => s.QuestionBankQuestionId = questionId);
            if (result.Value != null)
                await _audit.LogAsync(User.GetAdminId(), "Solution.CreateByAdmin", "QuestionBankQuestion", questionId, dto.Title);
            return result;
        }

        private async Task<ActionResult<SolutionResponseDto>> CreateSolutionByAdminAsync(SolutionCreateDto dto, Action<QuestionSolution> assignQuestion)
        {
            if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { message = "A short title is required." });
            if (string.IsNullOrWhiteSpace(dto.SolutionText)) return BadRequest(new { message = "Solution text is required." });

            var adminId = User.GetAdminId();
            var admin = await _db.Admins.FindAsync(adminId);
            if (admin == null) return Unauthorized();

            var solution = new QuestionSolution
            {
                Title = dto.Title.Trim(),
                SolutionType = dto.SolutionType,
                SubmittedByAdminId = adminId,
                SolutionText = dto.SolutionText,
                ImageUrl = dto.ImageUrl,
                IsApproved = true,
                IsVerified = true,
                CreatedAt = DateTime.UtcNow
            };
            assignQuestion(solution);

            _db.QuestionSolutions.Add(solution);
            await _db.SaveChangesAsync();

            solution.SubmittedByAdmin = admin;
            return Ok(ToResponseDto(solution));
        }

        // GET /api/admin/solutions/pending -- the moderation queue: every student-submitted solution
        // still waiting on a decision, oldest first (so nothing sits forgotten at the bottom).
        [HttpGet("admin/solutions/pending")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<PagedResult<PendingSolutionDto>>> GetPending([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateSolutions))
                return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            // Question Bank additions: QuestionId is now nullable and a pending solution can belong to
            // either a legacy Question OR a QuestionBankQuestion (SCORAM_QUESTION_BANK), so both
            // Include chains are loaded and the mapper below picks whichever one is actually set.
            var query = _db.QuestionSolutions
                .Include(s => s.SubmittedByUser)
                .Include(s => s.Question).ThenInclude(q => q!.Paper).ThenInclude(p => p!.Exam)
                .Include(s => s.QuestionBankQuestion).ThenInclude(q => q!.Subject)
                .Include(s => s.QuestionBankQuestion).ThenInclude(q => q!.Topic)
                .Where(s => !s.IsApproved)
                .OrderBy(s => s.CreatedAt);

            var totalCount = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new PagedResult<PendingSolutionDto>
            {
                Items = items.Select(ToPendingDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // PATCH /api/admin/solutions/{id}/approve
        [HttpPatch("admin/solutions/{id:guid}/approve")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Approve(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateSolutions))
                return Forbid();

            var solution = await _db.QuestionSolutions.FindAsync(id);
            if (solution == null) return NotFound();

            solution.IsApproved = true;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "Solution.Approve", "Solution", id, solution.Title);

            // GAMIFICATION -- XP for "adding a solution" is granted here, not at submission, so an
            // unmoderated/rejected solution never earns anything.
            if (solution.SubmittedByUserId.HasValue)
                await _gamification.AwardXpAsync(solution.SubmittedByUserId.Value, GamificationService.XpFor(GamificationService.Reasons.SolutionApproved), GamificationService.Reasons.SolutionApproved);

            return Ok(new { solution.Id, solution.IsApproved });
        }

        // DELETE /api/admin/solutions/{id} -- covers both "reject a pending submission" and general
        // moderation removal of something that was approved but later reported/found problematic.
        [HttpDelete("admin/solutions/{id:guid}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Delete(Guid id, [FromQuery] string? reason)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateSolutions))
                return Forbid();

            var solution = await _db.QuestionSolutions.FindAsync(id);
            if (solution == null) return NotFound();

            var wasApproved = solution.IsApproved;
            var title = solution.Title;
            _db.QuestionSolutions.Remove(solution);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), wasApproved ? "Solution.Remove" : "Solution.Reject", "Solution", id,
                string.IsNullOrWhiteSpace(reason) ? title : $"{title} — {reason}");

            return NoContent();
        }

        // PATCH /api/admin/solutions/{id}/priority
        [HttpPatch("admin/solutions/{id:guid}/priority")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> SetPriority(Guid id, [FromBody] int priority)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateSolutions))
                return Forbid();

            var solution = await _db.QuestionSolutions.FindAsync(id);
            if (solution == null) return NotFound();

            solution.Priority = priority;
            await _db.SaveChangesAsync();

            return Ok(new { solution.Id, solution.Priority });
        }

        // POST /api/solutions/{id}/upvote
        // NOTE: no duplicate-upvote prevention yet -- this just increments a counter.
        // Preventing the same student from upvoting twice would need a join table
        // (e.g. SolutionUpvotes: UserId + SolutionId, unique) which isn't modeled yet.
        [HttpPost("solutions/{id:guid}/upvote")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> Upvote(Guid id)
        {
            var solution = await _db.QuestionSolutions.FindAsync(id);
            if (solution == null) return NotFound();

            solution.UpvoteCount++;
            await _db.SaveChangesAsync();

            // GAMIFICATION -- XP for "receiving an upvote" goes to whoever wrote the solution (not
            // the person clicking upvote), and only if a student wrote it -- admin-authored Official
            // solutions have no SubmittedByUserId. Also re-checks the Top Contributor badge threshold
            // (50+ upvotes across all of that student's solutions) every time a new upvote lands.
            if (solution.SubmittedByUserId.HasValue)
            {
                await _gamification.AwardXpAsync(solution.SubmittedByUserId.Value, GamificationService.XpFor(GamificationService.Reasons.SolutionUpvoted), GamificationService.Reasons.SolutionUpvoted);
                await _gamification.CheckTopContributorAsync(solution.SubmittedByUserId.Value);
            }

            return Ok(new { solution.Id, solution.UpvoteCount });
        }

        // PATCH /api/solutions/{id}/verify  (Admin only)
        [HttpPatch("solutions/{id:guid}/verify")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Verify(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateSolutions))
                return Forbid();

            var solution = await _db.QuestionSolutions.FindAsync(id);
            if (solution == null) return NotFound();

            solution.IsVerified = true;
            solution.IsApproved = true;
            await _db.SaveChangesAsync();

            // GAMIFICATION -- "Verified Solver" badge.
            if (solution.SubmittedByUserId.HasValue)
                await _gamification.AwardBadgeByNameAsync(solution.SubmittedByUserId.Value, "Verified Solver");

            return Ok(new { solution.Id, solution.IsVerified });
        }

        // PATCH /api/solutions/{id}/mark-easiest  (Admin only)
        [HttpPatch("solutions/{id:guid}/mark-easiest")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> MarkEasiest(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateSolutions))
                return Forbid();

            var solution = await _db.QuestionSolutions.FindAsync(id);
            if (solution == null) return NotFound();

            // Only one "easiest method" per question at a time -- match on whichever FK this solution
            // actually has set (legacy Question vs QuestionBankQuestion), never both.
            var siblings = solution.QuestionId != null
                ? await _db.QuestionSolutions.Where(s => s.QuestionId == solution.QuestionId && s.Id != id && s.IsEasiestMethod).ToListAsync()
                : await _db.QuestionSolutions.Where(s => s.QuestionBankQuestionId == solution.QuestionBankQuestionId && s.Id != id && s.IsEasiestMethod).ToListAsync();
            foreach (var sibling in siblings) sibling.IsEasiestMethod = false;

            solution.IsEasiestMethod = true;
            await _db.SaveChangesAsync();

            // GAMIFICATION -- "10-Second Trick Master" badge.
            if (solution.SubmittedByUserId.HasValue)
                await _gamification.AwardBadgeByNameAsync(solution.SubmittedByUserId.Value, "10-Second Trick Master");

            return Ok(new { solution.Id, solution.IsEasiestMethod });
        }

        private static PendingSolutionDto ToPendingDto(QuestionSolution s)
        {
            var isQuestionBank = s.QuestionBankQuestionId != null;
            string snippet;
            string context;

            if (isQuestionBank && s.QuestionBankQuestion != null)
            {
                var q = s.QuestionBankQuestion;
                snippet = q.QuestionText.Length > 140 ? q.QuestionText[..140] + "…" : q.QuestionText;
                context = $"{q.Subject?.Name ?? "Unknown"} / {q.Topic?.Name ?? "Unknown"}";
            }
            else if (s.Question != null)
            {
                var q = s.Question;
                snippet = q.QuestionText.Length > 140 ? q.QuestionText[..140] + "…" : q.QuestionText;
                context = q.Paper?.Exam?.Name ?? q.ExamName ?? "Unknown";
            }
            else
            {
                snippet = "(question no longer exists)";
                context = "Unknown";
            }

            return new PendingSolutionDto
            {
                Id = s.Id,
                QuestionId = s.QuestionId,
                QuestionBankQuestionId = s.QuestionBankQuestionId,
                IsQuestionBank = isQuestionBank,
                QuestionTextSnippet = snippet,
                ExamName = context,
                Title = s.Title,
                SolutionType = s.SolutionType.ToString(),
                SolutionText = s.SolutionText,
                ImageUrl = s.ImageUrl,
                SubmittedByName = s.SubmittedByUser != null ? s.SubmittedByUser.FullName : "Unknown",
                CreatedAt = s.CreatedAt
            };
        }

        private static SolutionResponseDto ToResponseDto(QuestionSolution s) => new SolutionResponseDto
        {
            Id = s.Id,
            QuestionId = s.QuestionId,
            QuestionBankQuestionId = s.QuestionBankQuestionId,
            Title = s.Title,
            SolutionType = s.SolutionType.ToString(),
            SolutionText = s.SolutionText,
            ImageUrl = s.ImageUrl,
            SubmittedByName = s.SubmittedByAdmin != null
                ? s.SubmittedByAdmin.FullName
                : (s.SubmittedByUser != null ? s.SubmittedByUser.FullName : "Unknown"),
            SubmittedByAdmin = s.SubmittedByAdminId != null,
            Priority = s.Priority,
            UpvoteCount = s.UpvoteCount,
            IsVerified = s.IsVerified,
            IsEasiestMethod = s.IsEasiestMethod,
            IsApproved = s.IsApproved,
            CreatedAt = s.CreatedAt
        };
    }
}
