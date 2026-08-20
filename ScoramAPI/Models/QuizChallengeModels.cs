namespace ScoramAPI.Models
{
    // Deliberately NOT "Completed" or "Accepted" -- both are fully derivable from
    // ChallengedAttemptId/ChallengedAttempt.Status (null = not started yet, InProgress = accepted
    // but not finished, Submitted/AutoSubmitted/Expired = done) without a second field that could
    // drift out of sync with the attempt it's describing. This enum only covers the states that
    // AREN'T derivable from the attempt -- the challenge never got a response at all.
    public enum QuizChallengeStatus
    {
        Pending,
        Declined,
        Expired
    }

    // Phase 3 of the "Quizzes" feature (see TestKind.Quiz and Models/QuizModels.cs for Phases 1/2).
    // A challenge freezes a FRIEND'S exact question set (same QuestionBankQuestionIds, same order)
    // by pointing at one of their own completed Quiz attempts and letting the challenged student take
    // an identical copy of it -- built from SourceAttempt.Answers when they start (see
    // QuizChallengesController.Start), not from a separately-duplicated question list here. This
    // works whether the source was a Phase 1 Weak Topics Quiz (personalized, no two students would
    // ever get the same one naturally) or a Phase 2 Daily Quiz (already shared, but a challenge still
    // makes the head-to-head comparison explicit and social).
    public class QuizChallenge
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Set when this challenge was created alongside others in one "challenge N friends / a
        // group" action (see QuizChallengesController.Create) -- lets the UI show them together
        // ("You challenged 5 friends: 2 finished so far") without changing the fundamentally 1v1
        // shape of everything else on this row. Null for... nothing, actually -- even a single-friend
        // challenge gets a BatchId of its own (a batch of one), so callers never have to special-case
        // "was this sent alone or as part of a group?".
        public Guid BatchId { get; set; }

        public Guid ChallengerUserId { get; set; }
        public User? ChallengerUser { get; set; }

        public Guid ChallengedUserId { get; set; }
        public User? ChallengedUser { get; set; }

        // The challenger's own completed attempt this challenge is based on. Must be
        // TestKind.Quiz and not still InProgress at the time the challenge is created (see
        // QuizChallengesController.Create) -- there's no "correct answer" for a challenge based on
        // an attempt the challenger themselves hasn't actually finished yet.
        public Guid SourceAttemptId { get; set; }
        public StudentTestResult? SourceAttempt { get; set; }

        // Set once the challenged student starts their own attempt at the frozen question set.
        public Guid? ChallengedAttemptId { get; set; }
        public StudentTestResult? ChallengedAttempt { get; set; }

        public QuizChallengeStatus Status { get; set; } = QuizChallengeStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // A week to respond feels right for a casual, daily-quiz-adjacent feature -- long enough not
        // to feel rushed, short enough that a stale challenge doesn't linger in someone's inbox
        // forever. Checked and applied lazily on read (see QuizChallengesController.ExpireIfNeeded)
        // rather than needing a background job.
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);
    }
}
