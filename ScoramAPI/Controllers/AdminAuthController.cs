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
    // SRS Section 3 (User Roles): Super Admin has full system access and creates/manages other
    // admins; Admin accounts are never self-registered. A single Super Admin is seeded via
    // ScoramDbContext (see the HasData block) so there's a way in on a brand-new database --
    // see README for the seeded login credentials.
    [ApiController]
    [Route("api/admin")]
    public class AdminAuthController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly ITokenService _tokenService;
        private readonly IAuditLogService _audit;

        public AdminAuthController(ScoramDbContext db, ITokenService tokenService, IAuditLogService audit)
        {
            _db = db;
            _tokenService = tokenService;
            _audit = audit;
        }

        [HttpPost("auth/login")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<AdminAuthResponseDto>> Login(AdminLoginDto dto)
        {
            var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Email == dto.Email);

            if (admin == null || !BCrypt.Net.BCrypt.Verify(dto.Password, admin.PasswordHash))
                return Unauthorized(new { message = "Invalid email or password." });

            if (!admin.IsActive)
                return Unauthorized(new { message = "This admin account has been deactivated." });

            var (token, expiresAt) = _tokenService.GenerateAdminToken(admin);
            await _audit.LogAsync(admin.Id, "Admin.Login", "Admin", admin.Id);
            return Ok(new AdminAuthResponseDto
            {
                Token = token,
                ExpiresAt = expiresAt,
                AdminId = admin.Id,
                FullName = admin.FullName,
                Email = admin.Email,
                Role = admin.Role.ToString()
            });
        }

        // GET /api/admin/admins  (Super Admin only) -- "Monitor admin activities" / manage admins
        [HttpGet("admins")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<List<AdminResponseDto>>> ListAdmins()
        {
            var admins = await _db.Admins
                .OrderBy(a => a.FullName)
                .Select(a => new AdminResponseDto
                {
                    Id = a.Id,
                    FullName = a.FullName,
                    Email = a.Email,
                    Role = a.Role.ToString(),
                    IsActive = a.IsActive,
                    CreatedAt = a.CreatedAt,
                    Permissions = a.PermissionGrants.Select(g => g.Permission.ToString()).ToList()
                })
                .ToListAsync();

            return Ok(admins);
        }

        // POST /api/admin/admins  (Super Admin only) -- "Create and manage admins"
        [HttpPost("admins")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<AdminResponseDto>> CreateAdmin(AdminCreateDto dto)
        {
            if (await _db.Admins.AnyAsync(a => a.Email == dto.Email))
                return Conflict(new { message = "An admin account with this email already exists." });

            var admin = new Admin
            {
                FullName = dto.FullName,
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Role = dto.Role,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Admins.Add(admin);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "Admin.Create", "Admin", admin.Id, $"{admin.FullName} ({admin.Email}), role {admin.Role}");

            return Ok(new AdminResponseDto
            {
                Id = admin.Id,
                FullName = admin.FullName,
                Email = admin.Email,
                Role = admin.Role.ToString(),
                IsActive = admin.IsActive,
                CreatedAt = admin.CreatedAt
            });
        }

        // PATCH /api/admin/admins/{id}/status  (Super Admin only) -- activate/deactivate an admin,
        // e.g. to revoke access without deleting their history of uploaded questions/tests.
        [HttpPatch("admins/{id:guid}/status")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> SetStatus(Guid id, AdminStatusUpdateDto dto)
        {
            var admin = await _db.Admins.FindAsync(id);
            if (admin == null) return NotFound(new { message = "Admin not found." });

            if (admin.Id == User.GetAdminId())
                return BadRequest(new { message = "You can't change your own account's active status." });

            admin.IsActive = dto.IsActive;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), dto.IsActive ? "Admin.Activate" : "Admin.Deactivate", "Admin", admin.Id);

            return Ok(new { admin.Id, admin.IsActive });
        }

        // GET /api/admin/me/permissions -- any authenticated admin, for their own account. Used by the
        // frontend to decide what to show (e.g. hide the Review Queue nav item if you can't Publish) --
        // the actual enforcement always happens server-side regardless of what the UI shows.
        [HttpGet("me/permissions")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<AdminPermissionsResponseDto>> GetMyPermissions()
        {
            var adminId = User.GetAdminId();

            if (User.IsInRole("SuperAdmin"))
            {
                // Implicit -- a Super Admin has every permission regardless of grants (see
                // AdminPermissionService), so report the full set rather than whatever happens
                // to be in the grants table for them.
                return Ok(new AdminPermissionsResponseDto
                {
                    AdminId = adminId,
                    Permissions = Enum.GetValues<AdminPermission>().Select(p => p.ToString()).ToList()
                });
            }

            var permissions = await _db.AdminPermissionGrants
                .Where(g => g.AdminId == adminId)
                .Select(g => g.Permission.ToString())
                .ToListAsync();

            return Ok(new AdminPermissionsResponseDto { AdminId = adminId, Permissions = permissions });
        }

        // GET /api/admin/admins/{id}/permissions  (Super Admin only)
        [HttpGet("admins/{id:guid}/permissions")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<AdminPermissionsResponseDto>> GetPermissions(Guid id)
        {
            var adminExists = await _db.Admins.AnyAsync(a => a.Id == id);
            if (!adminExists) return NotFound(new { message = "Admin not found." });

            var permissions = await _db.AdminPermissionGrants
                .Where(g => g.AdminId == id)
                .Select(g => g.Permission.ToString())
                .ToListAsync();

            return Ok(new AdminPermissionsResponseDto { AdminId = id, Permissions = permissions });
        }

        // PUT /api/admin/admins/{id}/permissions  (Super Admin only) -- replace-all: send the complete
        // set of permissions this admin should have (see AdminPermissionsUpdateDto).
        [HttpPut("admins/{id:guid}/permissions")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<AdminPermissionsResponseDto>> SetPermissions(Guid id, AdminPermissionsUpdateDto dto)
        {
            var admin = await _db.Admins.FindAsync(id);
            if (admin == null) return NotFound(new { message = "Admin not found." });

            var existing = await _db.AdminPermissionGrants.Where(g => g.AdminId == id).ToListAsync();
            _db.AdminPermissionGrants.RemoveRange(existing);

            var distinctPermissions = dto.Permissions.Distinct();
            foreach (var permission in distinctPermissions)
            {
                _db.AdminPermissionGrants.Add(new AdminPermissionGrant { AdminId = id, Permission = permission });
            }

            await _db.SaveChangesAsync();
            var permissionList = distinctPermissions.Select(p => p.ToString()).ToList();
            await _audit.LogAsync(User.GetAdminId(), "Admin.SetPermissions", "Admin", id, string.Join(", ", permissionList));

            return Ok(new AdminPermissionsResponseDto
            {
                AdminId = id,
                Permissions = permissionList
            });
        }
    }
}
