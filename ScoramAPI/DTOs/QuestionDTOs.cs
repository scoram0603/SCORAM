using Microsoft.AspNetCore.Http;
using ScoramAPI.Enums;

namespace ScoramAPI.DTOs
{
    // [FromForm]-bound (multipart/form-data) since each of Question/Option A-D/Explanation can
    // optionally carry an image -- see PapersController for how PaperId/QuestionNumber flow in from
    // the wizard's "context lock" + auto-increment.
    public class QuestionCreateDto
    {
        public Guid PaperId { get; set; }

        // The question's number in the original paper. Required -- this is what lets the paper be
        // reconstructed in its original order for students. Uniqueness within a paper is enforced by
        // a DB index; see ScoramDbContext.
        public int QuestionNumber { get; set; }

        public string Subject { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public DifficultyLevel DifficultyLevel { get; set; } = DifficultyLevel.Medium;

        public string QuestionText { get; set; } = string.Empty;
        public IFormFile? QuestionImage { get; set; }

        public string OptionA { get; set; } = string.Empty;
        public IFormFile? OptionAImage { get; set; }
        public string OptionB { get; set; } = string.Empty;
        public IFormFile? OptionBImage { get; set; }
        public string OptionC { get; set; } = string.Empty;
        public IFormFile? OptionCImage { get; set; }
        public string OptionD { get; set; } = string.Empty;
        public IFormFile? OptionDImage { get; set; }

        public OptionLetter CorrectOption { get; set; }

        public string? Explanation { get; set; }
        public IFormFile? ExplanationImage { get; set; }

        // Optional JSON array of { type, content } blocks -- see DTOs/ContentBlockDto.cs. A plain
        // form-text field (not a file), so it rides alongside the image fields in the same
        // multipart/form-data request. Omit or send empty to skip rich content entirely.
        public string? ContentBlocksJson { get; set; }

        public string? SourceReference { get; set; }
    }

    // Editing an existing question -- same shape as create, minus PaperId (a question doesn't move
    // between papers) and minus the image files being required again (only send an image field if
    // you're replacing that image; existing images are left alone otherwise).
    public class QuestionUpdateDto
    {
        public int QuestionNumber { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public DifficultyLevel DifficultyLevel { get; set; } = DifficultyLevel.Medium;

        public string QuestionText { get; set; } = string.Empty;
        public IFormFile? QuestionImage { get; set; }
        public bool RemoveQuestionImage { get; set; }

        public string OptionA { get; set; } = string.Empty;
        public IFormFile? OptionAImage { get; set; }
        public bool RemoveOptionAImage { get; set; }
        public string OptionB { get; set; } = string.Empty;
        public IFormFile? OptionBImage { get; set; }
        public bool RemoveOptionBImage { get; set; }
        public string OptionC { get; set; } = string.Empty;
        public IFormFile? OptionCImage { get; set; }
        public bool RemoveOptionCImage { get; set; }
        public string OptionD { get; set; } = string.Empty;
        public IFormFile? OptionDImage { get; set; }
        public bool RemoveOptionDImage { get; set; }

        public OptionLetter CorrectOption { get; set; }

        public string? Explanation { get; set; }
        public IFormFile? ExplanationImage { get; set; }
        public bool RemoveExplanationImage { get; set; }

        // Same contract as QuestionCreateDto.ContentBlocksJson. Send the full desired list to
        // replace existing blocks, or omit/leave empty to clear them -- there's no separate "leave
        // unchanged" signal because, unlike images, this field isn't expensive to resend in full.
        public string? ContentBlocksJson { get; set; }

        public string? SourceReference { get; set; }
    }

    public class QuestionResponseDto
    {
        public Guid Id { get; set; }
        public Guid? ExamId { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public string? ExamLogoUrl { get; set; }
        public string? Language { get; set; }
        public int Year { get; set; }

        public Guid? PaperId { get; set; }
        public int? QuestionNumber { get; set; }

        public string Subject { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string DifficultyLevel { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
        public string? QuestionImageUrl { get; set; }
        public string OptionA { get; set; } = string.Empty;
        public string? OptionAImageUrl { get; set; }
        public string OptionB { get; set; } = string.Empty;
        public string? OptionBImageUrl { get; set; }
        public string OptionC { get; set; } = string.Empty;
        public string? OptionCImageUrl { get; set; }
        public string OptionD { get; set; } = string.Empty;
        public string? OptionDImageUrl { get; set; }
        // Optional rich-content sequence -- see DTOs/ContentBlockDto.cs. Empty for every question
        // that doesn't use this feature (the overwhelming majority), so existing clients that ignore
        // unknown response fields see no behavior change.
        public List<ContentBlockDto> ContentBlocks { get; set; } = new();
        public int SolutionCount { get; set; }
        public int LikeCount { get; set; }
        public int DislikeCount { get; set; }
        // true = I liked it, false = I disliked it, null = no vote from me (or not logged in).
        public bool? MyVote { get; set; }
        // Whether the current viewer has this question bookmarked (false when not logged in).
        public bool IsBookmarked { get; set; }
    }

    public class QuestionDetailDto : QuestionResponseDto
    {
        public string CorrectOption { get; set; } = string.Empty;
        public string? Explanation { get; set; }
        public string? ExplanationImageUrl { get; set; }
        public string? SourceReference { get; set; }
    }

    // Query parameters for GET /api/questions
    public class QuestionSearchQuery
    {
        public string? ExamName { get; set; }
        public Guid? ExamId { get; set; }
        public Guid? PaperId { get; set; }
        public string? Language { get; set; }
        public int? Year { get; set; }
        public string? PaperCode { get; set; }
        public int? QuestionNumber { get; set; }
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public DifficultyLevel? DifficultyLevel { get; set; }
        public string? Keyword { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
