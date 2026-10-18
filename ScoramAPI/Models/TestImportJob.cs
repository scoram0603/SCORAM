using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    // History record for one bulk upload of Mock Tests or Practice Test templates -- the same
    // "preview creates a row, committed rows are tagged with the job id, rollback removes exactly what
    // the job created" idea as QuestionBankImportJob, kept separate because a test upload creates
    // different things (tests, sections, and test-only questions) and is tracked with different counters.
    //
    // What a job created is found through the ImportJobId columns on MockTest / PracticeTestTemplate
    // and QuestionBankQuestion.TestImportJobId, so nothing here needs to list ids.
    public class TestImportJob
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        public TestImportKind Kind { get; set; }

        public ImportFileFormat Format { get; set; }

        // Stored as a readable string (see ScoramDbContext), same as QuestionBankImportJob.Status.
        public ImportJobStatus Status { get; set; } = ImportJobStatus.PendingReview;

        // Tests (or templates) found in the file, and how the validator classified them.
        public int TotalTests { get; set; }
        public int ValidTests { get; set; }
        public int InvalidTests { get; set; }

        // Filled at commit time.
        public int ImportedTests { get; set; }
        public int NewQuestionsCreated { get; set; }
        public int ExistingQuestionsReused { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CommittedAt { get; set; }
        public DateTime? RolledBackAt { get; set; }
    }
}
