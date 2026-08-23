using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    public class Question
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // ---------- Legacy fields (pre-Paper uploads) ----------
        // Kept for backward compatibility with rows created before the Paper entity existed.
        // New questions are created via PaperId instead -- see PapersController / QuestionsController.
        [MaxLength(100)]
        public string? ExamName { get; set; }   // SSC CGL, Railway NTPC, etc.

        public Guid? ExamId { get; set; }
        public Exam? Exam { get; set; }

        public int Year { get; set; }

        [MaxLength(50)]
        public string? Shift { get; set; }

        [MaxLength(30)]
        public string? Language { get; set; }

        // ---------- Paper-based upload (current flow) ----------
        // The paper (Exam+Year+Shift+Language+PaperCode) this question belongs to. Nullable so legacy
        // rows without a Paper don't break, but every question created through the current admin
        // upload flow always sets this.
        public Guid? PaperId { get; set; }
        public Paper? Paper { get; set; }

        // The question's number in the original paper (Q.45, etc.) -- required for anything uploaded
        // through the Paper flow so the paper can be reconstructed in its original order. There's a
        // unique index on (PaperId, QuestionNumber) to catch accidental double-entry.
        public int? QuestionNumber { get; set; }

        [Required, MaxLength(50)]
        public string Subject { get; set; } = string.Empty;    // Math, Reasoning, English, GK

        [Required, MaxLength(100)]
        public string Topic { get; set; } = string.Empty;

        public DifficultyLevel DifficultyLevel { get; set; } = DifficultyLevel.Medium;

        [Required]
        public string QuestionText { get; set; } = string.Empty;

        // Optional diagram/graph/map alongside the question text -- many PYQs (geometry, map reading,
        // pattern completion) can't be represented as text alone.
        public string? QuestionImageUrl { get; set; }

        [Required] public string OptionA { get; set; } = string.Empty;
        [Required] public string OptionB { get; set; } = string.Empty;
        [Required] public string OptionC { get; set; } = string.Empty;
        [Required] public string OptionD { get; set; } = string.Empty;

        // Each option can optionally carry its own image (e.g. "which shape completes the pattern"
        // questions where the options themselves are diagrams, not text).
        public string? OptionAImageUrl { get; set; }
        public string? OptionBImageUrl { get; set; }
        public string? OptionCImageUrl { get; set; }
        public string? OptionDImageUrl { get; set; }

        public OptionLetter CorrectOption { get; set; }

        public string? Explanation { get; set; }
        public string? ExplanationImageUrl { get; set; }

        [MaxLength(255)]
        public string? SourceReference { get; set; }

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        // Set when this question came from a bulk import rather than the one-by-one upload form --
        // see Controllers/BulkImportController.cs. Lets a bad import be rolled back by deleting
        // exactly the questions it created, without touching anything entered by hand.
        public Guid? ImportJobId { get; set; }
        public ImportJob? ImportJob { get; set; }

        // Set once this PYQ question has been auto-mirrored into the Question Bank (see
        // IQuestionBankMirrorService, called from QuestionsController.Create/Update and
        // BulkImportController.Commit) -- so it shows up in Question Bank search, Practice Tests,
        // Weak Topics Quiz, Daily Quiz, and Discussions, not just this one paper. Null for rows
        // created before this existed, or if the mirror attempt failed (mirroring is best-effort and
        // never blocks saving the actual PYQ question). Deliberately NOT a hard foreign key with
        // cascade behavior -- if the Question Bank copy is later deleted independently, this PYQ
        // question must keep working exactly as before.
        public Guid? MirroredToQuestionBankQuestionId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<QuestionSolution> Solutions { get; set; } = new List<QuestionSolution>();
        public ICollection<QuestionReport> Reports { get; set; } = new List<QuestionReport>();
        public ICollection<QuestionComment> Comments { get; set; } = new List<QuestionComment>();
        public ICollection<QuestionVote> Votes { get; set; } = new List<QuestionVote>();
        public ICollection<MockTestQuestion> MockTestQuestions { get; set; } = new List<MockTestQuestion>();
    }

    public class QuestionSolution
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Nullable so this one table/moderation-queue can serve BOTH the legacy Paper-based Question
        // and the new Question Bank's QuestionBankQuestion (SCORAM_QUESTION_BANK) -- exactly one of
        // QuestionId / QuestionBankQuestionId is ever set, never both, never neither. Enforced at the
        // application layer (SolutionsController), not a DB CHECK constraint, matching how
        // QuestionComment already does the same dual-author trick above.
        public Guid? QuestionId { get; set; }
        public Question? Question { get; set; }

        public Guid? QuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? QuestionBankQuestion { get; set; }

        public SolutionType SolutionType { get; set; }

        [Required, MaxLength(150)]
        public string Title { get; set; } = string.Empty;

        // Higher shows first (after IsEasiestMethod, before upvotes) -- lets an admin manually
        // curate solution order for a question without needing to touch IsEasiestMethod, which is
        // meant for "the one best method" rather than general ranking.
        public int Priority { get; set; } = 0;

        // Either a student or an admin submitted this solution
        public Guid? SubmittedByUserId { get; set; }
        public User? SubmittedByUser { get; set; }

        public Guid? SubmittedByAdminId { get; set; }
        public Admin? SubmittedByAdmin { get; set; }

        [Required]
        public string SolutionText { get; set; } = string.Empty;

        public string? ImageUrl { get; set; }

        public int UpvoteCount { get; set; } = 0;

        public bool IsVerified { get; set; } = false;

        public bool IsEasiestMethod { get; set; } = false;

        public bool IsApproved { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class QuestionReport
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Nullable for the same reason as QuestionSolution above -- one Report table/queue serves
        // both the legacy Question and the new QuestionBankQuestion (SCORAM_QUESTION_BANK). Exactly
        // one of QuestionId / QuestionBankQuestionId is set.
        public Guid? QuestionId { get; set; }
        public Question? Question { get; set; }

        public Guid? QuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? QuestionBankQuestion { get; set; }

        public Guid ReportedByUserId { get; set; }
        public User? ReportedByUser { get; set; }

        public ReportType ReportType { get; set; }

        public string? ProofUrl { get; set; }

        public string? Description { get; set; }

        public ReportStatus Status { get; set; } = ReportStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ResolvedAt { get; set; }
    }

    public class QuestionComment
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Nullable so the same comment/discussion-thread table serves both the legacy Paper-based
        // Question and the new QuestionBankQuestion (SCORAM_QUESTION_BANK), same dual-FK pattern as
        // QuestionSolution/QuestionReport above -- exactly one of QuestionId/QuestionBankQuestionId
        // is ever set.
        public Guid? QuestionId { get; set; }
        public Question? Question { get; set; }

        public Guid? QuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? QuestionBankQuestion { get; set; }

        // Nullable because a comment can instead be authored by an admin (SubmittedByAdminId below) --
        // exactly one of UserId/SubmittedByAdminId is set, never both, never neither.
        public Guid? UserId { get; set; }
        public User? User { get; set; }

        // Set instead of UserId for an admin-authored reply (e.g. an official answer to a doubt) --
        // mirrors QuestionSolution's dual-author pattern. Automatically implies IsAdminHighlighted.
        public Guid? SubmittedByAdminId { get; set; }
        public Admin? SubmittedByAdmin { get; set; }

        public Guid? ParentCommentId { get; set; }
        public QuestionComment? ParentComment { get; set; }

        [Required]
        public string CommentText { get; set; } = string.Empty;

        public int UpvoteCount { get; set; } = 0;
        public int DownvoteCount { get; set; } = 0;

        public bool IsPinned { get; set; } = false;

        public bool IsAdminHighlighted { get; set; } = false;

        // Set by the top-level comment's own author (or an admin) once their doubt has been answered
        // -- only meaningful on top-level comments; replies don't carry their own resolved state.
        public bool IsResolved { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // Per-user record of an upvote/downvote on a QuestionComment -- makes Upvote/Downvote in
    // DiscussionsController an actual toggle (vote / switch / un-vote) instead of an
    // anyone-can-click-forever counter. UNIQUE on (UserId, CommentId): one vote per user per comment.
    public class CommentVote
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CommentId { get; set; }
        public QuestionComment? Comment { get; set; }

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public bool IsUpvote { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // Per-user Like/Dislike on a QUESTION itself (distinct from a comment vote above) -- shared by
    // the legacy Question and the new QuestionBankQuestion, same dual-nullable-FK pattern as
    // QuestionComment. UNIQUE on (UserId, QuestionId) and (UserId, QuestionBankQuestionId): a
    // student can like/dislike a given question exactly once, and can flip or retract that vote.
    public class QuestionVote
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid? QuestionId { get; set; }
        public Question? Question { get; set; }

        public Guid? QuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? QuestionBankQuestion { get; set; }

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public bool IsLike { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CommentReport
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CommentId { get; set; }
        public QuestionComment? Comment { get; set; }

        public Guid ReportedByUserId { get; set; }
        public User? ReportedByUser { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        public ReportStatus Status { get; set; } = ReportStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }
    }
}
