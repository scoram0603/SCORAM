using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Extensions;
using ScoramAPI.Models;

namespace ScoramAPI.Controllers
{
    // "MY EXAMS" -- a student's persistent list of exams (see Models/UserExamPreference.cs). This is
    // what used to be called "Preparing For" in the UI; the table/endpoints were already named for
    // My Exams, so existing selections carry over untouched -- no data migration, nobody has to
    // choose again.
    //
    // My Exams is a STRICT CONTENT SCOPE, not a removable default filter: every student-facing,
    // exam-specific list/search endpoint (PYP, Question Bank/PYQ, Mock Tests, Practice Tests, Groups,
    // instant search, ...) resolves it through IMyExamScopeService and applies it in the database
    // query. A student who wants another exam's content adds that exam here -- there is no per-screen
    // override.
    //
    // An EMPTY list is a valid, supported state ("skipped" / "removed everything"): it means the
    // student sees no exam-specific content and is prompted to choose, never "all exams".
    //
    // Every endpoint is per-student, requires login, and takes the user id from the authenticated
    // principal only -- there is no way to read or change another student's My Exams.
    [ApiController]
    [Route("api/user/exams")]
    [Authorize(Roles = "Student")]
    public class UserExamsController : ControllerBase
    {
        // Sanity ceiling on a single save -- far above any real selection, just stops an absurd payload.
        private const int MaxSelectedExams = 100;

        private readonly ScoramDbContext _db;

        public UserExamsController(ScoramDbContext db)
        {
            _db = db;
        }

        // GET /api/user/exams -- current selections. An empty list is the "not configured yet"
        // signal the web/Flutter clients use to decide between onboarding / the choose-exams prompt
        // and the normal experience.
        [HttpGet]
        public async Task<ActionResult<MyExamsResponseDto>> Get()
        {
            var userId = User.GetUserId();
            return Ok(ToResponse(await LoadOrderedAsync(userId)));
        }

        // PUT /api/user/exams -- full replace (onboarding "Continue" and Profile -> "Update My
        // Exams"). An empty list CLEARS My Exams -- that is how "remove all" works and it is allowed.
        // Duplicate ids are collapsed; unknown / blocked exams are rejected as a whole (nothing is
        // half-saved).
        [HttpPut]
        public async Task<ActionResult<MyExamsResponseDto>> Set(SetMyExamsDto dto)
        {
            var examIds = (dto.ExamIds ?? new List<Guid>()).Where(id => id != Guid.Empty).Distinct().ToList();
            if (examIds.Count > MaxSelectedExams)
                return BadRequest(new { message = $"You can select at most {MaxSelectedExams} exams." });

            if (examIds.Count > 0)
            {
                // ONE query for the whole selection, and "available" means exactly what the public
                // exam list shows: not blocked, and not under a blocked Organization.
                var validExamIds = await AvailableExams()
                    .Where(e => examIds.Contains(e.Id))
                    .Select(e => e.Id)
                    .ToListAsync();
                if (examIds.Except(validExamIds).Any())
                    return BadRequest(new { message = "One or more selected exams could not be found." });
            }

            if (dto.PrimaryExamId.HasValue && !examIds.Contains(dto.PrimaryExamId.Value))
                return BadRequest(new { message = "Primary exam must be one of the selected exams." });

            var userId = User.GetUserId();
            var existing = await _db.UserExamPreferences.Where(p => p.UserId == userId).ToListAsync();
            var now = DateTime.UtcNow;

            // Diff instead of delete-all/insert-all: untouched exams keep their CreatedAt, and the
            // (UserId, ExamId) unique index is never asked to see a delete+insert of the same key.
            var toRemove = existing.Where(p => !examIds.Contains(p.ExamId)).ToList();
            _db.UserExamPreferences.RemoveRange(toRemove);

            var kept = existing.Where(p => examIds.Contains(p.ExamId)).ToList();
            var previousPrimaryId = kept.FirstOrDefault(p => p.IsPrimary)?.ExamId;
            Guid? primaryId = null;
            if (examIds.Count > 0)
            {
                // Keep the previous Primary if it survives, so re-saving a list (e.g. adding one more
                // exam) doesn't silently reset which exam is primary.
                primaryId = dto.PrimaryExamId ?? previousPrimaryId ?? examIds[0];
            }

            foreach (var pref in kept)
            {
                var shouldBePrimary = pref.ExamId == primaryId;
                if (pref.IsPrimary != shouldBePrimary)
                {
                    pref.IsPrimary = shouldBePrimary;
                    pref.UpdatedAt = now;
                }
            }

            var keptIds = kept.Select(p => p.ExamId).ToHashSet();
            foreach (var examId in examIds.Where(id => !keptIds.Contains(id)))
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
            return Ok(ToResponse(await LoadOrderedAsync(userId)));
        }

        // POST /api/user/exams/{examId} -- add one exam. Idempotent: adding an already-selected exam
        // just returns the unchanged list.
        [HttpPost("{examId:guid}")]
        public async Task<ActionResult<MyExamsResponseDto>> Add(Guid examId)
        {
            var examExists = await AvailableExams().AnyAsync(e => e.Id == examId);
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

            return Ok(ToResponse(await LoadOrderedAsync(userId)));
        }

        // DELETE /api/user/exams/{examId} -- remove one exam. Removing the LAST one is allowed (My
        // Exams simply becomes empty and the student is prompted to choose again) -- the student is
        // never trapped in a selection.
        [HttpDelete("{examId:guid}")]
        public async Task<IActionResult> Remove(Guid examId)
        {
            var userId = User.GetUserId();
            var all = await _db.UserExamPreferences.Where(p => p.UserId == userId).ToListAsync();
            var target = all.FirstOrDefault(p => p.ExamId == examId);
            if (target == null) return NotFound(new { message = "That exam isn't in your My Exams list." });

            _db.UserExamPreferences.Remove(target);

            // Removing the Primary exam auto-promotes the oldest remaining selection, so a non-empty
            // My Exams always has exactly one Primary.
            if (target.IsPrimary)
            {
                var next = all.Where(p => p.ExamId != examId).OrderBy(p => p.CreatedAt).FirstOrDefault();
                if (next != null)
                {
                    next.IsPrimary = true;
                    next.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // PATCH /api/user/exams/{examId}/primary -- set Primary Exam. Must already be selected.
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
            return Ok(ToResponse(await LoadOrderedAsync(userId)));
        }

        // ---------- helpers ----------

        // Same availability rule as GET /api/exams (public list): not blocked, Organization not blocked.
        private IQueryable<Exam> AvailableExams() =>
            _db.Exams.Where(e => !e.IsBlocked && (e.Organization == null || !e.Organization.IsBlocked));

        private Task<List<UserExamPreference>> LoadOrderedAsync(Guid userId) =>
            _db.UserExamPreferences
                .Include(p => p.Exam)
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.IsPrimary)
                .ThenBy(p => p.CreatedAt)
                .ToListAsync();

        private static MyExamsResponseDto ToResponse(List<UserExamPreference> prefs) => new MyExamsResponseDto
        {
            Exams = prefs.Select(ToDto).ToList(),
            PrimaryExamId = prefs.FirstOrDefault(p => p.IsPrimary)?.ExamId
        };

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
