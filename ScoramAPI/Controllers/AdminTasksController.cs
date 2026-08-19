using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Extensions;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Controllers
{
    // SRS Section 12 (Admin Task Management): Super Admin assigns tasks to admins;
    // admins track and complete their own assigned tasks.
    [ApiController]
    [Route("api/admin/tasks")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminTasksController : ControllerBase
    {
        private readonly ScoramDbContext _db;

        public AdminTasksController(ScoramDbContext db)
        {
            _db = db;
        }

        // POST /api/admin/tasks  (Super Admin only) -- "Assign task"
        [HttpPost]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<AdminTaskResponseDto>> Create(AdminTaskCreateDto dto)
        {
            var assignee = await _db.Admins.FindAsync(dto.AssignedToAdminId);
            if (assignee == null)
                return BadRequest(new { message = "The admin this task is being assigned to doesn't exist." });

            var task = new AdminTask
            {
                Title = dto.Title,
                Description = dto.Description,
                AssignedToAdminId = dto.AssignedToAdminId,
                AssignedByAdminId = User.GetAdminId(),
                Deadline = dto.Deadline,
                Status = AdminTaskStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _db.AdminTasks.Add(task);
            await _db.SaveChangesAsync();

            return Ok(await ToDto(task.Id));
        }

        // GET /api/admin/tasks  (Super Admin only) -- "View pending tasks" / "Track progress" across everyone.
        // Optional ?status= and ?assignedTo= filters so a Super Admin can narrow down a long list.
        [HttpGet]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<List<AdminTaskResponseDto>>> ListAll(
            [FromQuery] AdminTaskStatus? status, [FromQuery] Guid? assignedTo)
        {
            var query = _db.AdminTasks
                .Include(t => t.AssignedToAdmin)
                .Include(t => t.AssignedByAdmin)
                .AsQueryable();

            if (status.HasValue) query = query.Where(t => t.Status == status.Value);
            if (assignedTo.HasValue) query = query.Where(t => t.AssignedToAdminId == assignedTo.Value);

            var tasks = await query
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return Ok(tasks.Select(MapToDto));
        }

        // GET /api/admin/tasks/mine  (Admin or Super Admin) -- "View pending tasks" for yourself
        [HttpGet("mine")]
        public async Task<ActionResult<List<AdminTaskResponseDto>>> ListMine([FromQuery] AdminTaskStatus? status)
        {
            var adminId = User.GetAdminId();
            var query = _db.AdminTasks
                .Include(t => t.AssignedToAdmin)
                .Include(t => t.AssignedByAdmin)
                .Where(t => t.AssignedToAdminId == adminId);

            if (status.HasValue) query = query.Where(t => t.Status == status.Value);

            var tasks = await query
                .OrderBy(t => t.Deadline == null)
                .ThenBy(t => t.Deadline)
                .ToListAsync();

            return Ok(tasks.Select(MapToDto));
        }

        // PATCH /api/admin/tasks/{id}/status -- "Mark completed" / "Track progress".
        // Either the assigned admin themself, or any Super Admin, can update status.
        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, AdminTaskStatusUpdateDto dto)
        {
            var task = await _db.AdminTasks.FindAsync(id);
            if (task == null) return NotFound(new { message = "Task not found." });

            var adminId = User.GetAdminId();
            var isSuperAdmin = User.IsInRole("SuperAdmin");
            if (!isSuperAdmin && task.AssignedToAdminId != adminId)
                return Forbid();

            task.Status = dto.Status;
            task.CompletedAt = dto.Status == AdminTaskStatus.Completed ? DateTime.UtcNow : null;
            await _db.SaveChangesAsync();

            return Ok(new { task.Id, Status = task.Status.ToString(), task.CompletedAt });
        }

        // PATCH /api/admin/tasks/{id}  (Super Admin only) -- edit title/description/deadline/reassign
        [HttpPatch("{id:guid}")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<AdminTaskResponseDto>> Edit(Guid id, AdminTaskEditDto dto)
        {
            var task = await _db.AdminTasks.FindAsync(id);
            if (task == null) return NotFound(new { message = "Task not found." });

            if (dto.AssignedToAdminId.HasValue)
            {
                var assigneeExists = await _db.Admins.AnyAsync(a => a.Id == dto.AssignedToAdminId.Value);
                if (!assigneeExists)
                    return BadRequest(new { message = "The admin this task would be reassigned to doesn't exist." });
                task.AssignedToAdminId = dto.AssignedToAdminId.Value;
            }

            if (dto.Title != null) task.Title = dto.Title;
            if (dto.Description != null) task.Description = dto.Description;
            if (dto.Deadline.HasValue) task.Deadline = dto.Deadline;

            await _db.SaveChangesAsync();

            return Ok(await ToDto(task.Id));
        }

        // DELETE /api/admin/tasks/{id}  (Super Admin only)
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var task = await _db.AdminTasks.FindAsync(id);
            if (task == null) return NotFound(new { message = "Task not found." });

            _db.AdminTasks.Remove(task);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        private async Task<AdminTaskResponseDto?> ToDto(Guid taskId)
        {
            var task = await _db.AdminTasks
                .Include(t => t.AssignedToAdmin)
                .Include(t => t.AssignedByAdmin)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            return task == null ? null : MapToDto(task);
        }

        private static AdminTaskResponseDto MapToDto(AdminTask t) => new AdminTaskResponseDto
        {
            Id = t.Id,
            Title = t.Title,
            Description = t.Description,
            AssignedToAdminId = t.AssignedToAdminId,
            AssignedToAdminName = t.AssignedToAdmin?.FullName ?? "Unknown",
            AssignedByAdminId = t.AssignedByAdminId,
            AssignedByAdminName = t.AssignedByAdmin?.FullName,
            Deadline = t.Deadline,
            Status = t.Status.ToString(),
            CreatedAt = t.CreatedAt,
            CompletedAt = t.CompletedAt
        };
    }
}
