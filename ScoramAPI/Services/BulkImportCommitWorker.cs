using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using StackExchange.Redis;

namespace ScoramAPI.Services
{
    // Consumes BulkImportCommitJob entries queued by BulkImportController.Commit. Only ever
    // registered when Redis is configured (see Program.cs). See SearchIndexWorker's own comment for
    // the general LIST-vs-Streams reasoning this shares.
    //
    // Unlike a failed search-index job (silently dropped -- a missing search result is a minor,
    // recoverable annoyance), a failed commit is a real admin-facing outcome: the job's Status is set
    // to Failed so GetStatus/History surface it, rather than leaving the admin staring at "Processing"
    // forever with no explanation.
    public class BulkImportCommitWorker : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);

        private readonly IConnectionMultiplexer _redis;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BulkImportCommitWorker> _logger;

        public BulkImportCommitWorker(IConnectionMultiplexer redis, IServiceScopeFactory scopeFactory, ILogger<BulkImportCommitWorker> logger)
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
                    var json = await db.ListRightPopAsync(RedisBackgroundJobQueue.BulkImportCommitQueueKey);
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
                    _logger.LogError(ex, "BulkImportCommitWorker's poll loop failed, backing off before retrying");
                    await Task.Delay(ErrorBackoff, stoppingToken);
                }
            }
        }

        private async Task ProcessJobAsync(string json)
        {
            BulkImportCommitJob? job;
            try
            {
                job = JsonSerializer.Deserialize<BulkImportCommitJob>(json);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Dropping an unparseable bulk-import-commit job payload: {Payload}", json);
                return;
            }

            if (job == null) return;

            using var scope = _scopeFactory.CreateScope();
            var commitService = scope.ServiceProvider.GetRequiredService<IBulkImportCommitService>();

            try
            {
                await commitService.CommitAsync(job.JobId, job.AdminId, job.RowNumbers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk-import commit failed for job {JobId}, marking it Failed", job.JobId);

                // Best-effort: if even this fails (e.g. the DB itself is unreachable), the job is left
                // at Processing -- an admin re-checking GetStatus sees it hasn't finished rather than
                // a wrong "succeeded" status, which is the safer failure mode of the two.
                try
                {
                    var db = scope.ServiceProvider.GetRequiredService<ScoramDbContext>();
                    var importJob = await db.ImportJobs.FirstOrDefaultAsync(j => j.Id == job.JobId);
                    if (importJob != null && importJob.Status == ImportJobStatus.Processing)
                    {
                        importJob.Status = ImportJobStatus.Failed;
                        await db.SaveChangesAsync();
                    }
                }
                catch (Exception markFailedEx)
                {
                    _logger.LogError(markFailedEx, "Additionally failed to mark job {JobId} as Failed", job.JobId);
                }
            }
        }
    }
}
