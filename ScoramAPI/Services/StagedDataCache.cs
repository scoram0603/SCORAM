using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace ScoramAPI.Services
{
    // Thin JSON-serializing wrapper around IDistributedCache, for staging complex objects across
    // multiple requests in a multi-step workflow (upload -> preview/edit -> commit) -- specifically
    // the bulk-import row lists in BulkImportController, BulkPaperImportController, and
    // QuestionBankAdminController. Unlike QuestionsController's caches (a miss there just means "hit
    // the DB again for the same correct answer"), a miss here means the staged data doesn't exist
    // ANYWHERE -- it was only ever parsed from the uploaded file into memory, never persisted. Before
    // this existed, that data lived in per-process IMemoryCache: an admin's "upload" landing on one
    // instance and their later "commit" landing on a different one (behind a load balancer without
    // sticky sessions) would fail with "no staged rows found" even though the upload had genuinely
    // succeeded moments earlier. Backed by IDistributedCache -- Redis when configured, an in-memory
    // fallback otherwise (see Program.cs) -- so this is only actually fixed once Redis is configured;
    // without it, the failure mode above is unchanged from before.
    public interface IStagedDataCache
    {
        Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class;
        Task<T?> GetAsync<T>(string key) where T : class;
        Task RemoveAsync(string key);
    }

    public class StagedDataCache : IStagedDataCache
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly IDistributedCache _cache;

        public StagedDataCache(IDistributedCache cache)
        {
            _cache = cache;
        }

        public Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            return _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
        }

        public async Task<T?> GetAsync<T>(string key) where T : class
        {
            var json = await _cache.GetStringAsync(key);
            return json == null ? null : JsonSerializer.Deserialize<T>(json, JsonOptions);
        }

        public Task RemoveAsync(string key) => _cache.RemoveAsync(key);
    }
}
