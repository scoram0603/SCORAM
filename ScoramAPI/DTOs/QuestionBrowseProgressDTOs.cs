using System.Text.Json;

namespace ScoramAPI.DTOs
{
    // One option the student tapped. ClientUpdatedAt orders rapid / replayed writes (latest wins).
    public class QuestionBrowseAnswerInDto
    {
        public Guid QuestionId { get; set; }
        public string? SelectedOption { get; set; }
        public DateTime? ClientUpdatedAt { get; set; }
    }

    public class QuestionBrowsePositionDto
    {
        // JSON object describing the client's filters (opaque to the server).
        public JsonElement? Filters { get; set; }
        public Guid? LastQuestionId { get; set; }
        public int? LastPosition { get; set; }
        public string? ViewMode { get; set; }
        public DateTime? ClientUpdatedAt { get; set; }
    }

    // PUT /api/question-bank/progress -- both parts are optional so the client can send just answers
    // (every tap) or just the position (leaving the screen / lifecycle pause).
    public class QuestionBrowseSyncRequestDto
    {
        // The session the client believes is active. If another device started a fresh session in the
        // meantime the server answers 409 instead of mixing old answers into the new session.
        public Guid? SessionId { get; set; }
        public QuestionBrowsePositionDto? Position { get; set; }
        public List<QuestionBrowseAnswerInDto>? Answers { get; set; }
    }

    public class QuestionBrowseSyncResultDto
    {
        public Guid SessionId { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int AppliedAnswers { get; set; }
        // Dropped because a newer write for the same question already exists, or the question is unknown.
        public int IgnoredAnswers { get; set; }
    }

    public class QuestionBrowseAnswerDto
    {
        public Guid QuestionId { get; set; }
        public string SelectedOption { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
    }

    public class QuestionBrowseProgressDto
    {
        public Guid SessionId { get; set; }
        public JsonElement? Filters { get; set; }
        public Guid? LastQuestionId { get; set; }
        public int LastPosition { get; set; }
        public string? ViewMode { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int AnsweredCount { get; set; }
        public List<QuestionBrowseAnswerDto> Answers { get; set; } = new();
    }

    public class QuestionBrowseNewSessionResultDto
    {
        public Guid SessionId { get; set; }
        public DateTime StartedAt { get; set; }
    }
}
