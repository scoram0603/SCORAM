using ScoramAPI.Models;

namespace ScoramAPI.DTOs
{
    // Meilisearch's primary key must be a string, hence Id here (not Guid) -- everything else is
    // exactly what a search result card needs to render without hitting the database again.
    public class QuestionSearchDocument
    {
        public string Id { get; set; } = string.Empty;
        public Guid PaperId { get; set; }
        public Guid QuestionId { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public string? ExamLogoUrl { get; set; }
        public int Year { get; set; }
        public string? PaperCode { get; set; }
        public string Language { get; set; } = string.Empty;
        public int QuestionNumber { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
        public string? QuestionImageUrl { get; set; }

        // Single source of truth for "Question entity -> search document", used both when indexing
        // into Meilisearch (PapersController) and when answering from the SQL fallback
        // (FallbackSearchService) -- so a student sees an identical result shape regardless of which
        // backend actually answered their search. Requires q.Paper to be loaded (.Include(q => q.Paper)
        // .ThenInclude(p => p.Exam)); every current caller already loads it.
        public static QuestionSearchDocument FromQuestion(Question q) => new QuestionSearchDocument
        {
            Id = q.Id.ToString(),
            QuestionId = q.Id,
            PaperId = q.PaperId ?? Guid.Empty,
            ExamName = q.Paper?.Exam?.Name ?? "Unknown",
            ExamLogoUrl = q.Paper?.Exam?.LogoUrl,
            Year = q.Paper?.Year ?? 0,
            PaperCode = q.Paper?.PaperCode,
            Language = q.Paper?.Language.ToString() ?? "",
            QuestionNumber = q.QuestionNumber ?? 0,
            Subject = q.Subject,
            Topic = q.Topic,
            QuestionText = q.QuestionText,
            QuestionImageUrl = q.QuestionImageUrl
        };
    }
}
