using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.Models
{
    // QUESTION BANK ("PYQs") BROWSING PROGRESS -- lets a student pick up where they left off when they
    // browse previous-year questions one by one (filters, last question, the options they tapped).
    //
    // This is deliberately NOT a TestAttempt / StudentTestResult: browsing questions in the Question
    // Bank has no timer, score, submission or result review, and must never show up in tests history,
    // Smart Progress analytics, rank or XP. The formal Test / Mock Test / Paper-attempt lifecycle is not
    // touched by these tables at all.
    //
    // One ACTIVE session per (user, scope) -- enforced by a filtered unique index. "Start fresh" archives
    // the active session (IsActive = false) and opens a new one; nothing is deleted, so earlier answers
    // stay in the database.
    public class QuestionBrowseSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        // Reserved so another browsing surface could reuse the table; today only "QuestionBank".
        [Required, MaxLength(32)]
        public string Scope { get; set; } = QuestionBrowseScopes.QuestionBank;

        // The client's filter selection as a small JSON object (exam / year / subject / topic ids + names,
        // search text). Opaque to the server apart from "is a JSON object, at most 4000 chars".
        [MaxLength(4000)]
        public string? FiltersJson { get; set; }

        public Guid? LastQuestionId { get; set; }

        // Zero-based index of the last visited question in the filtered list, as a restore hint; the
        // client re-locates by LastQuestionId first, so a re-ordered list still lands on the right question.
        public int LastPosition { get; set; }

        [MaxLength(16)]
        public string? ViewMode { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Last-write-wins stamp for the position fields (client clock, clamped server-side).
        public DateTime? PositionClientUpdatedAt { get; set; }

        public DateTime? ArchivedAt { get; set; }

        public ICollection<QuestionBrowseAnswer> Answers { get; set; } = new List<QuestionBrowseAnswer>();
    }

    // The option a student tapped for one question within one browsing session. One row per
    // (session, question) -- a changed answer updates the same row (latest ClientUpdatedAt wins).
    public class QuestionBrowseAnswer
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SessionId { get; set; }
        public QuestionBrowseSession? Session { get; set; }

        public Guid QuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? QuestionBankQuestion { get; set; }

        // "A" | "B" | "C" | "D"
        [Required, MaxLength(1)]
        public string SelectedOption { get; set; } = string.Empty;

        public DateTime ClientUpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public static class QuestionBrowseScopes
    {
        public const string QuestionBank = "QuestionBank";
    }
}
