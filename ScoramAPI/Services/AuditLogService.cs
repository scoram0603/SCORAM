using ScoramAPI.Data;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    public interface IAuditLogService
    {
        /// <summary>Writes one accountability-trail entry. Deliberately swallows its own failures
        /// (logging them instead of throwing) -- an audit-log write going wrong should never be the
        /// reason a legitimate Publish/Delete/permission-change request fails for an admin.</summary>
        Task LogAsync(Guid adminId, string action, string? targetType = null, Guid? targetId = null, string? detail = null);
    }

    public class AuditLogService : IAuditLogService
    {
        private readonly ScoramDbContext _db;
        private readonly ILogger<AuditLogService> _logger;

        public AuditLogService(ScoramDbContext db, ILogger<AuditLogService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task LogAsync(Guid adminId, string action, string? targetType = null, Guid? targetId = null, string? detail = null)
        {
            try
            {
                _db.AuditLogs.Add(new AuditLog
                {
                    AdminId = adminId,
                    Action = action,
                    TargetType = targetType,
                    TargetId = targetId,
                    Detail = detail
                });
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write audit log entry for action {Action} by admin {AdminId}", action, adminId);
            }
        }
    }
}
