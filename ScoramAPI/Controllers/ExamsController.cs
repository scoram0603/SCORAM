using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Step 1 of the admin PYQ upload wizard: "choose exam you've already created (SSC CGL) or
    // + New Exam (enter name, choose logo)". This list also doubles as a public "browse by exam"
    // list for students, which is why GET is anonymous but creating an exam is Admin-only.
    [ApiController]
    [Route("api/exams")]
    public class ExamsController : ControllerBase
    {
        private static readonly string[] AllowedLogoExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".svg" };
        private const long MaxLogoSizeBytes = 2 * 1024 * 1024; // 2 MB

        private readonly ScoramDbContext _db;
        private readonly IFileStorageService _fileStorage;

        public ExamsController(ScoramDbContext db, IFileStorageService fileStorage)
        {
            _db = db;
            _fileStorage = fileStorage;
        }

        // GET /api/exams -- the picker list (also usable as a public "browse by exam" list). Excludes
        // blocked exams -- see IsBlocked -- for non-admin callers; GET /api/admin/exams below is the
        // unfiltered version admins manage from.
        [HttpGet]
        public async Task<ActionResult<List<ExamResponseDto>>> List([FromQuery] Guid? organizationId)
        {
            // ORGANIZATION HIERARCHY -- a blocked Organization hides every exam under it from this
            // public list too, without touching each exam's own IsBlocked flag (see
            // Organization.IsBlocked's own comment). organizationId powers the two-step "pick an
            // Organization, then pick from its exams" picker everywhere one exists.
            var query = _db.Exams
                .Where(e => !e.IsBlocked && (e.Organization == null || !e.Organization.IsBlocked));
            if (organizationId.HasValue) query = query.Where(e => e.OrganizationId == organizationId.Value);

            var exams = await query
                .OrderBy(e => e.Name)
                .Select(e => new ExamResponseDto
                {
                    Id = e.Id,
                    Name = e.Name,
                    LogoUrl = e.LogoUrl,
                    IsBlocked = e.IsBlocked,
                    QuestionCount = e.Questions.Count + e.Papers.SelectMany(p => p.Questions).Count(),
                    CreatedAt = e.CreatedAt,
                    OrganizationId = e.OrganizationId,
                    OrganizationName = e.Organization != null ? e.Organization.Name : null
                })
                .ToListAsync();

            return Ok(exams);
        }

        // GET /api/admin/exams  (Admin only) -- same shape as above but includes blocked exams (and
        // exams under a blocked Organization) too, for the admin "Manage Exams" screen.
        [HttpGet("/api/admin/exams")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<List<ExamResponseDto>>> AdminList([FromQuery] Guid? organizationId)
        {
            var query = _db.Exams.AsQueryable();
            if (organizationId.HasValue) query = query.Where(e => e.OrganizationId == organizationId.Value);

            var exams = await query
                .OrderBy(e => e.Name)
                .Select(e => new ExamResponseDto
                {
                    Id = e.Id,
                    Name = e.Name,
                    LogoUrl = e.LogoUrl,
                    IsBlocked = e.IsBlocked,
                    QuestionCount = e.Questions.Count + e.Papers.SelectMany(p => p.Questions).Count(),
                    CreatedAt = e.CreatedAt,
                    OrganizationId = e.OrganizationId,
                    OrganizationName = e.Organization != null ? e.Organization.Name : null
                })
                .ToListAsync();

            return Ok(exams);
        }

        // POST /api/admin/exams  (Admin only) -- "+ New Exam": Enter Exam Name, Choose Exam Logo
        [HttpPost("/api/admin/exams")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        [RequestSizeLimit(MaxLogoSizeBytes + 1024)]
        public async Task<ActionResult<ExamResponseDto>> Create([FromForm] ExamCreateDto dto)
        {
            var name = dto.Name.Trim();
            if (await _db.Exams.AnyAsync(e => e.Name.ToLower() == name.ToLower()))
                return Conflict(new { message = $"An exam named \"{name}\" already exists -- pick it from the list instead of creating a duplicate." });

            // ORGANIZATION HIERARCHY -- optional; validated up front so a typo'd/deleted
            // OrganizationId fails loudly here rather than silently creating an orphaned FK.
            Organization? organization = null;
            if (dto.OrganizationId.HasValue)
            {
                organization = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == dto.OrganizationId.Value);
                if (organization == null) return BadRequest(new { message = "Selected organization could not be found." });
            }

            string? logoUrl = null;
            if (dto.Logo != null)
            {
                var validationError = ValidateLogo(dto.Logo);
                if (validationError != null) return BadRequest(new { message = validationError });

                logoUrl = await _fileStorage.SaveImageAsync(dto.Logo, "exam-logos");
            }

            var exam = new Exam
            {
                Name = name,
                LogoUrl = logoUrl,
                OrganizationId = organization?.Id,
                CreatedByAdminId = User.GetAdminId(),
                CreatedAt = DateTime.UtcNow
            };

            _db.Exams.Add(exam);

            // Every exam gets a chat room automatically -- joining it is optional (see
            // ChatRoomsController), this just makes sure the room exists to be joined.
            // GROUP CHAT FIX -- IsFeatured = false: a student finds this room by searching for the
            // exam by name (ChatController.ListRooms), rather than every exam ever created flooding
            // everyone's default room list. See ChatRoom.IsFeatured for the full reasoning.
            _db.ChatRooms.Add(new ChatRoom
            {
                ExamId = exam.Id,
                Name = exam.Name,
                Description = $"Discussion room for {exam.Name} aspirants",
                IsFeatured = false,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            return Ok(new ExamResponseDto
            {
                Id = exam.Id,
                Name = exam.Name,
                LogoUrl = exam.LogoUrl,
                IsBlocked = false,
                QuestionCount = 0,
                CreatedAt = exam.CreatedAt,
                OrganizationId = organization?.Id,
                OrganizationName = organization?.Name
            });
        }

        // PATCH /api/admin/exams/{id}  (Admin only) -- rename and/or replace the logo. Renaming also
        // keeps the linked chat room's denormalized Name in sync (see ChatRoom.Name) since students
        // find that room by searching for the exam name (ChatController.ListRooms).
        [HttpPatch("/api/admin/exams/{id:guid}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        [RequestSizeLimit(MaxLogoSizeBytes + 1024)]
        public async Task<ActionResult<ExamResponseDto>> Update(Guid id, [FromForm] ExamUpdateDto dto)
        {
            var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id);
            if (exam == null) return NotFound();

            if (dto.Name != null)
            {
                var name = dto.Name.Trim();
                if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Name can't be empty." });
                if (await _db.Exams.AnyAsync(e => e.Id != id && e.Name.ToLower() == name.ToLower()))
                    return Conflict(new { message = $"An exam named \"{name}\" already exists." });

                exam.Name = name;

                var room = await _db.ChatRooms.FirstOrDefaultAsync(r => r.ExamId == id);
                if (room != null) room.Name = name;
            }

            if (dto.Logo != null)
            {
                var validationError = ValidateLogo(dto.Logo);
                if (validationError != null) return BadRequest(new { message = validationError });

                var oldLogoUrl = exam.LogoUrl;
                exam.LogoUrl = await _fileStorage.SaveImageAsync(dto.Logo, "exam-logos");
                await _fileStorage.DeleteImageAsync(oldLogoUrl);
            }

            // ORGANIZATION HIERARCHY -- see ExamUpdateDto.ClearOrganization's own comment on why
            // clearing needs its own explicit flag rather than just sending a null OrganizationId.
            if (dto.ClearOrganization)
            {
                exam.OrganizationId = null;
            }
            else if (dto.OrganizationId.HasValue)
            {
                var organizationExists = await _db.Organizations.AnyAsync(o => o.Id == dto.OrganizationId.Value);
                if (!organizationExists) return BadRequest(new { message = "Selected organization could not be found." });
                exam.OrganizationId = dto.OrganizationId.Value;
            }

            await _db.SaveChangesAsync();

            var updatedOrgName = exam.OrganizationId.HasValue
                ? await _db.Organizations.Where(o => o.Id == exam.OrganizationId).Select(o => o.Name).FirstOrDefaultAsync()
                : null;

            return Ok(new ExamResponseDto
            {
                Id = exam.Id,
                Name = exam.Name,
                LogoUrl = exam.LogoUrl,
                IsBlocked = exam.IsBlocked,
                QuestionCount = await _db.Questions.CountAsync(q => q.ExamId == id) + await _db.Papers.Where(p => p.ExamId == id).SelectMany(p => p.Questions).CountAsync(),
                CreatedAt = exam.CreatedAt,
                OrganizationId = exam.OrganizationId,
                OrganizationName = updatedOrgName
            });
        }

        // PATCH /api/admin/exams/{id}/block  (Admin only) -- hide/unhide, see Exam.IsBlocked. Blocking
        // also disables (but doesn't delete) the linked chat room, matching "no new engagement" intent.
        [HttpPatch("/api/admin/exams/{id:guid}/block")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<ExamResponseDto>> SetBlocked(Guid id, [FromBody] ExamBlockDto dto)
        {
            var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id);
            if (exam == null) return NotFound();

            exam.IsBlocked = dto.IsBlocked;

            var room = await _db.ChatRooms.FirstOrDefaultAsync(r => r.ExamId == id);
            if (room != null) room.IsChatDisabled = dto.IsBlocked;

            await _db.SaveChangesAsync();

            var orgName = exam.OrganizationId.HasValue
                ? await _db.Organizations.Where(o => o.Id == exam.OrganizationId).Select(o => o.Name).FirstOrDefaultAsync()
                : null;

            return Ok(new ExamResponseDto
            {
                Id = exam.Id,
                Name = exam.Name,
                LogoUrl = exam.LogoUrl,
                IsBlocked = exam.IsBlocked,
                QuestionCount = await _db.Questions.CountAsync(q => q.ExamId == id) + await _db.Papers.Where(p => p.ExamId == id).SelectMany(p => p.Questions).CountAsync(),
                CreatedAt = exam.CreatedAt,
                OrganizationId = exam.OrganizationId,
                OrganizationName = orgName
            });
        }

        // DELETE /api/admin/exams/{id}  (SuperAdmin only -- this is destructive and, unlike Block,
        // can't be undone) -- only succeeds if the exam is genuinely empty: no PYQ questions, no
        // Question Bank mappings, no Practice Test templates, no Mock Tests, and its chat room (if
        // any) has no messages/members. Anything else and the answer is Block, not Delete -- returns
        // 409 saying so rather than silently orphaning or cascading through a student's real activity.
        [HttpDelete("/api/admin/exams/{id:guid}")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id);
            if (exam == null) return NotFound();

            var hasContent = await _db.Questions.AnyAsync(q => q.ExamId == id)
                || await _db.Papers.AnyAsync(p => p.ExamId == id)
                || await _db.QuestionBankExamMappings.AnyAsync(m => m.ExamId == id)
                || await _db.PracticeTestTemplates.AnyAsync(t => t.ExamId == id)
                || await _db.MockTests.AnyAsync(m => m.ExamName == exam.Name);

            var room = await _db.ChatRooms.FirstOrDefaultAsync(r => r.ExamId == id);
            if (room != null)
            {
                hasContent = hasContent
                    || await _db.ChatMessages.AnyAsync(m => m.ChatRoomId == room.Id)
                    || await _db.ChatRoomMemberships.AnyAsync(m => m.ChatRoomId == room.Id);
            }

            if (hasContent)
                return Conflict(new { message = "This exam has questions, tests, or chat activity attached -- Block it instead of deleting." });

            if (room != null) _db.ChatRooms.Remove(room);
            _db.Exams.Remove(exam);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        private string? ValidateLogo(IFormFile logo)
        {
            if (logo.Length == 0) return "The uploaded logo file is empty.";
            if (logo.Length > MaxLogoSizeBytes) return "Logo must be 2 MB or smaller.";

            var ext = Path.GetExtension(logo.FileName).ToLowerInvariant();
            if (!AllowedLogoExtensions.Contains(ext))
                return $"Logo must be one of: {string.Join(", ", AllowedLogoExtensions)}.";

            return null;
        }
    }
}
