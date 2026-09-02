using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.Models
{
    // Sits above Exam (SSC, RRB, UPSC, ... each running several of their own exams -- SSC CGL,
    // SSC JE, SSC MTS under SSC; RRB NTPC, RRB JE, RRB ALP under RRB). Exists purely so the exam
    // picker everywhere (My Exams, PYP/Question Bank/Mock Tests/Practice Tests filters, admin's own
    // exam list) can show "pick an Organization, then pick from its exams" instead of one long flat
    // list of every exam at once -- see Exam.OrganizationId's own comment for how the link works.
    //
    // Deliberately the same shape as Exam itself (Name, LogoUrl, IsBlocked, CreatedByAdminId,
    // CreatedAt) -- managed the same way, by the same kind of admin, for the same reasons.
    public class Organization
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty; // "SSC", "RRB", "UPSC"

        // Relative URL under wwwroot, e.g. "/uploads/organization-logos/8f14e45f....png". Null if no
        // logo was uploaded yet -- a logo isn't required to create the organization.
        public string? LogoUrl { get; set; }

        // ADMIN ORGANIZATION MANAGEMENT -- hides the organization (and, per ExamsController's public
        // List(), every exam under it) from every student-facing picker, without deleting anything.
        // Doesn't touch each exam's own IsBlocked flag -- unblocking the organization later restores
        // exactly what was visible before, rather than needing to remember/restore per-exam state.
        public bool IsBlocked { get; set; } = false;

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Exam> Exams { get; set; } = new List<Exam>();
    }
}
