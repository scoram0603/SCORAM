using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    public class AdminTask
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public Guid AssignedToAdminId { get; set; }
        public Admin? AssignedToAdmin { get; set; }

        // The Super Admin who created/assigned this task (SRS Section 12: "Super Admin can assign
        // tasks to admins"). Nullable so existing rows created before this column don't break.
        public Guid? AssignedByAdminId { get; set; }
        public Admin? AssignedByAdmin { get; set; }

        public DateTime? Deadline { get; set; }

        public AdminTaskStatus Status { get; set; } = AdminTaskStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }
    }
}
