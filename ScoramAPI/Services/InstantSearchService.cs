using System.Net.Http.Json;
using ScoramAPI.DTOs;

namespace ScoramAPI.Services
{
    public interface IInstantSearchService
    {
        /// <summary>Adds or updates (upserts, by Id) the given documents in the search index. A
        /// Meilisearch outage/misconfiguration throws -- callers should catch and log rather than let
        /// a search-index hiccup fail the underlying database operation (publish/unpublish/delete).</summary>
        Task IndexQuestionsAsync(IEnumerable<QuestionSearchDocument> documents);

        Task RemoveQuestionsAsync(IEnumerable<Guid> questionIds);

        Task<List<QuestionSearchDocument>> SearchAsync(string query, int limit = 20);

        /// <summary>Wipes the entire index -- used by the admin "reindex from scratch" action.</summary>
        Task ClearIndexAsync();

        /// <summary>Pings Meilisearch's /health endpoint with a short timeout. Never throws -- returns
        /// false for anything from "unreachable" to "misconfigured URL", which is all the dashboard's
        /// system-status panel needs to know.</summary>
        Task<bool> IsHealthyAsync();
    }

    public class InstantSearchService : IInstantSearchService
    {
        private const string IndexName = "questions";
        private readonly HttpClient _http;

        public InstantSearchService(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _http = httpClientFactory.CreateClient("meilisearch");
            var url = config["Meilisearch:Url"] ?? "http://localhost:7700";
            _http.BaseAddress = new Uri(url.TrimEnd('/') + "/");

            var apiKey = config["Meilisearch:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
                _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        public async Task IndexQuestionsAsync(IEnumerable<QuestionSearchDocument> documents)
        {
            var list = documents.ToList();
            if (list.Count == 0) return;

            var response = await _http.PostAsJsonAsync($"indexes/{IndexName}/documents?primaryKey=id", list);
            response.EnsureSuccessStatusCode();
        }

        public async Task RemoveQuestionsAsync(IEnumerable<Guid> questionIds)
        {
            var ids = questionIds.Select(id => id.ToString()).ToList();
            if (ids.Count == 0) return;

            var response = await _http.PostAsJsonAsync($"indexes/{IndexName}/documents/delete-batch", ids);
            response.EnsureSuccessStatusCode();
        }

        public async Task<List<QuestionSearchDocument>> SearchAsync(string query, int limit = 20)
        {
            var response = await _http.PostAsJsonAsync($"indexes/{IndexName}/search", new { q = query, limit });
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<MeiliSearchResponse>();
            return result?.Hits ?? new List<QuestionSearchDocument>();
        }

        public async Task ClearIndexAsync()
        {
            var response = await _http.DeleteAsync($"indexes/{IndexName}/documents");
            response.EnsureSuccessStatusCode();
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var response = await _http.GetAsync("health", cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                // Unreachable, DNS failure, timeout, bad BaseAddress -- all just mean "not healthy"
                // from the dashboard's point of view, never a reason to fail the whole stats request.
                return false;
            }
        }

        private class MeiliSearchResponse
        {
            public List<QuestionSearchDocument> Hits { get; set; } = new();
        }
    }
}
