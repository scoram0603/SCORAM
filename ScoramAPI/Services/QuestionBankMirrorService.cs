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
        private readonly IFileStorageService _fileStorage;
        private readonly ILogger<QuestionBankMirrorService> _logger;

        public QuestionBankMirrorService(IQuestionBankImportService importService, IFileStorageService fileStorage, ILogger<QuestionBankMirrorService> logger)
        {
            _importService = importService;
            _fileStorage = fileStorage;
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

                // Independent copies, not shared URLs -- see IFileStorageService.CopyImageAsync's own
                // comment on why. Any of these can be null (question has no image there); CopyImageAsync
                // returns null for a null/non-local input, so this stays simple either way.
                var questionImageUrl = await _fileStorage.CopyImageAsync(question.QuestionImageUrl, "question-images");
                var optionAImageUrl = await _fileStorage.CopyImageAsync(question.OptionAImageUrl, "question-images");
                var optionBImageUrl = await _fileStorage.CopyImageAsync(question.OptionBImageUrl, "question-images");
                var optionCImageUrl = await _fileStorage.CopyImageAsync(question.OptionCImageUrl, "question-images");
                var optionDImageUrl = await _fileStorage.CopyImageAsync(question.OptionDImageUrl, "question-images");
                var explanationImageUrl = await _fileStorage.CopyImageAsync(question.ExplanationImageUrl, "question-images");

                var mirror = new QuestionBankQuestion
                {
                    QuestionText = question.QuestionText,
                    NormalizedQuestionText = normalized,
                    QuestionImageUrl = questionImageUrl,
                    OptionA = question.OptionA,
                    OptionAImageUrl = optionAImageUrl,
                    OptionB = question.OptionB,
                    OptionBImageUrl = optionBImageUrl,
                    OptionC = question.OptionC,
                    OptionCImageUrl = optionCImageUrl,
                    OptionD = question.OptionD,
                    OptionDImageUrl = optionDImageUrl,
                    CorrectOption = question.CorrectOption,
                    DifficultyLevel = question.DifficultyLevel,
                    Explanation = question.Explanation,
                    ExplanationImageUrl = explanationImageUrl,
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

                // Re-copy each image fresh (independent files, not shared URLs -- see
                // MirrorFromPyqAsync's own comment) only when the source side actually changed;
                // otherwise leave the mirror's existing copy alone. Comparing by URL is enough here --
                // ApplyImageUpdate on the Question side already gives a brand-new URL for any actual
                // re-upload, so "same URL" reliably means "nothing changed here".
                mirror.QuestionImageUrl = await ResyncImageAsync(question.QuestionImageUrl, mirror.QuestionImageUrl);
                mirror.OptionAImageUrl = await ResyncImageAsync(question.OptionAImageUrl, mirror.OptionAImageUrl);
                mirror.OptionBImageUrl = await ResyncImageAsync(question.OptionBImageUrl, mirror.OptionBImageUrl);
                mirror.OptionCImageUrl = await ResyncImageAsync(question.OptionCImageUrl, mirror.OptionCImageUrl);
                mirror.OptionDImageUrl = await ResyncImageAsync(question.OptionDImageUrl, mirror.OptionDImageUrl);
                mirror.ExplanationImageUrl = await ResyncImageAsync(question.ExplanationImageUrl, mirror.ExplanationImageUrl);

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

        // sourceUrl: the (already-saved) current image URL on the Question side. mirrorUrl: what the
        // mirror currently points to (its own independent file, or null). This reliably handles the
        // two cases that matter most -- an image ADDED where there wasn't one, and one REMOVED
        // entirely -- by copying in or clearing accordingly. It does NOT detect a same-slot REPLACE
        // (a brand-new image uploaded over an existing one): since the mirror deliberately doesn't
        // share the source's URL, there's no cheap way to tell "still the same picture" from "swapped
        // for a different one" without tracking extra state. In that narrower case the mirror keeps
        // its existing image until an admin updates it directly via the Question Bank's own image
        // endpoint -- an acceptable gap given the mirror is meant to seed independently-curated
        // Question Bank content, not stay perfectly mirrored forever.
        private async Task<string?> ResyncImageAsync(string? sourceUrl, string? mirrorUrl)
        {
            if (sourceUrl == null) return null; // removed on the source side -- clear the mirror's copy too
            if (mirrorUrl != null) return mirrorUrl; // mirror already has ITS OWN copy -- leave it as-is
            return await _fileStorage.CopyImageAsync(sourceUrl, "question-images"); // source has one, mirror doesn't yet -- copy it in
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
