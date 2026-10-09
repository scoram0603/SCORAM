using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.DTOs
{
    // ---------- Shared Stimulus (admin) ----------
    public class SharedStimulusSaveDto
    {
        [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
        [MaxLength(500)] public string? Description { get; set; }
        [MaxLength(50)] public string? Type { get; set; }        // Passage / Table / Chart / Graph / Diagram / Case Study / Image / Other
        [MaxLength(30)] public string? Language { get; set; }
        // Same { type, content } blocks the question editor already produces (text/math/image/table).
        public List<ContentBlockDto> ContentBlocks { get; set; } = new();
    }

    public class SharedStimulusListItemDto
    {
        public Guid Id { get; set; }
        public string? BusinessId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? Language { get; set; }
        public string Status { get; set; } = string.Empty;
        public int LinkedQuestionCount { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class SharedStimulusDetailDto : SharedStimulusListItemDto
    {
        public string? Description { get; set; }
        public List<ContentBlockDto> ContentBlocks { get; set; } = new();
    }

    public class PagedResultDto<T>
    {
        public List<T> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class StimulusLinkedQuestionDto
    {
        public Guid PaperId { get; set; }
        public string PaperName { get; set; } = string.Empty;
        public int? QuestionNumber { get; set; }
        public string Source { get; set; } = string.Empty;   // "Paper" | "QuestionBank"
        public Guid? QuestionId { get; set; }
        public Guid? LinkId { get; set; }
        public string QuestionTextPreview { get; set; } = string.Empty;
    }

    public class ImageUploadResultDto { public string Url { get; set; } = string.Empty; }

    // ---------- Question <-> Stimulus links (paper scoped) ----------
    // A target is one question OCCURRENCE in the paper: exactly one of QuestionId / LinkId.
    public class StimulusTargetDto
    {
        public Guid? QuestionId { get; set; }
        public Guid? LinkId { get; set; }
    }

    public class StimulusAttachRequestDto
    {
        public Guid StimulusId { get; set; }
        public List<StimulusTargetDto> Targets { get; set; } = new();
    }

    public class StimulusDetachRequestDto : StimulusAttachRequestDto { }

    public class StimulusReplaceRequestDto
    {
        public Guid FromStimulusId { get; set; }
        public Guid ToStimulusId { get; set; }
        public List<StimulusTargetDto> Targets { get; set; } = new();
    }

    public class StimulusLinkOperationResultDto
    {
        public int Changed { get; set; }          // created / removed / re-pointed
        public int Skipped { get; set; }          // already linked / not linked -- nothing to do
        public string Message { get; set; } = string.Empty;
    }

    public class PaperStimulusLinkDto
    {
        public Guid? QuestionId { get; set; }
        public Guid? LinkId { get; set; }
        public int? QuestionNumber { get; set; }
        public Guid StimulusId { get; set; }
        public string? StimulusBusinessId { get; set; }
        public string StimulusTitle { get; set; } = string.Empty;
        public string StimulusStatus { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
    }
}
