using Microsoft.EntityFrameworkCore;
using ScoramAPI.Controllers;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    public interface IBulkPaperImportCommitService
    {
        // Throws InvalidOperationException (user-facing message) if the preview cache entry has
        // expired -- the only real failure mode here, since unlike BulkImportController there's no
        // paper-must-still-be-Draft check (this creates NEW papers, it doesn't write into an
        // existing one).
        Task<BulkPaperImportCommitResultDto> CommitAsync(Guid jobId, Guid adminId, List<int>? rowNumbers);
    }

    // Extracted from BulkPaperImportController.Commit -- see that method's own comment for why, and
    // BulkImportCommitService for the sibling this mirrors.
    public class BulkPaperImportCommitService : IBulkPaperImportCommitService
    {
        private const string CachePrefix = "bulk-paper-import-rows:";

        private readonly ScoramDbContext _db;
        private readonly IStagedDataCache _cache;
        private readonly IAuditLogService _audit;

        public BulkPaperImportCommitService(ScoramDbContext db, IStagedDataCache cache, IAuditLogService audit)
        {
            _db = db;
            _cache = cache;
            _audit = audit;
        }

        public async Task<BulkPaperImportCommitResultDto> CommitAsync(Guid jobId, Guid adminId, List<int>? rowNumbers)
        {
            var rows = await _cache.GetAsync<List<ImportedPaperRow>>(CachePrefix + jobId);
            if (rows == null)
                throw new InvalidOperationException("This preview has expired (previews last 30 minutes). Please re-upload the file.");

            var targetRows = rowNumbers == null
                ? rows.Where(r => r.IsValid && !r.PaperAlreadyExists).ToList()
                : rows.Where(r => rowNumbers.Contains(r.RowNumber)).ToList();

            var examCache = await _db.Exams.ToDictionaryAsync(e => e.Name, StringComparer.OrdinalIgnoreCase);

            // See the identical comment in the pre-extraction version of this method (now here) on
            // why this is needed on top of the live ExamHasContentAsync check below.
            var examsGivenAPaperThisBatch = new HashSet<Guid>();

            var created = new List<Paper>();
            var skippedExisting = 0;

            foreach (var row in targetRows)
            {
                if (!row.IsValid) continue;

                var exam = await ExamsController.GetOrCreateExamCachedAsync(_db, row.ExamName.Trim(), adminId, examCache);
                var language = Enum.Parse<PaperLanguage>(row.Medium, ignoreCase: true);

                var existing = await _db.Papers.FirstOrDefaultAsync(p =>
                    p.ExamId == exam.Id && p.Year == row.Year && p.Language == language &&
                    p.PaperCode == row.PaperCode && p.Tier == row.Tier &&
                    p.ExamDate == row.ExamDate && p.Shift == row.Shift && p.PaperLabel == row.PaperLabel);

                if (existing != null)
                {
                    skippedExisting++;
                    continue;
                }

                var examWasEmpty = !examsGivenAPaperThisBatch.Contains(exam.Id)
                    && !await ExamsController.ExamHasContentAsync(_db, exam.Id, exam.Name);
                examsGivenAPaperThisBatch.Add(exam.Id);

                var paper = new Paper
                {
                    ExamId = exam.Id,
                    Year = row.Year,
                    Language = language,
                    PaperCode = row.PaperCode,
                    Tier = row.Tier,
                    ExamDate = row.ExamDate,
                    Shift = row.Shift,
                    PaperLabel = row.PaperLabel,
                    Status = PaperStatus.Draft,
                    ExamCreatedForThisPaper = examWasEmpty,
                    CreatedByAdminId = adminId,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Papers.Add(paper);
                created.Add(paper);
            }

            await _db.SaveChangesAsync();
            await _cache.RemoveAsync(CachePrefix + jobId);

            await _audit.LogAsync(adminId, "BulkPaperImport.Commit", "Paper", null,
                $"{created.Count} paper shell(s) created, {skippedExisting} already existed and were skipped");

            var createdDtos = new List<PaperResponseDto>();
            foreach (var p in created)
            {
                await _db.Entry(p).Reference(x => x.Exam).LoadAsync();
                await _db.Entry(p).Reference(x => x.CreatedByAdmin).LoadAsync();
                createdDtos.Add(PapersController.MapToDto(p, questionCountOverride: 0));
            }

            return new BulkPaperImportCommitResultDto
            {
                Status = "Committed",
                CreatedCount = created.Count,
                SkippedExistingCount = skippedExisting,
                CreatedPapers = createdDtos
            };
        }
    }
}
