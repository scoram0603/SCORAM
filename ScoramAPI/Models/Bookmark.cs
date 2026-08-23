namespace ScoramAPI.Models
{
    // A student's saved-for-later item. Same multi-nullable-FK reuse pattern as QuestionComment/
    // QuestionVote elsewhere in this codebase -- one table serves every bookmarkable content type
    // instead of five parallel BookmarkedQuestion/BookmarkedPaper/... tables. Exactly ONE of the
    // five target columns below is ever set per row (enforced at the application layer in
    // BookmarksController, same as the QuestionId/QuestionBankQuestionId convention already used
    // throughout Models/QuestionModels.cs).
    public class Bookmark
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        // Legacy Paper-based question.
        public Guid? QuestionId { get; set; }
        public Question? Question { get; set; }

        // Question Bank question (also what a Previous Year Paper's QuestionBankLinks point at).
        public Guid? QuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? QuestionBankQuestion { get; set; }

        // A whole discussion thread -- always a TOP-LEVEL QuestionComment (ParentCommentId == null),
        // never a single reply, mirroring how DiscussionsController's own feed only ever surfaces
        // top-level comments as "threads".
        public Guid? CommentId { get; set; }
        public QuestionComment? Comment { get; set; }

        // A full PYQ paper (Previous Year Paper Practice).
        public Guid? PaperId { get; set; }
        public Paper? Paper { get; set; }

        // A mock test.
        public Guid? MockTestId { get; set; }
        public MockTest? MockTest { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
