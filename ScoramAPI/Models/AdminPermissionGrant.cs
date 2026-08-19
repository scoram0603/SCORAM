using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    // One row per (Admin, Permission) granted. A Super Admin implicitly has every permission
    // regardless of what's granted here (enforced in code, not stored) -- this table only matters
    // for regular Admin accounts, which start with zero permissions until a Super Admin grants some.
    public class AdminPermissionGrant
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid AdminId { get; set; }
        public Admin? Admin { get; set; }

        public AdminPermission Permission { get; set; }

        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    }
}
