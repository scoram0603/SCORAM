using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.DTOs
{
    // Step 2 of the wizard (after Exam + Language) -- Year/PaperCode together with the already chosen
    // Exam/Language make up the paper's identity. See PapersController.Create for the duplicate-check
    // behavior this feeds into.
    public class PaperCreateDto
    {
        [Required]
        public Guid ExamId { get; set; }

        [Required]
        public int Year { get; set; }

        [Required]
        public PaperLanguage Language { get; set; }

        // Optional. Only matters when the same Exam+Year+Language+Tier+Date+Shift+PaperLabel has
        // multiple question Sets (Set A / Set B / ...) -- leave blank for the common case of one set.
        [MaxLength(50)]
        public string? PaperCode { get; set; }

        // ---------- Paper identity (see Models/Paper.cs) ----------
        // All optional -- an exam with no tiers/shifts just leaves these null.
        [MaxLength(50)]
        public string? Tier { get; set; }

        public DateOnly? ExamDate { get; set; }

        [MaxLength(50)]
        public string? Shift { get; set; }

        [MaxLength(100)]
        public string? PaperLabel { get; set; }
    }

    // PATCH /api/admin/papers/{id}/identity -- fixes a paper's Exam/Year/Medium/Tier/Date/Shift/
    // Code/Label after creation (Draft/PendingReview only, same as editing anything else about a
    // paper). Added alongside bulk paper-shell import (see BulkPaperImportController) so a row that
    // resolved to the wrong exam, or had a typo'd year, can be corrected in place instead of having
    // to delete and recreate the paper -- same shape and the same duplicate-check as PaperCreateDto/
    // Create.
    public class PaperIdentityUpdateDto
    {
        [Required]
        public Guid ExamId { get; set; }

        [Required]
        public int Year { get; set; }

        [Required]
        public PaperLanguage Language { get; set; }

        [MaxLength(50)]
        public string? PaperCode { get; set; }

        [MaxLength(50)]
        public string? Tier { get; set; }

        public DateOnly? ExamDate { get; set; }

        [MaxLength(50)]
        public string? Shift { get; set; }

        [MaxLength(100)]
        public string? PaperLabel { get; set; }
    }

    public class PaperResponseDto
    {
        public Guid Id { get; set; }
        public Guid ExamId { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public string? ExamLogoUrl { get; set; }
        public int Year { get; set; }
        public string Language { get; set; } = string.Empty;
        public string? PaperCode { get; set; }

        // ---------- Paper identity ----------
        public string? Tier { get; set; }
        public DateOnly? ExamDate { get; set; }
        public string? Shift { get; set; }
        public string? PaperLabel { get; set; }

        public string Status { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }
        public int QuestionCount { get; set; }
        public Guid CreatedByAdminId { get; set; }
        public string CreatedByAdminName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? PublishedAt { get; set; }

        // ---------- Previous Year Paper Practice ----------
        public int? DurationMinutes { get; set; }
        public decimal? NegativeMarkingRatio { get; set; }
        public int? RequiredQuestionCount { get; set; }

        // QuestionCount above already counts BOTH sources (Question.PaperId + QuestionBankLinks --
        // see PapersController.MapToDto), so this is just "is it ready to attempt": true when
        // RequiredQuestionCount isn't set (integrity not enforced) or QuestionCount has reached it.
        public bool IsComplete { get; set; }

        // True once DurationMinutes AND NegativeMarkingRatio AND RequiredQuestionCount are all set --
        // i.e. an admin has actually configured this paper for Previous Year Paper Practice, as
        // opposed to a legacy paper that only supports the older "browse its questions" flow.
        public bool IsConfiguredForPractice { get; set; }

        // Whether the current viewer has this paper bookmarked (false when not logged in).
        public bool IsBookmarked { get; set; }

        // Distinct students who've submitted an attempt on this paper (Submitted/AutoSubmitted --
        // not counting ones still InProgress or abandoned/Expired without submitting). Powers the
        // "Attempted by X students" line the mobile reference UI shows on every paper card.
        public int AttemptCount { get; set; }
    }

    public class PaperRejectDto
    {
        [Required, MinLength(3)]
        public string Reason { get; set; } = string.Empty;
    }

    // PATCH /api/admin/papers/{id}/config -- sets up a paper (new or previously-legacy) for
    // Previous Year Paper Practice. Separate from PaperCreateDto/Create because these settings can
    // be adjusted any time a paper is editable (Draft/PendingReview), not just at creation.
    public class PaperConfigUpdateDto
    {
        [Range(1, 600)]
        public int? DurationMinutes { get; set; }

        [Range(0, 5)]
        public decimal? NegativeMarkingRatio { get; set; }

        [Range(1, 1000)]
        public int? RequiredQuestionCount { get; set; }
    }

    // POST /api/admin/papers/{id}/map-question -- map an EXISTING Question Bank question onto this
    // paper at a given question number. Never creates a new question (spec section 12).
    public class PaperQuestionMapDto
    {
        [Required]
        public Guid QuestionBankQuestionId { get; set; }

        [Required, Range(1, 1000)]
        public int QuestionNumber { get; set; }
    }

    // One row of the merged "this paper's full question list" view -- tags each question with
    // where it actually lives (spec section 10, "Question Source Transparency") without ever
    // exposing that distinction to students.
    public class PaperMappedQuestionDto
    {
        public int QuestionNumber { get; set; }

        // "PyqUpload" (Question.PaperId) or "QuestionBank" (PaperQuestionBankLink).
        public string Source { get; set; } = string.Empty;

        // The underlying Question.Id or QuestionBankQuestion.Id, whichever applies.
        public Guid QuestionId { get; set; }

        // Only set for a QuestionBank-sourced row -- lets the admin UI unmap it via
        // DELETE /api/admin/papers/{id}/map-question/{linkId}. PyqUpload-sourced rows are removed
        // via the existing DELETE /api/admin/questions/{id} instead (they're a real Question row).
        public Guid? LinkId { get; set; }

        public string QuestionText { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;

        // Always true for a PyqUpload row (admin typed the real Q.No). For a QuestionBank row, false
        // when it was bulk-added (see PapersController.MapQuestionsBulk) -- lets the admin UI show an
        // "approximate" badge instead of implying every Q.No on the page is the real exam layout.
        public bool IsNumberExact { get; set; } = true;
    }

    // POST /api/admin/papers/{id}/map-questions-bulk -- add many existing Question Bank questions to
    // this paper in one call, e.g. "this Exam+Year already has 40 matching questions in the Question
    // Bank, add them all". Unlike PaperQuestionMapDto there's no per-question QuestionNumber -- there's
    // no reliable way to know each one's TRUE original position when adding many at once, so numbers
    // are auto-assigned sequentially and every resulting link is IsNumberExact = false.
    public class PaperQuestionBulkMapDto
    {
        [Required, MinLength(1)]
        public List<Guid> QuestionBankQuestionIds { get; set; } = new();
    }

    public class PaperBulkMapResultDto
    {
        public int AddedCount { get; set; }

        // Requested IDs that were skipped because they're already mapped to this paper.
        public List<Guid> SkippedAlreadyMapped { get; set; } = new();

        // Requested IDs that don't exist or aren't active.
        public List<Guid> SkippedInvalid { get; set; } = new();

        // The (approximate, auto-assigned) Q.No range the newly-added questions landed on, e.g. 71-100.
        public int StartQuestionNumber { get; set; }
        public int EndQuestionNumber { get; set; }
    }

    // GET /api/admin/papers/{id}/validate -- spec section 14, "Question Number Validation".
    public class PaperValidationDto
    {
        public int RequiredQuestionCount { get; set; }
        public int ActualQuestionCount { get; set; }
        public int MissingCount { get; set; }
        public List<int> MissingQuestionNumbers { get; set; } = new();
        public List<int> DuplicateQuestionNumbers { get; set; } = new();
        public bool IsReadyToPublish { get; set; }
        public string? Message { get; set; }

        // True when at least one mapped Question Bank question has an auto-assigned (not-exact) Q.No
        // -- see PaperQuestionBankLink.IsNumberExact. Drives the admin UI's "Q.No aren't exact, student
        // will get a subject-grouped order instead" notice.
        public bool HasApproximateQuestionNumbers { get; set; }
    }

    // ============================================================================================
    // Student-facing browse (spec section 32, "Search and Filter") -- GET /api/papers
    // ============================================================================================

    // GET /api/papers/filter-options?examId=&year= -- which of Tier/Shift/ExamDate/PaperLabel/
    // Language actually have more than one value for this exam (or overall, if no exam/year picked
    // yet), so the frontend only shows a dropdown for a filter that's actually meaningful instead of
    // forcing every exam through the same rigid Tier/Date/Shift fields (spec section 4).
    public class PaperFilterOptionsDto
    {
        public List<string> Tiers { get; set; } = new();
        public List<DateOnly> ExamDates { get; set; } = new();
        public List<string> Shifts { get; set; } = new();
        public List<string> PaperLabels { get; set; } = new();
        public List<string> Languages { get; set; } = new();
    }

    // GET /api/papers/my-attempts -- backs the "Continue Attempting" / "Completed Papers" tabs.
    // Deliberately a separate, paper-flavoured DTO from TestAttemptsController.MyTestAttemptSummaryDto
    // (which covers Mock/Practice/Paper generically for a general "my tests" history) because this
    // page's cards need paper-specific metadata (Tier/Shift/questions-answered progress) that a Mock
    // or Practice attempt doesn't have.
    public class MyPaperAttemptDto
    {
        public Guid AttemptId { get; set; }
        public Guid PaperId { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public int Year { get; set; }
        public string? Tier { get; set; }
        public string? Shift { get; set; }
        public DateOnly? ExamDate { get; set; }
        public int TotalQuestions { get; set; }
        public int AnsweredCount { get; set; }
        public int? DurationMinutes { get; set; }
        public string Status { get; set; } = string.Empty; // "InProgress" | "Submitted" | "AutoSubmitted"
        public decimal? Score { get; set; } // null while InProgress
        public DateTime LastActivityAt { get; set; }
        public bool CanResume { get; set; }
    }
}
