using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;

namespace ScoramAPI.Services
{
    public interface IAdminPermissionService
    {
        /// <summary>True if the authenticated admin can perform the given action. Super Admins always
        /// return true regardless of explicit grants. Checked against the database on every call
        /// (rather than a JWT claim) so a Super Admin revoking a permission takes effect immediately --
        /// this app has ~100 admins, so the extra lookup is negligible; it's the ~5 lakh students
        /// hitting high-traffic endpoints where a DB round trip per request would actually matter, and
        /// none of their endpoints go through this check.</summary>
        Task<bool> HasPermissionAsync(ClaimsPrincipal user, AdminPermission permission);
    }

    public class AdminPermissionService : IAdminPermissionService
    {
        private readonly ScoramDbContext _db;

        public AdminPermissionService(ScoramDbContext db)
        {
            _db = db;
        }

        public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, AdminPermission permission)
        {
            if (user.IsInRole("SuperAdmin")) return true;

            var adminId = user.GetAdminId();
            return await _db.AdminPermissionGrants
                .AnyAsync(g => g.AdminId == adminId && g.Permission == permission);
        }
    }
}
