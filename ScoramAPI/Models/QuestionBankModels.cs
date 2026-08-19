using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    // ==================================================================================
    // SCORAM_QUESTION_BANK — individual, searchable PYQ question bank. Deliberately separate
    // from Paper/Question (the existing "upload a full paper" flow): a Question Bank question isn't
    // tied to one Paper/Exam/Year -- the SAME question can be tagged against several exam+year
    // combinations (see QuestionBankExamMapping) without duplicating the question row. See the
    // Master Prompt this feature was built from for the full spec.
    //
    // Reused from the existing app rather than duplicated here: Exam (picklist), QuestionSolution
    // and QuestionReport (both got a nullable QuestionBankQuestionId added alongside their existing
    // nullable QuestionId -- see QuestionModels.cs) so "Alternative Solution" and "Report Question"
    // work identically for both the legacy Paper-based Question and this new QuestionBankQuestion.
    // ==================================================================================

    public class QuestionBankSubject
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty; // "Ancient History", "Quantitative Aptitude"

        public bool IsActive { get; set; } = true;

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<QuestionBankTopic> Topics { get; set; } = new List<QuestionBankTopic>();
        public ICollection<QuestionBankQuestion> Questions { get; set; } = new List<QuestionBankQuestion>();
    }

    public class QuestionBankTopic
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SubjectId { get; set; }
        public QuestionBankSubject? Subject { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty; // "Stone Age", "Percentage"

        public bool IsActive { get; set; } = true;

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<QuestionBankQuestion> Questions { get; set; } = new List<QuestionBankQuestion>();
    }

    public class QuestionBankQuestion
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string QuestionText { get; set; } = string.Empty;

        // Lowercased, whitespace-collapsed, punctuation-stripped copy of QuestionText -- see
        // Services/QuestionBankImportService.NormalizeForDuplicateCheck. Used for duplicate detection
        // (section 13 of the spec) and as a prefix-searchable column when Meilisearch/full-text isn't
        // available. Kept in sync with QuestionText on every create/update.
        [Required]
        public string NormalizedQuestionText { get; set; } = string.Empty;

        [Required] public string OptionA { get; set; } = string.Empty;
        [Required] public string OptionB { get; set; } = string.Empty;
        [Required] public string OptionC { get; set; } = string.Empty;
        [Required] public string OptionD { get; set; } = string.Empty;

        public OptionLetter CorrectOption { get; set; }

        // SCORAM_TESTS: added for Practice Test filtering ("Subject/Topic/Exam/Year/Difficulty").
        // Mirrors Question.DifficultyLevel (same enum, same Medium default) -- kept optional to set
        // at add/import time since it wasn't part of the original Question Bank spec, but every
        // question has a sensible default so nothing needs backfilling to start using it.
        public DifficultyLevel DifficultyLevel { get; set; } = DifficultyLevel.Medium;

        public string? Explanation { get; set; }

        public Guid SubjectId { get; set; }
        public QuestionBankSubject? Subject { get; set; }

        public Guid TopicId { get; set; }
        public QuestionBankTopic? Topic { get; set; }

        // Free-text pointer back to a source book/PDF -- NOT a foreign key to Paper. Section 21 of
        // the spec is explicit that Question Bank must work independently of the Paper/PYP upload
        // flow; this is only ever an optional citation string ("NCERT Class 11, Ch. 4", "SSC CGL
        // 2018 Booklet Set A Q.45"), never a hard link.
        [MaxLength(255)]
        public string? SourceReference { get; set; }

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        // Set when this question came from a bulk import (Excel/JSON) rather than the one-by-one
        // admin form -- mirrors Question.ImportJobId / ImportJob, but points at
        // QuestionBankImportJob instead since the two import flows track different things (no
        // PaperId, no QuestionNumber).
        public Guid? ImportJobId { get; set; }
        public QuestionBankImportJob? ImportJob { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public ICollection<QuestionBankExamMapping> ExamMappings { get; set; } = new List<QuestionBankExamMapping>();
        public ICollection<QuestionSolution> Solutions { get; set; } = new List<QuestionSolution>();
        public ICollection<QuestionReport> Reports { get; set; } = new List<QuestionReport>();
        public ICollection<QuestionComment> Comments { get; set; } = new List<QuestionComment>();
        public ICollection<QuestionVote> Votes { get; set; } = new List<QuestionVote>();
    }

    // One question can appear in many exams and/or many years (section 6 of the spec) -- e.g. the
    // same question tagged SSC CGL 2018, SSC CHSL 2020, and UP TGT 2022 is three rows here pointing
    // at ONE QuestionBankQuestion, not three duplicated questions.
    public class QuestionBankExamMapping
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid QuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? QuestionBankQuestion { get; set; }

        // Reuses the existing Exam master table (Models/Exam.cs) -- same picklist admins already
        // manage for PYQ papers, rather than a second parallel "exam name" list.
        public Guid ExamId { get; set; }
        public Exam? Exam { get; set; }

        public int Year { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // Question Bank's own bulk-import job record -- deliberately NOT reusing Models/ImportJob, which
    // is hard-wired to a required PaperId and the Paper-based QuestionNumber-uniqueness flow. Same
    // "preview creates a row, rows themselves live briefly in IMemoryCache until commit" pattern as
    // ImportJob though -- see Controllers/QuestionBankAdminController.cs.
    public class QuestionBankImportJob
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        public ImportFileFormat Format { get; set; } // Excel or Json only for Question Bank

        public ImportJobStatus Status { get; set; } = ImportJobStatus.PendingReview;

        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        public int InvalidRows { get; set; }
        public int DuplicateRows { get; set; }

        // Actual number of NEW question rows written at commit time. May be less than ValidRows if
        // some valid rows turned out to be duplicates of existing questions (those get their
        // exam/year mapping merged into the existing question instead of a new row -- see
        // MergedIntoExistingCount).
        public int ImportedCount { get; set; }

        // How many valid rows were duplicates of an already-existing question and got merged
        // (new QuestionBankExamMapping added to the existing question) rather than creating a new
        // QuestionBankQuestion row.
        public int MergedIntoExistingCount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CommittedAt { get; set; }
    }
}
