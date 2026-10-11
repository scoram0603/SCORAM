using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using ScoramAPI.Data;

namespace ScoramAPI.Services
{
    public interface ISecurityStampService
    {
        Task<bool> IsValidAsync(Guid principalId, bool isAdmin, string tokenStamp);

        // Called wherever SecurityStamp is regenerated (password change, logout-everywhere,
        // deactivation) so the next request doesn't keep reading a stale cached value for up to the
        // full cache TTL. Async and awaited at every call site rather than fire-and-forget: this runs
        // on a revocation path, so letting the process move on before the invalidation has actually
        // landed would partially defeat the point of calling it at all (the whole reason it's called
        // instead of just waiting out the TTL is to close that window immediately).
        Task InvalidateAsync(Guid principalId, bool isAdmin);
    }

    // Backed by IDistributedCache -- Redis when ConnectionStrings:Redis is configured, an in-memory
    // fallback otherwise (see Program.cs) -- so revocation is consistent across every app instance
    // instead of being correct-within-60-seconds-per-instance the way a plain IMemoryCache would be.
    public class SecurityStampService : ISecurityStampService
    {
        private static readonly DistributedCacheEntryOptions CacheOptions =
            new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) };

        // IDistributedCache can't distinguish "not cached yet" from "cached as null" (GetStringAsync
        // returns null for both) -- this sentinel is what's actually stored to mean "looked this
        // principal up, they don't exist / have no stamp", so that case gets cached too instead of
        // hitting the DB on every single request for it (e.g. a token for a deleted account).
        private const string NotFoundSentinel = "\0none";

        private readonly ScoramDbContext _db;
        private readonly IDistributedCache _cache;

        public SecurityStampService(ScoramDbContext db, IDistributedCache cache)
        {
            _db = db;
            _cache = cache;
        }

        public async Task<bool> IsValidAsync(Guid principalId, bool isAdmin, string tokenStamp)
        {
            var cacheKey = CacheKey(principalId, isAdmin);

            // The cache is only an optimisation: if Redis is down/slow, fall through to the database
            // instead of turning every authenticated request into a 500.
            string? cached = null;
            try { cached = await _cache.GetStringAsync(cacheKey); }
            catch (Exception) { /* cache unavailable -- read the DB below */ }

            string? currentStamp;
            if (cached == null)
            {
                currentStamp = isAdmin
                    ? (await _db.Admins.Where(a => a.Id == principalId).Select(a => (Guid?)a.SecurityStamp).FirstOrDefaultAsync())?.ToString()
                    : (await _db.Users.Where(u => u.Id == principalId).Select(u => (Guid?)u.SecurityStamp).FirstOrDefaultAsync())?.ToString();

                try { await _cache.SetStringAsync(cacheKey, currentStamp ?? NotFoundSentinel, CacheOptions); }
                catch (Exception) { /* best-effort */ }
            }
            else
            {
                currentStamp = cached == NotFoundSentinel ? null : cached;
            }

            return currentStamp != null && currentStamp == tokenStamp;
        }

        public async Task InvalidateAsync(Guid principalId, bool isAdmin)
        {
            try { await _cache.RemoveAsync(CacheKey(principalId, isAdmin)); }
            catch (Exception) { /* entry expires within the 60s TTL anyway */ }
        }

        private static string CacheKey(Guid principalId, bool isAdmin) => $"secstamp:{(isAdmin ? "admin" : "user")}:{principalId}";
    }
}
