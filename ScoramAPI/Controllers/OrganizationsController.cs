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
    // ORGANIZATION HIERARCHY -- Step 0 of "pick your exams" everywhere an exam picker exists (My
    // Exams, PYP/Question Bank/Mock Tests/Practice Tests filters, admin's own exam list): pick an
    // Organization (SSC, RRB, UPSC, ...) first, then pick from its exams -- see Models/Organization.cs
    // and Exam.OrganizationId. Mirrors ExamsController's own shape line for line (GET is public/
    // unblocked-only, admin management is a separate unfiltered surface, Delete only succeeds if
    // genuinely empty) since it's managed the same way, by the same kind of admin, for the same
    // reasons.
    [ApiController]
    [Route("api/organizations")]
    public class OrganizationsController : ControllerBase
    {
        private static readonly string[] AllowedLogoExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".svg" };
        private const long MaxLogoSizeBytes = 2 * 1024 * 1024; // 2 MB

        private readonly ScoramDbContext _db;
        private readonly IFileStorageService _fileStorage;

        public OrganizationsController(ScoramDbContext db, IFileStorageService fileStorage)
        {
            _db = db;
            _fileStorage = fileStorage;
        }

        // GET /api/organizations -- the public picker list. Excludes blocked organizations, same as
        // ExamsController.List does for exams.
        [HttpGet]
        public async Task<ActionResult<List<OrganizationResponseDto>>> List()
        {
            var organizations = await _db.Organizations
                .Where(o => !o.IsBlocked)
                .OrderBy(o => o.Name)
                .Select(o => new OrganizationResponseDto
                {
                    Id = o.Id,
                    Name = o.Name,
                    LogoUrl = o.LogoUrl,
                    IsBlocked = o.IsBlocked,
                    ExamCount = o.Exams.Count(e => !e.IsBlocked),
                    CreatedAt = o.CreatedAt
                })
                .ToListAsync();

            return Ok(organizations);
        }

        // GET /api/admin/organizations  (Admin only) -- same shape as above but includes blocked
        // organizations too, and counts every exam under each (not just unblocked ones), for the
        // admin "Manage Organizations" screen.
        [HttpGet("/api/admin/organizations")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<List<OrganizationResponseDto>>> AdminList()
        {
            var organizations = await _db.Organizations
                .OrderBy(o => o.Name)
                .Select(o => new OrganizationResponseDto
                {
                    Id = o.Id,
                    Name = o.Name,
                    LogoUrl = o.LogoUrl,
                    IsBlocked = o.IsBlocked,
                    ExamCount = o.Exams.Count,
                    CreatedAt = o.CreatedAt
                })
                .ToListAsync();

            return Ok(organizations);
        }

        // POST /api/admin/organizations  (Admin only)
        [HttpPost("/api/admin/organizations")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        [RequestSizeLimit(MaxLogoSizeBytes + 1024)]
        public async Task<ActionResult<OrganizationResponseDto>> Create([FromForm] OrganizationCreateDto dto)
        {
            var name = dto.Name.Trim();
            if (await _db.Organizations.AnyAsync(o => o.Name.ToLower() == name.ToLower()))
                return Conflict(new { message = $"An organization named \"{name}\" already exists -- pick it from the list instead of creating a duplicate." });

            string? logoUrl = null;
            if (dto.Logo != null)
            {
                var validationError = ValidateLogo(dto.Logo);
                if (validationError != null) return BadRequest(new { message = validationError });

                logoUrl = await _fileStorage.SaveImageAsync(dto.Logo, "organization-logos");
            }

            var organization = new Organization
            {
                Name = name,
                LogoUrl = logoUrl,
                CreatedByAdminId = User.GetAdminId(),
                CreatedAt = DateTime.UtcNow
            };

            _db.Organizations.Add(organization);
            await _db.SaveChangesAsync();

            return Ok(new OrganizationResponseDto
            {
                Id = organization.Id,
                Name = organization.Name,
                LogoUrl = organization.LogoUrl,
                IsBlocked = false,
                ExamCount = 0,
                CreatedAt = organization.CreatedAt
            });
        }

        // PATCH /api/admin/organizations/{id}  (Admin only) -- rename and/or replace the logo.
        [HttpPatch("/api/admin/organizations/{id:guid}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        [RequestSizeLimit(MaxLogoSizeBytes + 1024)]
        public async Task<ActionResult<OrganizationResponseDto>> Update(Guid id, [FromForm] OrganizationUpdateDto dto)
        {
            var organization = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == id);
            if (organization == null) return NotFound();

            if (dto.Name != null)
            {
                var name = dto.Name.Trim();
                if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Name can't be empty." });
                if (await _db.Organizations.AnyAsync(o => o.Id != id && o.Name.ToLower() == name.ToLower()))
                    return Conflict(new { message = $"An organization named \"{name}\" already exists." });

                organization.Name = name;
            }

            if (dto.Logo != null)
            {
                var validationError = ValidateLogo(dto.Logo);
                if (validationError != null) return BadRequest(new { message = validationError });

                var oldLogoUrl = organization.LogoUrl;
                organization.LogoUrl = await _fileStorage.SaveImageAsync(dto.Logo, "organization-logos");
                await _fileStorage.DeleteImageAsync(oldLogoUrl);
            }

            await _db.SaveChangesAsync();

            return Ok(new OrganizationResponseDto
            {
                Id = organization.Id,
                Name = organization.Name,
                LogoUrl = organization.LogoUrl,
                IsBlocked = organization.IsBlocked,
                ExamCount = await _db.Exams.CountAsync(e => e.OrganizationId == id),
                CreatedAt = organization.CreatedAt
            });
        }

        // PATCH /api/admin/organizations/{id}/block  (Admin only) -- see Organization.IsBlocked's own
        // comment: this doesn't touch each exam's own IsBlocked flag, only hides them from the public
        // List() above while the organization itself is blocked.
        [HttpPatch("/api/admin/organizations/{id:guid}/block")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<OrganizationResponseDto>> SetBlocked(Guid id, [FromBody] OrganizationBlockDto dto)
        {
            var organization = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == id);
            if (organization == null) return NotFound();

            organization.IsBlocked = dto.IsBlocked;
            await _db.SaveChangesAsync();

            return Ok(new OrganizationResponseDto
            {
                Id = organization.Id,
                Name = organization.Name,
                LogoUrl = organization.LogoUrl,
                IsBlocked = organization.IsBlocked,
                ExamCount = await _db.Exams.CountAsync(e => e.OrganizationId == id),
                CreatedAt = organization.CreatedAt
            });
        }

        // DELETE /api/admin/organizations/{id}  (SuperAdmin only) -- only succeeds if no exam is
        // currently assigned to it, same "Block, not Delete" pattern ExamsController.Delete uses for
        // an exam with real content attached. An admin who wants to delete an organization that still
        // has exams needs to reassign or delete those exams first.
        [HttpDelete("/api/admin/organizations/{id:guid}")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var organization = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == id);
            if (organization == null) return NotFound();

            var hasExams = await _db.Exams.AnyAsync(e => e.OrganizationId == id);
            if (hasExams)
                return Conflict(new { message = "This organization still has exams assigned to it -- reassign or delete those first, or Block it instead." });

            _db.Organizations.Remove(organization);
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
