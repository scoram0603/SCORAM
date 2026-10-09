using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.Models
{
    // ==================================================================================
    // SHARED STIMULUS + PAPER INSTRUCTIONS (admin content-authoring, additive only)
    //
    // SharedStimulus   = content common to several questions (passage / table / chart / image...).
    //                    Stored ONCE; questions reference it, never copy it.
    // PaperQuestionStimulus = the reference (Question OR PaperQuestionBankLink -> SharedStimulus).
    //                    It attaches to the paper OCCURRENCE of a question, never to the canonical
    //                    Question Bank row, so the same QB question can appear in another paper
    //                    with no stimulus.
    // PaperInstruction = ordered instruction blocks of one paper, independent of any Question.
    //
    // Content uses the EXISTING ContentBlocksJson format (text/math/image/table) -- see
    // DTOs/ContentBlockDto.cs -- so the existing renderer/validator/image URLs are reused.
    // Nothing here changes Question, Paper, PaperQuestionBankLink or any existing column.
    // ==================================================================================
    public enum SharedStimulusStatus
    {
        Active,
        Archived
    }

    public enum PaperInstructionStatus
    {
        Active,
        Archived
    }

    public class SharedStimulus : IHasBusinessId
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // STM0001... assigned centrally in ScoramDbContext.SaveChangesAsync; never set by callers.
        [MaxLength(30)]
        public string? BusinessId { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        // Optional free-text category: Passage / Table / Chart / Graph / Diagram / Case Study /
        // Image / Other. Open string on purpose (no migration for a new category).
        [MaxLength(50)]
        public string? Type { get; set; }

        [MaxLength(30)]
        public string? Language { get; set; }

        // Validated via ContentBlocksJsonHelper.ValidateAndSerialize before storing.
        public string? ContentBlocksJson { get; set; }

        public SharedStimulusStatus Status { get; set; } = SharedStimulusStatus.Active;

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Guid? UpdatedByAdminId { get; set; }
        public Admin? UpdatedByAdmin { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ICollection<PaperQuestionStimulus> Links { get; set; } = new List<PaperQuestionStimulus>();
    }

    // Exactly one of QuestionId / PaperQuestionBankLinkId is set (DB CHECK constraint).
    // Paper is derived through the question / link, not stored here -- avoids a second cascade
    // path (Paper -> Question -> Link and Paper -> Link) on SQL Server.
    public class PaperQuestionStimulus
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SharedStimulusId { get; set; }
        public SharedStimulus? SharedStimulus { get; set; }

        public Guid? QuestionId { get; set; }
        public Question? Question { get; set; }

        public Guid? PaperQuestionBankLinkId { get; set; }
        public PaperQuestionBankLink? PaperQuestionBankLink { get; set; }

        // Order among several stimuli on the same question (0 = first). Question order itself
        // is untouched -- it always comes from QuestionNumber.
        public int DisplayOrder { get; set; }

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class PaperInstruction
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid PaperId { get; set; }
        public Paper? Paper { get; set; }

        [MaxLength(200)]
        public string? Title { get; set; }

        public string? ContentBlocksJson { get; set; }

        [MaxLength(30)]
        public string? Language { get; set; }

        public int DisplayOrder { get; set; }

        public PaperInstructionStatus Status { get; set; } = PaperInstructionStatus.Active;

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Guid? UpdatedByAdminId { get; set; }
        public Admin? UpdatedByAdmin { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
