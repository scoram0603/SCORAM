using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    public class Admin
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public AdminRole Role { get; set; } = AdminRole.Admin;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<Question> QuestionsUploaded { get; set; } = new List<Question>();
        public ICollection<MockTest> MockTestsCreated { get; set; } = new List<MockTest>();
        public ICollection<CurrentAffair> CurrentAffairsPosted { get; set; } = new List<CurrentAffair>();
        public ICollection<JobAlert> JobAlertsPosted { get; set; } = new List<JobAlert>();
        public ICollection<AdminTask> AssignedTasks { get; set; } = new List<AdminTask>();
        public ICollection<AdminPermissionGrant> PermissionGrants { get; set; } = new List<AdminPermissionGrant>();
        public ICollection<Paper> UploadedPapers { get; set; } = new List<Paper>();
    }
}
