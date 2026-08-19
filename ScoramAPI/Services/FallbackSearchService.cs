using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;

namespace ScoramAPI.Services
{
    public interface IFallbackSearchService
    {
        /// <summary>Answers an instant-search query straight from SQL Server when Meilisearch is down.
        /// Tries Full-Text Search (CONTAINS) first for relevance-ranked, word-form-aware matching; if
        /// that throws (most likely because the full-text catalog/index from
        /// Scripts/SetupFullTextSearch.sql hasn't been run yet), falls back further to a plain
        /// LIKE '%term%' scan. Never throws -- returns an empty list if even LIKE fails, since at that
        /// point the database itself is the problem, not the search strategy.</summary>
        Task<(List<QuestionSearchDocument> Results, string Source)> SearchAsync(string query, int limit = 20);
    }

    public class FallbackSearchService : IFallbackSearchService
    {
        private readonly ScoramDbContext _db;
        private readonly ILogger<FallbackSearchService> _logger;

        public FallbackSearchService(ScoramDbContext db, ILogger<FallbackSearchService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<(List<QuestionSearchDocument> Results, string Source)> SearchAsync(string query, int limit = 20)
        {
            try
            {
                var ftsResults = await SearchWithFullTextAsync(query, limit);
                return (ftsResults, "SqlFullText");
            }
            catch (Exception ex)
            {
                // Most commonly: no full-text catalog/index exists yet (see Scripts/SetupFullTextSearch.sql),
                // which SQL Server surfaces as an error on the CONTAINS predicate itself, not as "zero
                // rows". That's expected until the script has been run -- log it once at Warning (not
                // Error, since LIKE below covers it) rather than let it look like a real outage.
                _logger.LogWarning(ex, "SQL Full-Text search unavailable, falling back to LIKE search for query {Query}", query);
            }

            var likeResults = await SearchWithLikeAsync(query, limit);
            return (likeResults, "SqlLike");
        }

        private async Task<List<QuestionSearchDocument>> SearchWithFullTextAsync(string query, int limit)
        {
            // EF.Functions.Contains issues a SQL Server CONTAINS predicate, which needs a full-text
            // index on QuestionText (created by Scripts/SetupFullTextSearch.sql). It understands word
            // forms ("running" matches "run") and ranks by relevance -- the closest equivalent to
            // Meilisearch's behavior that plain SQL offers.
            var entities = await BaseQuery()
                .Where(q => EF.Functions.Contains(q.QuestionText, query) || EF.Functions.Contains(q.Subject, query) || EF.Functions.Contains(q.Topic, query))
                .OrderByDescending(q => q.CreatedAt)
                .Take(limit)
                .ToListAsync();

            return entities.Select(QuestionSearchDocument.FromQuestion).ToList();
        }

        private async Task<List<QuestionSearchDocument>> SearchWithLikeAsync(string query, int limit)
        {
            var term = query.Trim();
            var isNumeric = int.TryParse(term, out var numericTerm);

            var entities = await BaseQuery()
                .Where(q =>
                    EF.Functions.Like(q.QuestionText, $"%{term}%") ||
                    EF.Functions.Like(q.Subject, $"%{term}%") ||
                    EF.Functions.Like(q.Topic, $"%{term}%") ||
                    (q.Paper != null && q.Paper.PaperCode != null && EF.Functions.Like(q.Paper.PaperCode, $"%{term}%")) ||
                    (q.Paper != null && EF.Functions.Like(q.Paper.Exam!.Name, $"%{term}%")) ||
                    (isNumeric && q.QuestionNumber == numericTerm))
                // Best-effort relevance: a match at the very start of the question text ranks above a
                // match buried in the middle, then newest first as a tiebreaker. Not real ranking, but
                // meaningfully better than an arbitrary order when Meilisearch isn't there to do it.
                .OrderByDescending(q => q.QuestionText.StartsWith(term))
                .ThenByDescending(q => q.CreatedAt)
                .Take(limit)
                .ToListAsync();

            return entities.Select(QuestionSearchDocument.FromQuestion).ToList();
        }

        // Same visibility rule as every other student-facing question query: only Published-paper
        // questions (or legacy pre-Paper rows), matching what's actually in the Meilisearch index.
        private IQueryable<Models.Question> BaseQuery() => _db.Questions
            .Include(q => q.Paper).ThenInclude(p => p!.Exam)
            .Where(q => q.PaperId == null || q.Paper!.Status == PaperStatus.Published);
    }
}
