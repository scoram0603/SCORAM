using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    // A question reference resolved down to just what BuildSnapshotAnswersAsync needs -- kept
    // separate from the DTO layer so this service doesn't depend on ScoramAPI.DTOs.
    public record QuestionRef(Guid? QuestionId, Guid? QuestionBankQuestionId, int Order);

    // One subject's accuracy across a student's own graded history -- used to pick which subjects a
    // Weak Topics Quiz should draw from. Kept here rather than in ScoramAPI.DTOs for the same reason
    // as QuestionRef above.
    public record WeakSubjectStat(string Subject, int Attempts, int Correct)
    {
        public double Accuracy => Attempts == 0 ? 0 : (double)Correct / Attempts;
    }

    public interface ITestAttemptService
    {
        /// <summary>Loads the live question data for each ref (batched -- one query against Questions,
        /// one against QuestionBankQuestions, not one query per question) and builds unsaved
        /// StudentAnswer rows carrying a frozen snapshot of that question's text/options/answer/
        /// explanation. Refs whose target question no longer exists (deleted) are silently skipped.</summary>
        Task<List<StudentAnswer>> BuildSnapshotAnswersAsync(ScoramDbContext db, IEnumerable<QuestionRef> refs);

        /// <summary>Assembles an eligible Question Bank pool for a Practice Test generation request
        /// and returns `count` of them as QuestionRefs, softly avoiding questions this user answered
        /// in their last few Practice attempts when the pool is large enough to do so (spec: "avoid
        /// unnecessary repetition... prefer other eligible questions when enough questions are
        /// available"). Returns fewer than `count` if the pool genuinely doesn't have enough.</summary>
        Task<List<QuestionRef>> SelectPracticeQuestionsAsync(
            ScoramDbContext db, Guid userId, Guid? subjectId, Guid? topicId, Guid? examId,
            int? yearFrom, int? yearTo, DifficultyLevel? difficulty, int count, PaperLanguage? language = null);

        /// <summary>Ranks the subjects behind a student's own graded, wrong-answer-containing history
        /// (across every TestKind, most recent first) from weakest accuracy to strongest, ignoring any
        /// subject with too few answers to mean anything yet. Empty for a student with no graded
        /// history at all.</summary>
        Task<List<WeakSubjectStat>> GetWeakSubjectsAsync(ScoramDbContext db, Guid userId);

        /// <summary>Same idea as SelectPracticeQuestionsAsync, but the "subject" filter isn't chosen by
        /// the student -- it's the 2-3 weakest subjects from GetWeakSubjectsAsync. Falls back to a
        /// general mixed pool (every active Question Bank question) when there's no weak-subject
        /// signal yet, so a brand-new student still gets a quiz instead of an error.</summary>
        Task<List<QuestionRef>> SelectWeakTopicQuestionsAsync(ScoramDbContext db, Guid userId, int count);
    }

    public class TestAttemptService : ITestAttemptService
    {
        // How many of the student's most recent Practice answers count as "recently attempted" for
        // the soft-avoidance rule above.
        private const int RecentPracticeAnswerLookback = 100;

        public async Task<List<StudentAnswer>> BuildSnapshotAnswersAsync(ScoramDbContext db, IEnumerable<QuestionRef> refs)
        {
            var refList = refs.ToList();
            var questionIds = refList.Where(r => r.QuestionId.HasValue).Select(r => r.QuestionId!.Value).ToList();
            var qbQuestionIds = refList.Where(r => r.QuestionBankQuestionId.HasValue).Select(r => r.QuestionBankQuestionId!.Value).ToList();

            var questions = questionIds.Count > 0
                ? await db.Questions.Where(q => questionIds.Contains(q.Id)).ToDictionaryAsync(q => q.Id)
                : new Dictionary<Guid, Question>();

            var qbQuestions = qbQuestionIds.Count > 0
                ? await db.QuestionBankQuestions.Include(q => q.Subject).Include(q => q.Topic)
                    .Where(q => qbQuestionIds.Contains(q.Id)).ToDictionaryAsync(q => q.Id)
                : new Dictionary<Guid, QuestionBankQuestion>();

            var answers = new List<StudentAnswer>();
            foreach (var r in refList)
            {
                if (r.QuestionId.HasValue && questions.TryGetValue(r.QuestionId.Value, out var q))
                {
                    answers.Add(new StudentAnswer
                    {
                        QuestionId = q.Id,
                        QuestionOrder = r.Order,
                        QuestionTextSnapshot = q.QuestionText,
                        OptionASnapshot = q.OptionA,
                        OptionBSnapshot = q.OptionB,
                        OptionCSnapshot = q.OptionC,
                        OptionDSnapshot = q.OptionD,
                        CorrectOptionSnapshot = q.CorrectOption,
                        ExplanationSnapshot = q.Explanation,
                        SubjectSnapshot = q.Subject,
                        TopicSnapshot = q.Topic,
                        QuestionImageUrlSnapshot = q.QuestionImageUrl,
                        OptionAImageUrlSnapshot = q.OptionAImageUrl,
                        OptionBImageUrlSnapshot = q.OptionBImageUrl,
                        OptionCImageUrlSnapshot = q.OptionCImageUrl,
                        OptionDImageUrlSnapshot = q.OptionDImageUrl,
                        ExplanationImageUrlSnapshot = q.ExplanationImageUrl,
                        ContentBlocksJsonSnapshot = q.ContentBlocksJson
                    });
                }
                else if (r.QuestionBankQuestionId.HasValue && qbQuestions.TryGetValue(r.QuestionBankQuestionId.Value, out var qb))
                {
                    answers.Add(new StudentAnswer
                    {
                        QuestionBankQuestionId = qb.Id,
                        QuestionOrder = r.Order,
                        QuestionTextSnapshot = qb.QuestionText,
                        OptionASnapshot = qb.OptionA,
                        OptionBSnapshot = qb.OptionB,
                        OptionCSnapshot = qb.OptionC,
                        OptionDSnapshot = qb.OptionD,
                        CorrectOptionSnapshot = qb.CorrectOption,
                        ExplanationSnapshot = qb.Explanation,
                        SubjectSnapshot = qb.Subject?.Name,
                        TopicSnapshot = qb.Topic?.Name,
                        QuestionImageUrlSnapshot = qb.QuestionImageUrl,
                        OptionAImageUrlSnapshot = qb.OptionAImageUrl,
                        OptionBImageUrlSnapshot = qb.OptionBImageUrl,
                        OptionCImageUrlSnapshot = qb.OptionCImageUrl,
                        OptionDImageUrlSnapshot = qb.OptionDImageUrl,
                        ExplanationImageUrlSnapshot = qb.ExplanationImageUrl,
                        ContentBlocksJsonSnapshot = qb.ContentBlocksJson
                    });
                }
                // else: the referenced question no longer exists -- skipped rather than throwing, so
                // one stale reference in an admin-curated template doesn't take down the whole test.
            }

            return answers;
        }

        public async Task<List<QuestionRef>> SelectPracticeQuestionsAsync(
            ScoramDbContext db, Guid userId, Guid? subjectId, Guid? topicId, Guid? examId,
            int? yearFrom, int? yearTo, DifficultyLevel? difficulty, int count, PaperLanguage? language = null)
        {
            // Question Bank is the pool for Practice Tests (spec's own architecture diagram: Question
            // Bank -> Practice Tests) -- legacy PYQ Papers aren't included here, since those questions
            // are already reachable through the Question Bank once imported/added there.
            var pool = db.QuestionBankQuestions.Where(q => q.IsActive).AsQueryable();

            if (subjectId.HasValue) pool = pool.Where(q => q.SubjectId == subjectId);
            if (topicId.HasValue) pool = pool.Where(q => q.TopicId == topicId);
            if (difficulty.HasValue) pool = pool.Where(q => q.DifficultyLevel == difficulty);
            if (examId.HasValue) pool = pool.Where(q => q.ExamMappings.Any(m => m.ExamId == examId));
            if (yearFrom.HasValue) pool = pool.Where(q => q.ExamMappings.Any(m => m.Year >= yearFrom));
            if (yearTo.HasValue) pool = pool.Where(q => q.ExamMappings.Any(m => m.Year <= yearTo));
            // A question with no medium tagged (Language == null) always stays eligible regardless of
            // this filter -- same "untagged = shows for everyone" rule as Mock Test/Quiz/Paper.
            if (language.HasValue) pool = pool.Where(q => q.Language == null || q.Language == language);

            var eligibleIds = await pool.Select(q => q.Id).ToListAsync();
            if (eligibleIds.Count == 0) return new List<QuestionRef>();

            var recentlySeenIds = await db.StudentAnswers
                .Where(a => a.QuestionBankQuestionId != null
                    && a.StudentTestResult!.UserId == userId
                    && a.StudentTestResult!.TestKind == TestKind.Practice)
                .OrderByDescending(a => a.StudentTestResult!.StartedAt)
                .Take(RecentPracticeAnswerLookback)
                .Select(a => a.QuestionBankQuestionId!.Value)
                .ToListAsync();
            var recentlySeenSet = recentlySeenIds.ToHashSet();

            var preferred = eligibleIds.Where(id => !recentlySeenSet.Contains(id)).ToList();
            var pickFrom = preferred.Count >= count ? preferred : eligibleIds;

            var random = Random.Shared;
            var selected = pickFrom.OrderBy(_ => random.Next()).Take(count).ToList();

            return selected.Select((id, i) => new QuestionRef(null, id, i + 1)).ToList();
        }

        // How many of a student's most recent graded answers (any TestKind) are looked at to compute
        // weak subjects / avoid recently-seen questions for a Weak Topics Quiz.
        private const int WeakTopicAnswerLookback = 300;
        // A subject needs at least this many answered questions in that lookback window before its
        // accuracy is trusted as a real signal -- otherwise one unlucky wrong answer in a subject the
        // student's barely touched would misleadingly mark it "weak".
        private const int MinAnswersForWeakSubjectSignal = 3;
        // How many of the weakest subjects a quiz draws from -- enough to keep the quiz from being
        // repetitive, not so many it dilutes back into "basically a random mixed quiz".
        private const int WeakSubjectsPerQuiz = 3;

        public async Task<List<WeakSubjectStat>> GetWeakSubjectsAsync(ScoramDbContext db, Guid userId)
        {
            // Only graded answers count -- an InProgress attempt's unanswered questions are stored
            // with IsCorrect = false (see StudentAnswer/GradeAttempt), which would wrongly drag down
            // a subject's accuracy for a paper the student hasn't even finished yet.
            var rows = await db.StudentAnswers
                .Where(a => a.QuestionBankQuestionId != null
                    && a.SelectedOption != null
                    && a.SubjectSnapshot != null
                    && a.StudentTestResult!.UserId == userId
                    && a.StudentTestResult!.Status != TestAttemptStatus.InProgress)
                .OrderByDescending(a => a.StudentTestResult!.AttemptedAt)
                .Take(WeakTopicAnswerLookback)
                .Select(a => new { Subject = a.SubjectSnapshot!, a.IsCorrect })
                .ToListAsync();

            return rows
                .GroupBy(a => a.Subject)
                .Select(g => new WeakSubjectStat(g.Key, g.Count(), g.Count(a => a.IsCorrect)))
                .Where(s => s.Attempts >= MinAnswersForWeakSubjectSignal)
                .OrderBy(s => s.Accuracy)
                .ToList();
        }

        public async Task<List<QuestionRef>> SelectWeakTopicQuestionsAsync(ScoramDbContext db, Guid userId, int count)
        {
            var weakSubjects = await GetWeakSubjectsAsync(db, userId);
            var targetSubjects = weakSubjects.Take(WeakSubjectsPerQuiz).Select(s => s.Subject).ToHashSet();

            var pool = db.QuestionBankQuestions.Where(q => q.IsActive).Include(q => q.Subject).AsQueryable();
            if (targetSubjects.Count > 0)
                pool = pool.Where(q => q.Subject != null && targetSubjects.Contains(q.Subject.Name));

            var eligibleIds = await pool.Select(q => q.Id).ToListAsync();

            // No weak-subject signal yet (brand-new student, or the subject names above genuinely
            // matched nothing) -- fall back to a general mixed pool instead of a dead end, same idea
            // as an ad-hoc Practice Test generated with no filters at all.
            if (eligibleIds.Count == 0)
                eligibleIds = await db.QuestionBankQuestions.Where(q => q.IsActive).Select(q => q.Id).ToListAsync();
            if (eligibleIds.Count == 0) return new List<QuestionRef>();

            var recentlySeenIds = await db.StudentAnswers
                .Where(a => a.QuestionBankQuestionId != null && a.StudentTestResult!.UserId == userId)
                .OrderByDescending(a => a.StudentTestResult!.StartedAt)
                .Take(WeakTopicAnswerLookback)
                .Select(a => a.QuestionBankQuestionId!.Value)
                .ToListAsync();
            var recentlySeenSet = recentlySeenIds.ToHashSet();

            var preferred = eligibleIds.Where(id => !recentlySeenSet.Contains(id)).ToList();
            var pickFrom = preferred.Count >= count ? preferred : eligibleIds;

            var random = Random.Shared;
            var selected = pickFrom.OrderBy(_ => random.Next()).Take(count).ToList();

            return selected.Select((id, i) => new QuestionRef(null, id, i + 1)).ToList();
        }
    }
}
