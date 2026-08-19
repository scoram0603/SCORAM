using ScoramAPI.Enums;

namespace ScoramAPI.DTOs
{
    // Replace-all semantics: the Super Admin sends the complete set of permissions this admin should
    // have, rather than incremental grant/revoke calls -- simpler for a checkbox-list UI to reason about.
    public class AdminPermissionsUpdateDto
    {
        public List<AdminPermission> Permissions { get; set; } = new();
    }

    public class AdminPermissionsResponseDto
    {
        public Guid AdminId { get; set; }
        public List<string> Permissions { get; set; } = new();
    }
}
