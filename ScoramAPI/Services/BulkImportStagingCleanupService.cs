using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScoramAPI.Data;
using ScoramAPI.Enums;

namespace ScoramAPI.Services
{
    // Deletes the staged images left behind by an abandoned bulk-import preview -- one that was
    // parsed (and, if it used a ZIP, had its images uploaded to a temp "bulk-import-staging/{jobId}"
    // blob folder) but never committed before its 30-minute IMemoryCache entry expired. A committed
    // job already cleans up its own staging folder at the end of Commit() -- see
    // BulkImportController.Commit / QuestionBankAdminController.Commit -- so this service only ever
    // finds abandoned/expired jobs left over.
    //
    // Runs as a plain timer-based sweep rather than an IMemoryCache eviction callback -- the
    // eviction-callback approach would need to spin up its own DI scope from inside a
    // cache-internal callback (the controller's own request-scoped services are long gone by the
    // time an entry actually expires), which is exactly the kind of infrastructure spec section 53
    // warns against for what is, underneath it, a straightforward "clean up anything older than 30
    // minutes" sweep. Re-checking (and re-attempting a no-op delete on) an already-cleaned job every
    // sweep is a deliberate simplicity tradeoff -- cheap for an admin tool's realistic volume of
    // bulk-import attempts, and avoids adding a "staging already cleaned" column just to skip it.
    public class BulkImportStagingCleanupService : BackgroundService
    {
        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);
        // A little longer than the preview cache's own 30-minute lifetime, so this never races a
        // preview that's still legitimately in progress right at the boundary.
        private static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(35);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BulkImportStagingCleanupService> _logger;

        public BulkImportStagingCleanupService(IServiceScopeFactory scopeFactory, ILogger<BulkImportStagingCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Give the app a moment to finish starting up before the first sweep.
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // Best-effort, same as every other cleanup path in this project (see
                    // FileStorageService.DeleteImageAsync/DeleteFolderAsync) -- a failed sweep just
                    // tries again next interval rather than crashing the whole API process.
                    _logger.LogWarning(ex, "Bulk-import staging cleanup sweep failed");
                }

                try { await Task.Delay(SweepInterval, stoppingToken); }
                catch (TaskCanceledException) { }
            }
        }

        private async Task SweepOnceAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ScoramDbContext>();
            var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

            var cutoff = DateTime.UtcNow - AbandonedAfter;

            var abandonedPaperJobIds = await db.ImportJobs
                .Where(j => j.Status == ImportJobStatus.PendingReview && j.CreatedAt < cutoff)
                .Select(j => j.Id)
                .ToListAsync(stoppingToken);

            var abandonedBankJobIds = await db.QuestionBankImportJobs
                .Where(j => j.Status == ImportJobStatus.PendingReview && j.CreatedAt < cutoff)
                .Select(j => j.Id)
                .ToListAsync(stoppingToken);

            foreach (var jobId in abandonedPaperJobIds.Concat(abandonedBankJobIds))
            {
                await fileStorage.DeleteFolderAsync($"bulk-import-staging/{jobId}");
            }
        }
    }
}
