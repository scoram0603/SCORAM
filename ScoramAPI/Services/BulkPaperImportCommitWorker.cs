using System.Text.Json;
using StackExchange.Redis;

namespace ScoramAPI.Services
{
    // Consumes BulkPaperImportCommitJob entries. See SearchIndexWorker for the general LIST-vs-
    // Streams reasoning, and BulkImportCommitWorker for the sibling this mirrors -- the one real
    // difference is where the outcome is recorded: that one updates an ImportJob DB row's Status
    // column, this one writes a BulkPaperImportCommitStatus into IStagedDataCache instead, since
    // BulkPaperImportController has no DB job row at all (see that job class's own comment).
    public class BulkPaperImportCommitWorker : BackgroundService
    {
        // Longer than the 30-minute preview-rows cache -- an admin should still be able to check
        // "did this finish?" for a while after a slow commit, even though the input rows it
        // consumed are long gone by then.
        private static readonly TimeSpan StatusTtl = TimeSpan.FromHours(2);
        public const string StatusCachePrefix = "bulk-paper-import-status:";

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);

        private readonly IConnectionMultiplexer _redis;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BulkPaperImportCommitWorker> _logger;

        public BulkPaperImportCommitWorker(IConnectionMultiplexer redis, IServiceScopeFactory scopeFactory, ILogger<BulkPaperImportCommitWorker> logger)
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
                    var json = await db.ListRightPopAsync(RedisBackgroundJobQueue.BulkPaperImportCommitQueueKey);
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
                    _logger.LogError(ex, "BulkPaperImportCommitWorker's poll loop failed, backing off before retrying");
                    await Task.Delay(ErrorBackoff, stoppingToken);
                }
            }
        }

        private async Task ProcessJobAsync(string json)
        {
            BulkPaperImportCommitJob? job;
            try
            {
                job = JsonSerializer.Deserialize<BulkPaperImportCommitJob>(json);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Dropping an unparseable bulk-paper-import-commit job payload: {Payload}", json);
                return;
            }

            if (job == null) return;

            using var scope = _scopeFactory.CreateScope();
            var commitService = scope.ServiceProvider.GetRequiredService<IBulkPaperImportCommitService>();
            var cache = scope.ServiceProvider.GetRequiredService<IStagedDataCache>();

            try
            {
                var result = await commitService.CommitAsync(job.JobId, job.AdminId, job.RowNumbers);
                await cache.SetAsync(StatusCachePrefix + job.JobId, new BulkPaperImportCommitStatus { Status = "Committed", Result = result }, StatusTtl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk-paper-import commit failed for job {JobId}", job.JobId);
                try
                {
                    await cache.SetAsync(StatusCachePrefix + job.JobId, new BulkPaperImportCommitStatus { Status = "Failed", ErrorMessage = ex.Message }, StatusTtl);
                }
                catch (Exception cacheEx)
                {
                    _logger.LogError(cacheEx, "Additionally failed to record Failed status for job {JobId}", job.JobId);
                }
            }
        }
    }
}
