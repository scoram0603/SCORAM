using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using StackExchange.Redis;

namespace ScoramAPI.Services
{
    // Consumes QuestionBankImportCommitJob entries. Mirrors BulkImportCommitWorker exactly --
    // QuestionBankImportJob already has a Status column (like ImportJob does), so the outcome is
    // recorded there rather than needing a cache-based status wrapper the way
    // BulkPaperImportCommitWorker does.
    public class QuestionBankImportCommitWorker : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);

        private readonly IConnectionMultiplexer _redis;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<QuestionBankImportCommitWorker> _logger;

        public QuestionBankImportCommitWorker(IConnectionMultiplexer redis, IServiceScopeFactory scopeFactory, ILogger<QuestionBankImportCommitWorker> logger)
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
                    var json = await db.ListRightPopAsync(RedisBackgroundJobQueue.QuestionBankImportCommitQueueKey);
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
                    _logger.LogError(ex, "QuestionBankImportCommitWorker's poll loop failed, backing off before retrying");
                    await Task.Delay(ErrorBackoff, stoppingToken);
                }
            }
        }

        private async Task ProcessJobAsync(string json)
        {
            QuestionBankImportCommitJob? job;
            try
            {
                job = JsonSerializer.Deserialize<QuestionBankImportCommitJob>(json);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Dropping an unparseable question-bank-import-commit job payload: {Payload}", json);
                return;
            }

            if (job == null) return;

            using var scope = _scopeFactory.CreateScope();
            var commitService = scope.ServiceProvider.GetRequiredService<IQuestionBankImportCommitService>();

            try
            {
                await commitService.CommitAsync(job.JobId, job.AdminId, job.RowNumbers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Question-bank-import commit failed for job {JobId}, marking it Failed", job.JobId);
                try
                {
                    var db = scope.ServiceProvider.GetRequiredService<ScoramDbContext>();
                    var importJob = await db.QuestionBankImportJobs.FirstOrDefaultAsync(j => j.Id == job.JobId);
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
