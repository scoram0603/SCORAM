using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.Models
{
    // A lightweight, append-only trail of "who did what, when" for admin actions that matter for
    // accountability -- publishing/rejecting/deleting content, and admin-account/permission changes.
    // Deliberately denormalized (TargetType/TargetId as plain string/Guid, not a real FK) so writing
    // an entry never fails just because the thing it refers to was itself deleted afterwards, and so
    // this table never needs a migration when a new entity type starts being audited.
    public class AuditLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid AdminId { get; set; }
        public Admin? Admin { get; set; }

        // e.g. "Paper.Publish", "Paper.Delete", "Admin.SetPermissions"
        [MaxLength(100)]
        public string Action { get; set; } = string.Empty;

        // e.g. "Paper", "Admin", "Question"
        [MaxLength(100)]
        public string? TargetType { get; set; }
        public Guid? TargetId { get; set; }

        // Free-form human-readable context (e.g. "Rejected: missing explanation on Q4") -- not meant
        // to be machine-parsed, just enough for a reviewer to understand what happened without
        // cross-referencing other tables.
        [MaxLength(1000)]
        public string? Detail { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
