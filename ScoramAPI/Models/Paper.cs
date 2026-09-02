using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    // A "Paper" is the real-world PYQ paper being uploaded (e.g. SSC CGL 2022, Hindi). Individual
    // Questions belong to a Paper (Question.PaperId) rather than carrying their own exam/year/language
    // -- that metadata now lives here, once per paper, instead of being repeated (and potentially
    // inconsistent) on every single question row.
    public class Paper
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ExamId { get; set; }
        public Exam? Exam { get; set; }

        public int Year { get; set; }

        public PaperLanguage Language { get; set; }

        // Optional. When set, it's part of what makes a paper unique -- this is what allows the same
        // Exam+Year+Language to be uploaded more than once as separate question "Sets" (Set A / Set B /
        // Set C...), which real exams commonly have.
        [MaxLength(50)]
        public string? PaperCode { get; set; }

        // ---------- Paper identity (Tier / Date / Shift / Paper-label) ----------
        // Previously these all lived squeezed into the single free-text PaperCode above (e.g.
        // "Tier 1 - 26 Sep - Shift 1"). That's fine for display but can't be filtered as separate
        // dropdowns on the student "Previous Year Paper Practice" page, so they're broken out into
        // their own columns here. All are optional/nullable -- an exam with no tiers or shifts (most
        // don't) simply leaves these null, matching spec section 4's "don't force irrelevant filters
        // on every exam". PaperCode is kept too, still available as a free-text differentiator for
        // whatever doesn't fit these four (Set A/B/C, booklet series, etc.).
        [MaxLength(50)]
        public string? Tier { get; set; }

        public DateOnly? ExamDate { get; set; }

        [MaxLength(50)]
        public string? Shift { get; set; }

        // Which paper/section within a shift, for exams that split a shift into separate papers by
        // subject (e.g. "General Awareness", "Quantitative Aptitude") -- distinct from Question.Subject,
        // which tags individual questions; this tags the WHOLE paper when the exam itself is structured
        // that way.
        [MaxLength(100)]
        public string? PaperLabel { get; set; }

        public PaperStatus Status { get; set; } = PaperStatus.Draft;

        // Set when a Super Admin (or a Publish-permission admin) sends a PendingReview paper back to
        // Draft instead of approving it -- shown to the uploading admin so they know what to fix.
        public string? RejectionReason { get; set; }

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PublishedAt { get; set; }

        // True when the Exam this paper belongs to had NO other content (no other Papers/Questions/
        // Question Bank mappings/Tests/chat activity) at the exact moment this paper was created --
        // stamped once in PapersController.Create via ExamsController.ExamHasContentAsync, never
        // changed afterward. Lets BulkImportController.Rollback safely offer "this exam has nothing
        // else on it -- delete it too?" after undoing a bad bulk upload, without any fragile
        // timestamp-based guessing about whether the exam was "just created for this".
        public bool ExamCreatedForThisPaper { get; set; } = false;

        // ---------- Previous Year Paper Practice ----------
        // Added so a Paper can be ATTEMPTED as a real timed paper (previously a Paper only powered
        // "browse its questions" -- there was no attempt/timer/scoring concept at all). All nullable
        // so every existing Paper row keeps working unchanged: a paper with no DurationMinutes/
        // RequiredQuestionCount simply isn't attemptable yet (see StudentPapersController.Start),
        // it can still be browsed via the existing Questions endpoints exactly as before.

        // Null = not attemptable as a full paper yet (admin hasn't configured it for practice mode).
        public int? DurationMinutes { get; set; }

        // Null is treated as 0 (no negative marking) when an attempt is generated -- see
        // StudentPapersController.Start. Kept nullable rather than defaulted so "not configured yet"
        // is distinguishable from "configured, explicitly zero" if that's ever needed later.
        public decimal? NegativeMarkingRatio { get; set; }

        // Expected total question count for this paper (section 8, "exact paper integrity" of the
        // Previous Year Paper Practice spec). Null = integrity isn't enforced (legacy/browse-only
        // papers). When set, Publish/Submit and Start both refuse to proceed until
        // Questions.Count + QuestionBankLinks.Count reaches this number.
        public int? RequiredQuestionCount { get; set; }

        public ICollection<Question> Questions { get; set; } = new List<Question>();

        // Existing Question Bank questions mapped onto this paper (in addition to Questions above,
        // which come from the separate legacy PYQ-upload flow) -- see Models/PaperQuestionBankLink.cs.
        // Together, Questions ∪ QuestionBankLinks (sorted by QuestionNumber) IS the paper a student
        // attempts; neither list is ever copied into the other.
        public ICollection<PaperQuestionBankLink> QuestionBankLinks { get; set; } = new List<PaperQuestionBankLink>();
    }
}
