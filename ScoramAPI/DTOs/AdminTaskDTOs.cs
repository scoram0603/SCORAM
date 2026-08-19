using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.DTOs
{
    // Super Admin assigns a task to an Admin (SRS Section 12).
    public class AdminTaskCreateDto
    {
        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        public Guid AssignedToAdminId { get; set; }

        public DateTime? Deadline { get; set; }
    }

    // Super Admin edits an existing task -- title/description/deadline/reassignment.
    // All fields optional so a partial edit doesn't require re-sending everything.
    public class AdminTaskEditDto
    {
        [MaxLength(200)]
        public string? Title { get; set; }

        public string? Description { get; set; }

        public Guid? AssignedToAdminId { get; set; }

        public DateTime? Deadline { get; set; }
    }

    // The assigned Admin (or a Super Admin) moves a task through its lifecycle.
    public class AdminTaskStatusUpdateDto
    {
        [Required]
        public AdminTaskStatus Status { get; set; }
    }

    public class AdminTaskResponseDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid AssignedToAdminId { get; set; }
        public string AssignedToAdminName { get; set; } = string.Empty;
        public Guid? AssignedByAdminId { get; set; }
        public string? AssignedByAdminName { get; set; }
        public DateTime? Deadline { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
