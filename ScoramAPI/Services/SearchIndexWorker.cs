using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using StackExchange.Redis;

namespace ScoramAPI.Services
{
    // Consumes SearchIndexJob entries queued by RedisBackgroundJobQueue (see that file's own comment
    // for the LIST-vs-Streams tradeoff) and applies them via IInstantSearchService. Only ever
    // registered when Redis is configured (see Program.cs) -- there's nothing for it to consume
    // otherwise.
    //
    // Failed jobs are logged and dropped, not retried or dead-lettered -- same "log it and move on"
    // stance as the synchronous code this replaces (PapersController's own comment on a search-index
    // hiccup never failing the underlying Publish/Unpublish/Delete). A stuck/repeatedly-failing job
    // type would need PapersController's ReindexSearch (SuperAdmin, rebuilds from scratch) as the
    // recovery path today -- a smarter retry-with-backoff is a reasonable future addition if failures
    // turn out to be common enough in practice to justify it, but isn't built here speculatively.
    public class SearchIndexWorker : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);

        private readonly IConnectionMultiplexer _redis;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SearchIndexWorker> _logger;

        public SearchIndexWorker(IConnectionMultiplexer redis, IServiceScopeFactory scopeFactory, ILogger<SearchIndexWorker> logger)
        {
            _redis = redis;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var db = _redis.GetDatabase();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // A plain non-blocking RPOP polled every couple seconds, not Redis's blocking
                    // BRPOP -- simpler to reason about correctly across StackExchange.Redis versions,
                    // at the cost of up to ~2s added latency before a newly-enqueued job starts
                    // processing. That's a non-issue here: nothing waits on this queue synchronously
                    // (that's the entire point of moving this work off the request path), so a couple
                    // extra seconds before a paper's questions become searchable is unnoticeable.
                    var json = await db.ListRightPopAsync(RedisBackgroundJobQueue.SearchIndexQueueKey);
                    if (json.IsNullOrEmpty)
                    {
                        await Task.Delay(PollInterval, stoppingToken);
                        continue;
                    }

                    await ProcessJobAsync(json!);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SearchIndexWorker's poll loop failed, backing off before retrying");
                    await Task.Delay(ErrorBackoff, stoppingToken);
                }
            }
        }

        private async Task ProcessJobAsync(string json)
        {
            SearchIndexJob? job;
            try
            {
                job = JsonSerializer.Deserialize<SearchIndexJob>(json);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Dropping an unparseable search-index job payload: {Payload}", json);
                return;
            }

            if (job == null) return;

            try
            {
                // Scoped services (ScoramDbContext, IInstantSearchService) need their own scope here --
                // this worker itself is a singleton (BackgroundService), so it can't just take them via
                // constructor injection the way a controller would.
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ScoramDbContext>();
                var instantSearch = scope.ServiceProvider.GetRequiredService<IInstantSearchService>();

                switch (job.JobType)
                {
                    case SearchIndexJobType.IndexPaper when job.PaperId.HasValue:
                        var docs = await db.Questions
                            .Include(q => q.Paper).ThenInclude(p => p!.Exam)
                            .Where(q => q.PaperId == job.PaperId.Value)
                            .ToListAsync();
                        await instantSearch.IndexQuestionsAsync(docs.Select(QuestionSearchDocument.FromQuestion));
                        break;

                    case SearchIndexJobType.RemoveQuestions when job.QuestionIds is { Count: > 0 }:
                        await instantSearch.RemoveQuestionsAsync(job.QuestionIds);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process search-index job {JobType} for paper {PaperId}", job.JobType, job.PaperId);
            }
        }
    }
}
