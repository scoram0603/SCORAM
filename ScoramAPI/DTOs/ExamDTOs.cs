using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.DTOs
{
    // Bound with [FromForm] since it carries a file -- this is the "+ New Exam" step
    // (Enter Exam Name, Choose Exam Logo) of the admin PYQ upload wizard.
    public class ExamCreateDto
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        // Optional -- an exam can be created and used right away without a logo.
        public IFormFile? Logo { get; set; }
    }

    // ADMIN EXAM MANAGEMENT -- PATCH /api/admin/exams/{id}. Name/Logo are optional (partial update);
    // omit either to leave it unchanged. Logo, if provided, replaces the existing one.
    public class ExamUpdateDto
    {
        [MaxLength(100)]
        public string? Name { get; set; }
        public IFormFile? Logo { get; set; }
    }

    // ADMIN EXAM MANAGEMENT -- PATCH /api/admin/exams/{id}/block
    public class ExamBlockDto
    {
        public bool IsBlocked { get; set; }
    }

    public class ExamResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public bool IsBlocked { get; set; }
        public int QuestionCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
