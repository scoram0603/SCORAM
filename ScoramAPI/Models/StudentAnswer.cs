using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    /// <summary>
    /// One student's answer for one question within one test attempt (Practice or Mock -- see
    /// StudentTestResult.TestKind). This now ALSO carries a point-in-time snapshot of the question
    /// itself (SCORAM_TESTS), captured once when the attempt starts, so a later admin edit to the
    /// live Question/QuestionBankQuestion (text, options, correct answer, explanation) can never
    /// silently change what a past attempt shows or how it was scored. Report Question and
    /// Alternative Solution still operate on the LIVE question via QuestionId/QuestionBankQuestionId
    /// below -- only the historical DISPLAY is frozen, not those separate feedback features.
    /// </summary>
    public class StudentAnswer
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid StudentTestResultId { get; set; }
        public StudentTestResult? StudentTestResult { get; set; }

        // Nullable so a test question can come from either the legacy Question table or the
        // Question Bank -- same dual-FK pattern used throughout (Solutions, Reports, Comments,
        // Votes). Exactly one of these two is set. Kept even though the display is snapshotted
        // below, because Report Question / Alternative Solution / Comments / Like still need to
        // point at the real, current question.
        public Guid? QuestionId { get; set; }
        public Question? Question { get; set; }

        public Guid? QuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? QuestionBankQuestion { get; set; }

        // Position within this specific attempt. For a Mock Test this normally mirrors
        // MockTestQuestion.QuestionOrder (or a shuffled copy of it, per MockTest.IsRandomOrder);
        // for a Practice attempt there's no catalog ordering to mirror, so this IS the order.
        public int QuestionOrder { get; set; }

        // ---- Snapshot, captured once at attempt-start ----
        public string QuestionTextSnapshot { get; set; } = string.Empty;
        public string OptionASnapshot { get; set; } = string.Empty;
        public string OptionBSnapshot { get; set; } = string.Empty;
        public string OptionCSnapshot { get; set; } = string.Empty;
        public string OptionDSnapshot { get; set; } = string.Empty;
        public OptionLetter CorrectOptionSnapshot { get; set; }
        public string? ExplanationSnapshot { get; set; }
        public string? SubjectSnapshot { get; set; }
        public string? TopicSnapshot { get; set; }

        // Added alongside the rich PYP/PYQ upload feature -- same "frozen at attempt-start" contract
        // as every other *Snapshot field above (a later admin edit to the live question's image or
        // ContentBlocks can't retroactively change what a past attempt showed). Null for every
        // attempt started before this existed, and for any question that has no image/rich content
        // to begin with -- both render exactly as before (plain text, no image).
        public string? QuestionImageUrlSnapshot { get; set; }
        public string? OptionAImageUrlSnapshot { get; set; }
        public string? OptionBImageUrlSnapshot { get; set; }
        public string? OptionCImageUrlSnapshot { get; set; }
        public string? OptionDImageUrlSnapshot { get; set; }
        public string? ExplanationImageUrlSnapshot { get; set; }
        public string? ContentBlocksJsonSnapshot { get; set; }

        // Null means the student hasn't answered (yet, if InProgress) or skipped it (if submitted).
        public OptionLetter? SelectedOption { get; set; }

        public bool IsCorrect { get; set; }

        // "Mark for Review" (spec: Practice + Mock question interface) -- persisted so a resumed
        // attempt (auto-save) keeps this state instead of losing it on a dropped connection/reload.
        public bool IsMarkedForReview { get; set; } = false;

        // Null until the student actually selects an option for this question (auto-save writes
        // this each time); distinct from StudentTestResult.AttemptedAt, which is when the whole
        // attempt was submitted.
        public DateTime? AnsweredAt { get; set; }
    }
}

