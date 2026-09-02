using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.DTOs
{
    // ORGANIZATION HIERARCHY -- see Models/Organization.cs and OrganizationsController.cs. Same
    // shape as ExamCreateDto/ExamUpdateDto/ExamBlockDto/ExamResponseDto for exactly the same reason
    // Organization mirrors Exam's own model shape.
    public class OrganizationCreateDto
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public IFormFile? Logo { get; set; }
    }

    public class OrganizationUpdateDto
    {
        [MaxLength(100)]
        public string? Name { get; set; }
        public IFormFile? Logo { get; set; }
    }

    public class OrganizationBlockDto
    {
        public bool IsBlocked { get; set; }
    }

    public class OrganizationResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public bool IsBlocked { get; set; }
        public int ExamCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
