using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.DTOs
{
    public class PaperInstructionSaveDto
    {
        [MaxLength(200)] public string? Title { get; set; }
        [MaxLength(30)] public string? Language { get; set; }
        public List<ContentBlockDto> ContentBlocks { get; set; } = new();
    }

    public class PaperInstructionDto
    {
        public Guid Id { get; set; }
        public Guid PaperId { get; set; }
        public string? Title { get; set; }
        public string? Language { get; set; }
        public int DisplayOrder { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<ContentBlockDto> ContentBlocks { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // Must list EVERY active instruction of the paper exactly once, in the new order.
    public class PaperInstructionReorderDto
    {
        public List<Guid> OrderedIds { get; set; } = new();
    }
}
