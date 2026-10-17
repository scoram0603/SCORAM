using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.Models
{
    // ADMIN PLATFORM -- runtime app configuration (maintenance mode, feature flags, minimum/recommended
    // app versions). A small key/value table instead of one column per setting so adding a flag later is a
    // code change (see Services/AppConfigService.cs's known-key list), never a schema migration.
    //
    // Only keys the server knows about (and validates) are ever written -- the admin UI cannot invent keys.
    public class AppSetting
    {
        [Key, MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        [Required, MaxLength(2000)]
        public string Value { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Deliberately no FK to Admins: the row must survive an admin account being removed, and the
        // audit trail (AuditLog + AppSettingRevision) is what records who changed it.
        public Guid? UpdatedByAdminId { get; set; }
    }

    // Append-only history of every change to an AppSetting. This IS the emergency rollback path:
    // POST /api/admin/app-config/revisions/{id}/rollback writes OldValue back (as a NEW revision, so a
    // rollback is itself recorded and can be rolled back).
    public class AppSettingRevision
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        // Null = the key had no stored value before this change (it was running on its default).
        [MaxLength(2000)]
        public string? OldValue { get; set; }

        [Required, MaxLength(2000)]
        public string NewValue { get; set; } = string.Empty;

        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

        public Guid? ChangedByAdminId { get; set; }

        // Groups the revisions written by one "Save" click so the UI can show them together.
        public Guid BatchId { get; set; }

        [MaxLength(300)]
        public string? Note { get; set; }
    }
}
