using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Read-only trail for admins with the Audit permission (Super Admins always have it implicitly --
    // see AdminPermissionService) to review who did what across Papers/Questions/Admin management.
    [ApiController]
    [Route("api/admin/audit-logs")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AuditLogsController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;

        public AuditLogsController(ScoramDbContext db, IAdminPermissionService permissions)
        {
            _db = db;
            _permissions = permissions;
        }

        // GET /api/admin/audit-logs?page=1&pageSize=25&adminId=&action=
        [HttpGet]
        public async Task<ActionResult<PagedResult<AuditLogResponseDto>>> List(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            [FromQuery] Guid? adminId = null,
            [FromQuery] string? action = null)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.Audit))
                return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.AuditLogs.Include(a => a.Admin).AsQueryable();

            if (adminId.HasValue) query = query.Where(a => a.AdminId == adminId.Value);
            if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action.Contains(action));

            query = query.OrderByDescending(a => a.CreatedAt);

            var totalCount = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new AuditLogResponseDto
                {
                    Id = a.Id,
                    AdminId = a.AdminId,
                    AdminName = a.Admin != null ? a.Admin.FullName : "Unknown",
                    Action = a.Action,
                    TargetType = a.TargetType,
                    TargetId = a.TargetId,
                    Detail = a.Detail,
                    CreatedAt = a.CreatedAt
                })
                .ToListAsync();

            return Ok(new PagedResult<AuditLogResponseDto> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
        }
    }
}
