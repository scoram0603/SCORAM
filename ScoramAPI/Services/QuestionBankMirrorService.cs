using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    // Closes the gap where a PYQ question typed/imported directly onto a Paper (via
    // QuestionsController or BulkImportController) never showed up anywhere the Question Bank feeds
    // from -- Question Bank search, Practice Tests, Weak Topics Quiz, Daily Quiz, Discussions. From
    // now on every new PYQ question is ALSO mirrored into the Question Bank as a standalone entry,
    // tagged for the paper's own Exam+Year (so it immediately shows up as "already tagged" in the
    // PYP Paper Builder's bulk-add picker for OTHER papers of the same exam/year too).
    //
    // Deliberately NOT a hard link back into the Paper that created it (no PaperQuestionBankLink) --
    // the paper already has this exact question via its own legacy Question row; adding a link too
    // would double-count it in that paper's merged question list and clash on Q.No. The mirror exists
    // purely so the CONTENT is reusable elsewhere, while this paper keeps referencing the original
    // legacy row exactly as it always has.
    //
    // KNOWN LIMITATION: Question Bank has no image fields at all (see QuestionBankQuestion), so a PYQ
    // question with a diagram/image only carries its TEXT into the mirror -- the image stays on the
    // original Paper-side question. Fixing that would mean adding image support to the Question Bank
    // itself, which is a bigger, separate change than this gap-fix.
    public interface IQuestionBankMirrorService
    {
        /// <summary>Creates (or reuses, if an identical question already exists -- same duplicate
        /// check as QuestionBankAdminController.Create) a Question Bank entry for this PYQ question,
        /// tagged with the given Exam+Year. Never throws for "ordinary" problems (e.g. couldn't
        /// resolve a subject) -- returns null instead, since a failed mirror must never block saving
        /// the actual PYQ question. Caller is responsible for calling SaveChangesAsync.</summary>
        Task<Guid?> MirrorFromPyqAsync(ScoramDbContext db, Question question, Guid examId, int year, Guid adminId);

        /// <summary>Keeps an existing mirror's text/options/etc in sync after the source PYQ question
        /// is edited. No-op if the mirror was deleted independently in the Question Bank (that's a
        /// deliberate admin action elsewhere, not something an unrelated PYQ edit should undo).</summary>
        Task SyncMirrorAsync(ScoramDbContext db, Guid questionBankQuestionId, Question question);
    }

    public class QuestionBankMirrorService : IQuestionBankMirrorService
    {
        private readonly IQuestionBankImportService _importService;
        private readonly ILogger<QuestionBankMirrorService> _logger;

        public QuestionBankMirrorService(IQuestionBankImportService importService, ILogger<QuestionBankMirrorService> logger)
        {
            _importService = importService;
            _logger = logger;
        }

        public async Task<Guid?> MirrorFromPyqAsync(ScoramDbContext db, Question question, Guid examId, int year, Guid adminId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(question.Subject) || string.IsNullOrWhiteSpace(question.Topic))
                    return null; // nothing sensible to file it under -- skip rather than guess

                var normalized = _importService.NormalizeForDuplicateCheck(question.QuestionText);

                // Same duplicate rule as the Question Bank's own manual Create -- if this exact
                // question already exists there (e.g. an admin separately typed it into both places,
                // or it was mirrored from a different paper that happens to share it), reuse that row
                // and just make sure it's tagged for this Exam+Year too, instead of creating a
                // visible duplicate in Question Bank search results.
                var existing = await db.QuestionBankQuestions
                    .FirstOrDefaultAsync(q => q.IsActive && q.NormalizedQuestionText == normalized);
                if (existing != null)
                {
                    var alreadyTagged = await db.QuestionBankExamMappings
                        .AnyAsync(m => m.QuestionBankQuestionId == existing.Id && m.ExamId == examId && m.Year == year);
                    if (!alreadyTagged)
                    {
                        db.QuestionBankExamMappings.Add(new QuestionBankExamMapping
                        {
                            QuestionBankQuestionId = existing.Id,
                            ExamId = examId,
                            Year = year
                        });
                    }
                    return existing.Id;
                }

                var subject = await FindOrCreateSubjectAsync(db, question.Subject, adminId);
                var topic = await FindOrCreateTopicAsync(db, subject.Id, question.Topic, adminId);

                var mirror = new QuestionBankQuestion
                {
                    QuestionText = question.QuestionText,
                    NormalizedQuestionText = normalized,
                    OptionA = question.OptionA,
                    OptionB = question.OptionB,
                    OptionC = question.OptionC,
                    OptionD = question.OptionD,
                    CorrectOption = question.CorrectOption,
                    DifficultyLevel = question.DifficultyLevel,
                    Explanation = question.Explanation,
                    SubjectId = subject.Id,
                    TopicId = topic.Id,
                    SourceReference = question.SourceReference,
                    CreatedByAdminId = adminId,
                    CreatedAt = DateTime.UtcNow
                };
                db.QuestionBankQuestions.Add(mirror);
                db.QuestionBankExamMappings.Add(new QuestionBankExamMapping
                {
                    QuestionBankQuestionId = mirror.Id,
                    ExamId = examId,
                    Year = year
                });

                return mirror.Id;
            }
            catch (Exception ex)
            {
                // Best-effort by design -- see this service's own class-level comment. The PYQ
                // question itself has already (or is about to be) saved successfully regardless.
                _logger.LogWarning(ex, "Question Bank mirror failed for PYQ question {QuestionId}, continuing without it.", question.Id);
                return null;
            }
        }

        public async Task SyncMirrorAsync(ScoramDbContext db, Guid questionBankQuestionId, Question question)
        {
            try
            {
                var mirror = await db.QuestionBankQuestions.FindAsync(questionBankQuestionId);
                if (mirror == null) return; // deleted independently -- leave it alone, see interface comment

                mirror.QuestionText = question.QuestionText;
                mirror.NormalizedQuestionText = _importService.NormalizeForDuplicateCheck(question.QuestionText);
                mirror.OptionA = question.OptionA;
                mirror.OptionB = question.OptionB;
                mirror.OptionC = question.OptionC;
                mirror.OptionD = question.OptionD;
                mirror.CorrectOption = question.CorrectOption;
                mirror.DifficultyLevel = question.DifficultyLevel;
                mirror.Explanation = question.Explanation;
                mirror.SourceReference = question.SourceReference;
                mirror.UpdatedAt = DateTime.UtcNow;
                // Deliberately NOT touching Subject/Topic here -- an admin may have since moved the
                // mirror to a more specific Question Bank topic than the free-text PYQ fields ever
                // captured, and a routine text edit on the Paper side shouldn't silently undo that
                // curation.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Question Bank mirror sync failed for question {QuestionBankQuestionId}, continuing without it.", questionBankQuestionId);
            }
        }

        private static async Task<QuestionBankSubject> FindOrCreateSubjectAsync(ScoramDbContext db, string name, Guid adminId)
        {
            var trimmed = name.Trim();
            var existing = await db.QuestionBankSubjects
                .FirstOrDefaultAsync(s => s.IsActive && s.Name.ToLower() == trimmed.ToLower());
            if (existing != null) return existing;

            var subject = new QuestionBankSubject { Name = trimmed, CreatedByAdminId = adminId };
            db.QuestionBankSubjects.Add(subject);
            return subject;
        }

        private static async Task<QuestionBankTopic> FindOrCreateTopicAsync(ScoramDbContext db, Guid subjectId, string name, Guid adminId)
        {
            var trimmed = name.Trim();
            var existing = await db.QuestionBankTopics
                .FirstOrDefaultAsync(t => t.IsActive && t.SubjectId == subjectId && t.Name.ToLower() == trimmed.ToLower());
            if (existing != null) return existing;

            var topic = new QuestionBankTopic { SubjectId = subjectId, Name = trimmed, CreatedByAdminId = adminId };
            db.QuestionBankTopics.Add(topic);
            return topic;
        }
    }
}
