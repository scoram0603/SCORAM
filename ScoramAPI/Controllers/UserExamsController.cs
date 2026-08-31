using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Extensions;
using ScoramAPI.Models;

namespace ScoramAPI.Controllers
{
    // "MY EXAMS" -- a student's persistent list of exams they're preparing for (see
    // Models/UserExamPreference.cs), used as the DEFAULT exam context across Question Bank / PYP /
    // Mock Tests / Practice Tests / Weak-Topics Quiz (see each of those controllers/services for how
    // they consume it). This never replaces those sections' own explicit exam filters -- callers
    // there still accept their normal exam parameter(s), which always win over My Exams when
    // present (filter precedence: explicit filter > My Exams > All Exams).
    //
    // Every endpoint here is per-student and requires login -- there is no admin "manage a student's
    // My Exams" surface (spec section 25: students manage their own preparation preferences, admin's
    // existing Exam master data is reused as-is, unchanged).
    [ApiController]
    [Route("api/user/exams")]
    [Authorize(Roles = "Student")]
    public class UserExamsController : ControllerBase
    {
        private readonly ScoramDbContext _db;

        public UserExamsController(ScoramDbContext db)
        {
            _db = db;
        }

        // GET /api/user/exams -- current selections. An empty list is the "not configured yet"
        // signal the web/Flutter clients check right after login to decide onboarding vs. Home.
        [HttpGet]
        public async Task<ActionResult<MyExamsResponseDto>> Get()
        {
            var userId = User.GetUserId();
            var prefs = await LoadOrderedAsync(userId);

            return Ok(new MyExamsResponseDto
            {
                Exams = prefs.Select(ToDto).ToList(),
                PrimaryExamId = prefs.FirstOrDefault(p => p.IsPrimary)?.ExamId
            });
        }

        // PUT /api/user/exams -- full replace. Used by:
        //   (a) first-time onboarding ("What are you preparing for?" -> Continue, spec section 4)
        //   (b) the My Exams management screen's "Save Changes" (spec section 13)
        // Minimum one exam (spec section 4); duplicates in the incoming list are ignored rather than
        // rejected, since a multi-select UI naturally can't produce them anyway.
        [HttpPut]
        public async Task<ActionResult<MyExamsResponseDto>> Set(SetMyExamsDto dto)
        {
            var examIds = (dto.ExamIds ?? new List<Guid>()).Distinct().ToList();
            if (examIds.Count == 0)
                return BadRequest(new { message = "Select at least one exam." });

            var validExamIds = await _db.Exams.Where(e => examIds.Contains(e.Id)).Select(e => e.Id).ToListAsync();
            var invalidIds = examIds.Except(validExamIds).ToList();
            if (invalidIds.Count > 0)
                return BadRequest(new { message = "One or more selected exams could not be found." });

            if (dto.PrimaryExamId.HasValue && !examIds.Contains(dto.PrimaryExamId.Value))
                return BadRequest(new { message = "Primary exam must be one of the selected exams." });

            var userId = User.GetUserId();
            var existing = await _db.UserExamPreferences.Where(p => p.UserId == userId).ToListAsync();

            // Keep the previous Primary Exam if it's still in the new selection, so re-saving the
            // same list (e.g. adding one more exam via the management screen) doesn't silently
            // reset which exam was primary.
            var previousPrimaryId = existing.FirstOrDefault(p => p.IsPrimary)?.ExamId;
            var primaryId = dto.PrimaryExamId
                ?? (previousPrimaryId.HasValue && examIds.Contains(previousPrimaryId.Value) ? previousPrimaryId : examIds[0]);

            _db.UserExamPreferences.RemoveRange(existing);

            var now = DateTime.UtcNow;
            foreach (var examId in examIds)
            {
                _db.UserExamPreferences.Add(new UserExamPreference
                {
                    UserId = userId,
                    ExamId = examId,
                    IsPrimary = examId == primaryId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            await _db.SaveChangesAsync();

            var saved = await LoadOrderedAsync(userId);
            return Ok(new MyExamsResponseDto
            {
                Exams = saved.Select(ToDto).ToList(),
                PrimaryExamId = saved.FirstOrDefault(p => p.IsPrimary)?.ExamId
            });
        }

        // POST /api/user/exams/{examId} -- add a single exam to My Exams (the management screen's
        // "+ Add Exam"). Idempotent: adding an exam that's already selected just returns the
        // unchanged current list rather than erroring (spec section 35, duplicate prevention).
        [HttpPost("{examId:guid}")]
        public async Task<ActionResult<MyExamsResponseDto>> Add(Guid examId)
        {
            var examExists = await _db.Exams.AnyAsync(e => e.Id == examId);
            if (!examExists) return NotFound(new { message = "Exam not found." });

            var userId = User.GetUserId();
            var alreadySelected = await _db.UserExamPreferences.AnyAsync(p => p.UserId == userId && p.ExamId == examId);
            if (!alreadySelected)
            {
                var hasAny = await _db.UserExamPreferences.AnyAsync(p => p.UserId == userId);
                var now = DateTime.UtcNow;
                _db.UserExamPreferences.Add(new UserExamPreference
                {
                    UserId = userId,
                    ExamId = examId,
                    IsPrimary = !hasAny, // the very first exam a student adds becomes Primary by default
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await _db.SaveChangesAsync();
            }

            var saved = await LoadOrderedAsync(userId);
            return Ok(new MyExamsResponseDto
            {
                Exams = saved.Select(ToDto).ToList(),
                PrimaryExamId = saved.FirstOrDefault(p => p.IsPrimary)?.ExamId
            });
        }

        // DELETE /api/user/exams/{examId} -- remove one exam (spec section 13). Refuses to remove
        // the student's only remaining exam rather than leaving My Exams empty via a side door --
        // Set(...) above (or a fresh onboarding pass) is the intentional way to clear everything.
        [HttpDelete("{examId:guid}")]
        public async Task<IActionResult> Remove(Guid examId)
        {
            var userId = User.GetUserId();
            var all = await _db.UserExamPreferences.Where(p => p.UserId == userId).ToListAsync();
            var target = all.FirstOrDefault(p => p.ExamId == examId);
            if (target == null) return NotFound(new { message = "That exam isn't in your My Exams list." });

            if (all.Count == 1)
                return BadRequest(new { message = "Select another exam before removing your last one." });

            _db.UserExamPreferences.Remove(target);

            // Removing the Primary Exam auto-promotes the next-oldest remaining selection, so the
            // student never ends up with a valid My Exams list but no Primary Exam (spec section 13
            // offers either asking the student to re-pick or auto-assigning -- auto-assigning avoids
            // a blocking extra step and never produces an invalid state).
            if (target.IsPrimary)
            {
                var next = all.Where(p => p.ExamId != examId).OrderBy(p => p.CreatedAt).First();
                next.IsPrimary = true;
                next.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // PATCH /api/user/exams/{examId}/primary -- set Primary Exam (spec section 13, "Set as
        // Primary"). The exam must already be one of the student's selected exams.
        [HttpPatch("{examId:guid}/primary")]
        public async Task<ActionResult<MyExamsResponseDto>> SetPrimary(Guid examId)
        {
            var userId = User.GetUserId();
            var all = await _db.UserExamPreferences.Where(p => p.UserId == userId).ToListAsync();
            var target = all.FirstOrDefault(p => p.ExamId == examId);
            if (target == null) return NotFound(new { message = "That exam isn't in your My Exams list." });

            var now = DateTime.UtcNow;
            foreach (var p in all)
            {
                var shouldBePrimary = p.ExamId == examId;
                if (p.IsPrimary != shouldBePrimary)
                {
                    p.IsPrimary = shouldBePrimary;
                    p.UpdatedAt = now;
                }
            }

            await _db.SaveChangesAsync();

            var saved = await LoadOrderedAsync(userId);
            return Ok(new MyExamsResponseDto
            {
                Exams = saved.Select(ToDto).ToList(),
                PrimaryExamId = saved.FirstOrDefault(p => p.IsPrimary)?.ExamId
            });
        }

        // ---------- helpers ----------

        private Task<List<UserExamPreference>> LoadOrderedAsync(Guid userId) =>
            _db.UserExamPreferences
                .Include(p => p.Exam)
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.IsPrimary)
                .ThenBy(p => p.CreatedAt)
                .ToListAsync();

        private static UserExamPreferenceDto ToDto(UserExamPreference p) => new UserExamPreferenceDto
        {
            ExamId = p.ExamId,
            ExamName = p.Exam?.Name ?? string.Empty,
            ExamLogoUrl = p.Exam?.LogoUrl,
            IsPrimary = p.IsPrimary,
            CreatedAt = p.CreatedAt
        };
    }
}
