using System.Text.Json;
using StackExchange.Redis;

namespace ScoramAPI.Services
{
    public interface IBackgroundJobQueue
    {
        // False when Redis isn't configured/reachable at startup -- callers must run the equivalent
        // work inline in that case (see PapersController for the exact fallback pattern), since
        // there's no other durable, multi-instance-safe place to queue it. Checking this rather than
        // making Enqueue* itself silently degrade to "run inline" keeps that decision visible at the
        // call site instead of hidden inside this service.
        bool IsAvailable { get; }

        Task EnqueueSearchIndexJobAsync(SearchIndexJob job);

        // See BulkImportController.Commit for the fallback pattern -- same shape as
        // EnqueueSearchIndexJobAsync, separate queue/list key since these are unrelated job types
        // processed by a different worker (BulkImportCommitWorker) at a different pace.
        Task EnqueueBulkImportCommitJobAsync(BulkImportCommitJob job);

        // See BulkPaperImportController.Commit for the fallback pattern.
        Task EnqueueBulkPaperImportCommitJobAsync(BulkPaperImportCommitJob job);

        // See QuestionBankAdminController.Commit for the fallback pattern.
        Task EnqueueQuestionBankImportCommitJobAsync(QuestionBankImportCommitJob job);
    }

    // Backed by a plain Redis LIST (LPUSH/RPOP), not Redis Streams -- deliberately, for lower
    // implementation risk: Streams' consumer-group/pending-entry-list machinery is real value for a
    // queue that needs strict at-least-once delivery with crash recovery, but this one doesn't need
    // that bar. The synchronous code this replaces already treats a failed Meilisearch call as
    // "log it and move on" (see PapersController's own comment on that), so "the process crashes in
    // the small window between popping a job and finishing it" being an actual-loss scenario here is
    // no worse than what the existing code already tolerates -- just now rarer, since the job
    // normally only leaves the queue once its worker has already started handling it.
    public class RedisBackgroundJobQueue : IBackgroundJobQueue
    {
        public const string SearchIndexQueueKey = "jobs:search-index";
        public const string BulkImportCommitQueueKey = "jobs:bulk-import-commit";
        public const string BulkPaperImportCommitQueueKey = "jobs:bulk-paper-import-commit";
        public const string QuestionBankImportCommitQueueKey = "jobs:question-bank-import-commit";

        private readonly IConnectionMultiplexer _redis;

        public RedisBackgroundJobQueue(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public bool IsAvailable => true;

        public Task EnqueueSearchIndexJobAsync(SearchIndexJob job)
        {
            var json = JsonSerializer.Serialize(job);
            // LPUSH here, RPOP in SearchIndexWorker -- FIFO order (oldest job processed first).
            return _redis.GetDatabase().ListLeftPushAsync(SearchIndexQueueKey, json);
        }

        public Task EnqueueBulkImportCommitJobAsync(BulkImportCommitJob job)
        {
            var json = JsonSerializer.Serialize(job);
            return _redis.GetDatabase().ListLeftPushAsync(BulkImportCommitQueueKey, json);
        }

        public Task EnqueueBulkPaperImportCommitJobAsync(BulkPaperImportCommitJob job)
        {
            var json = JsonSerializer.Serialize(job);
            return _redis.GetDatabase().ListLeftPushAsync(BulkPaperImportCommitQueueKey, json);
        }

        public Task EnqueueQuestionBankImportCommitJobAsync(QuestionBankImportCommitJob job)
        {
            var json = JsonSerializer.Serialize(job);
            return _redis.GetDatabase().ListLeftPushAsync(QuestionBankImportCommitQueueKey, json);
        }
    }

    // Registered instead of RedisBackgroundJobQueue when Redis isn't configured/reachable (see
    // Program.cs) -- exists so every caller can take an IBackgroundJobQueue via plain constructor
    // injection rather than needing a nullable/optional one just to handle this case.
    public class NullBackgroundJobQueue : IBackgroundJobQueue
    {
        public bool IsAvailable => false;

        public Task EnqueueSearchIndexJobAsync(SearchIndexJob job) =>
            throw new InvalidOperationException(
                "Background job queue isn't available (Redis isn't configured) -- check IsAvailable before calling Enqueue*, and run the work inline instead.");

        public Task EnqueueBulkImportCommitJobAsync(BulkImportCommitJob job) =>
            throw new InvalidOperationException(
                "Background job queue isn't available (Redis isn't configured) -- check IsAvailable before calling Enqueue*, and run the work inline instead.");

        public Task EnqueueBulkPaperImportCommitJobAsync(BulkPaperImportCommitJob job) =>
            throw new InvalidOperationException(
                "Background job queue isn't available (Redis isn't configured) -- check IsAvailable before calling Enqueue*, and run the work inline instead.");

        public Task EnqueueQuestionBankImportCommitJobAsync(QuestionBankImportCommitJob job) =>
            throw new InvalidOperationException(
                "Background job queue isn't available (Redis isn't configured) -- check IsAvailable before calling Enqueue*, and run the work inline instead.");
    }
}
