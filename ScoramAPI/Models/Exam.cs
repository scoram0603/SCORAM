using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.Models
{
    // The admin-managed list of exams (SSC CGL, SSC CHSL, Railway NTPC, ...) that powers the
    // "choose exam / + New Exam" step of the PYQ upload wizard. Kept as its own table (rather than
    // just a free-text field on Question) so it can carry a logo and be reused as a picklist instead
    // of admins re-typing the same exam name slightly differently every time.
    public class Exam
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty; // "SSC CGL", "SSC CHSL", "Railway NTPC"

        // ORGANIZATION HIERARCHY -- which body runs this exam (SSC, RRB, UPSC, ...), so pickers can
        // group "pick an Organization, then pick from its exams" instead of showing every exam ever
        // created in one flat list. Nullable, and deliberately so: existing exams created before this
        // feature stay perfectly valid with no Organization assigned -- there's no forced backfill or
        // migration step (same reasoning MockTest.ExamId's own nullable FK had) -- an admin just
        // assigns one later via ExamsController.Update. One Organization per exam, not many-to-many:
        // "SSC CGL" only ever makes sense under "SSC".
        public Guid? OrganizationId { get; set; }
        public Organization? Organization { get; set; }

        // Relative URL under wwwroot, e.g. "/uploads/exam-logos/8f14e45f....png". Null if no logo
        // was uploaded yet -- a logo isn't required to create the exam and start uploading questions.
        public string? LogoUrl { get; set; }

        // ADMIN EXAM MANAGEMENT -- hides the exam from every student-facing list (Question Bank
        // filters, PYQ Bank, exam picker, chat room search) and disables its chat room, without
        // deleting anything. The safe alternative to DELETE for an exam that already has content --
        // see ExamsController.Delete, which refuses to hard-delete anything with real data attached.
        public bool IsBlocked { get; set; } = false;

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Question> Questions { get; set; } = new List<Question>();
        public ICollection<Paper> Papers { get; set; } = new List<Paper>();
    }
}
