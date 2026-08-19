using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    // MASTER PROMPT — Previous Year Paper Practice: lets an existing QuestionBankQuestion be reused
    // inside a Paper without duplicating it, exactly the same dual-source idea as MockTestQuestion /
    // PracticeTestTemplateQuestion (see Models/MockTestModels.cs, Models/PracticeTestModels.cs) --
    // this is the same pattern applied to Paper instead of MockTest.
    //
    // A Paper's full, ordered question list is therefore:
    //   Questions (Question.PaperId, the existing PYQ-upload flow)  ∪  QuestionBankLinks (this table)
    // sorted by QuestionNumber. Neither side is ever copied into the other -- see
    // StudentPapersController.Start / PapersController.GetMappedQuestions for where they're merged.
    public class PaperQuestionBankLink
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid PaperId { get; set; }
        public Paper? Paper { get; set; }

        public Guid QuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? QuestionBankQuestion { get; set; }

        // This question's position in the ORIGINAL paper (Q.45, etc.) -- mirrors
        // Question.QuestionNumber exactly, so the two sources interleave correctly when sorted
        // together. Uniqueness within a paper (both against other links AND against Question's own
        // QuestionNumber for the same PaperId) is enforced at the application layer in
        // PapersController, not the database, because it spans two different tables -- same
        // reasoning as the existing PaperCode duplicate check.
        public int QuestionNumber { get; set; }

        // True when an admin picked this exact QuestionNumber knowing it's the question's real
        // position in the original paper (single map-question, or manually corrected afterwards).
        // False when it was auto-assigned as "next free slot" by a bulk add (see
        // PapersController.MapQuestionsBulk) -- bulk-adding N questions at once has no way to know
        // each one's true original position, so the number is only there to satisfy uniqueness, not
        // to claim accuracy. StudentPapersController.Start checks this: if ANY link on a paper is
        // approximate, the attempt falls back to a subject-grouped order instead of presenting the
        // merged QuestionNumber sequence as if it were the real exam layout (spec section 9 only
        // requires exact order "wherever that information exists").
        public bool IsNumberExact { get; set; } = true;

        public Guid LinkedByAdminId { get; set; }
        public Admin? LinkedByAdmin { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
