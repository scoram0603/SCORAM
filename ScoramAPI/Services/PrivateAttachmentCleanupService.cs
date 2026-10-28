using Microsoft.EntityFrameworkCore;
using ScoramAPI.Controllers;
using ScoramAPI.Data;

namespace ScoramAPI.Services
{
    /// <summary>What one sweep did. Counts only -- never blob names or URLs.</summary>
    public readonly record struct AttachmentSweepResult(int Examined, int Cleared, int Failed);

    // Finishes the deletion of private chat / DM attachment blobs that an earlier request could not remove.
    //
    // WHY: "delete for everyone" / "unsend" hides the message first and then deletes the blob. If storage was
    // down at that moment the stored URL is deliberately KEPT as a retry marker (see
    // PrivateAttachmentDelivery.CleanupAsync). The message is no longer shown, so the person cannot repeat the
    // delete from the UI -- without this sweep that blob would stay in storage indefinitely.
    //
    // WHAT: every deleted message that still has an attachment value is re-run through the same validated,
    // idempotent cleanup (only a file directly inside chat-attachments/ or dm-attachments/ is ever deleted).
    // Success -- or "the blob was already gone" -- clears the URL, so the row drops out of the next sweep.
    // Failures stay as markers and are tried again next time.
    //
    // SAFETY: switched OFF by default. Turning it on deletes blobs of every already-deleted message that still
    // holds a URL -- which, for messages deleted before this change, includes old group-chat deletions that never
    // removed their file. That is an irreversible bulk deletion, so it needs an explicit decision:
    // PrivateAttachments:CleanupSweep:Enabled=true (see the deployment notes for the read-only count query and
    // the backup/soft-delete check to do first). Blob deletes never run inside a database transaction.
    public class PrivateAttachmentCleanupService : BackgroundService
    {
        public const string EnabledConfigKey = "PrivateAttachments:CleanupSweep:Enabled";

        private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);
        private const int PageSize = 100;
        private const int MaxPagesPerSweep = 20; // bounds one sweep to 2,000 messages per message type

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PrivateAttachmentCleanupService> _logger;

        public PrivateAttachmentCleanupService(
            IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<PrivateAttachmentCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(InitialDelay, stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_configuration.GetValue<bool>(EnabledConfigKey))
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<ScoramDbContext>();
                        var blobs = scope.ServiceProvider.GetRequiredService<IAzureBlobService>();
                        var result = await SweepOnceAsync(db, blobs, _logger, stoppingToken);
                        if (result.Examined > 0)
                            _logger.LogInformation("Private attachment cleanup sweep: examined {Examined}, cleared {Cleared}, still failing {Failed}.",
                                result.Examined, result.Cleared, result.Failed);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception ex)
                    {
                        // Best-effort like every other cleanup path here: try again next interval. Exception type only.
                        _logger.LogWarning("Private attachment cleanup sweep failed ({ExceptionType}).", ex.GetType().Name);
                    }
                }

                try { await Task.Delay(SweepInterval, stoppingToken); }
                catch (TaskCanceledException) { }
            }
        }

        /// <summary>One pass over deleted group-chat and direct messages that still carry an attachment value.</summary>
        public static async Task<AttachmentSweepResult> SweepOnceAsync(
            ScoramDbContext db, IAzureBlobService blobs, ILogger? logger, CancellationToken cancellationToken, int pageSize = PageSize)
        {
            int examined = 0, cleared = 0, failed = 0;

            // ---- group chat ----
            var chatFailedSoFar = 0;
            for (var page = 0; page < MaxPagesPerSweep; page++)
            {
                // Rows that were cleared leave the filter; rows that failed stay, so skipping the failures seen so
                // far walks forward without ever re-reading them and without a Guid range comparison.
                var batch = await db.ChatMessages
                    .Where(m => m.IsDeleted && m.AttachmentUrl != null)
                    .OrderBy(m => m.Id)
                    .Skip(chatFailedSoFar)
                    .Take(pageSize)
                    .ToListAsync(cancellationToken);
                if (batch.Count == 0) break;

                foreach (var message in batch)
                {
                    examined++;
                    var outcome = await PrivateAttachmentDelivery.CleanupAsync(
                        blobs, message.AttachmentUrl, PrivateAttachmentRules.ChatFolder, logger, message.Id, cancellationToken);
                    if (outcome is AttachmentCleanupResult.Deleted or AttachmentCleanupResult.SkippedInvalid or AttachmentCleanupResult.NothingToDelete)
                    {
                        message.AttachmentUrl = null;
                        cleared++;
                    }
                    else
                    {
                        failed++;
                        chatFailedSoFar++;
                    }
                }
                await db.SaveChangesAsync(cancellationToken); // short save AFTER the storage calls, never around them
            }

            // ---- direct messages ----
            var dmFailedSoFar = 0;
            for (var page = 0; page < MaxPagesPerSweep; page++)
            {
                var batch = await db.DirectMessages
                    .Where(m => m.IsDeleted && m.AttachmentUrl != null)
                    .OrderBy(m => m.Id)
                    .Skip(dmFailedSoFar)
                    .Take(pageSize)
                    .ToListAsync(cancellationToken);
                if (batch.Count == 0) break;

                foreach (var message in batch)
                {
                    examined++;
                    var outcome = await PrivateAttachmentDelivery.CleanupAsync(
                        blobs, message.AttachmentUrl, PrivateAttachmentRules.DirectMessageFolder, logger, message.Id, cancellationToken);
                    if (outcome is AttachmentCleanupResult.Deleted or AttachmentCleanupResult.SkippedInvalid or AttachmentCleanupResult.NothingToDelete)
                    {
                        message.AttachmentUrl = null;
                        cleared++;
                    }
                    else
                    {
                        failed++;
                        dmFailedSoFar++;
                    }
                }
                await db.SaveChangesAsync(cancellationToken);
            }

            return new AttachmentSweepResult(examined, cleared, failed);
        }
    }
}
