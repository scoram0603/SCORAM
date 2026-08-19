using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    public class SyllabusTopic
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(100)]
        public string ExamName { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Subject { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string TopicName { get; set; } = string.Empty;

        public int OrderIndex { get; set; }

        public ICollection<StudentSyllabusProgress> StudentProgress { get; set; } = new List<StudentSyllabusProgress>();
    }

    public class StudentSyllabusProgress
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public Guid SyllabusTopicId { get; set; }
        public SyllabusTopic? SyllabusTopic { get; set; }

        public SyllabusStatus Status { get; set; } = SyllabusStatus.NotStarted;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CurrentAffair
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Content { get; set; } = string.Empty;

        public CurrentAffairsCategory Category { get; set; }

        public DateOnly PublishedDate { get; set; }

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }
    }

    public class TypingTestResult
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public TypingLanguage Language { get; set; }

        public decimal WPM { get; set; }

        public decimal Accuracy { get; set; }

        public int DurationSeconds { get; set; }

        public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
    }

    public class ExamCalendarEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(100)]
        public string ExamName { get; set; } = string.Empty;

        public ExamEventType EventType { get; set; }

        public DateOnly EventDate { get; set; }

        public string? Description { get; set; }
    }

    public class JobAlert
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string Organization { get; set; } = string.Empty;

        public DateOnly PostedDate { get; set; }

        public DateOnly? ApplicationDeadline { get; set; }

        public string? Description { get; set; }

        public string? NotificationUrl { get; set; }

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }
    }
}
