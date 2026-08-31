namespace ScoramAPI.Models
{
    // "MY EXAMS" -- a student's own, persistent list of exams they're preparing for (SSC CGL,
    // RRB NTPC, ...). Drives the default exam context across Question Bank / PYP / Mock Tests /
    // Practice Tests / Weak-Topics Quiz instead of a student having to re-apply the same exam
    // filter in every section (see UserExamsController). Deliberately a real join table -- not a
    // JSON/CSV field on User -- so it stays queryable/indexable the same way every other
    // student-to-content relationship in this schema already is (Bookmark, StudentTestResult, ...).
    public class UserExamPreference
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public Guid ExamId { get; set; }
        public Exam? Exam { get; set; }

        // Exactly one of a student's selected exams may be primary at a time -- enforced by a
        // filtered unique index on (UserId) WHERE IsPrimary = 1, see ScoramDbContext. Used to
        // order/prioritize Home recommendations and the Weak-Topics Quiz question pool when a
        // student is preparing for more than one exam at once.
        public bool IsPrimary { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
