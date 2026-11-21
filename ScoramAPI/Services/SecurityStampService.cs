using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using ScoramAPI.Data;

namespace ScoramAPI.Services
{
    public interface ISecurityStampService
    {
        Task<bool> IsValidAsync(Guid principalId, bool isAdmin, string tokenStamp);

        // SINGLE-DEVICE LOGIN -- students only (no isAdmin parameter: admins have no
        // ActiveSessionId column at all -- see AdminSessionsController for why they're
        // intentionally exempt from this feature). Checked by Program.cs's OnTokenValidated
        // alongside IsValidAsync above, sharing the same cached per-principal lookup (see the
        // implementation) so this never costs an extra DB round trip or cache entry per request.
        Task<bool> IsSessionValidAsync(Guid userId, string tokenSessionId);

        // Called wherever SecurityStamp is regenerated (password change, logout-everywhere,
        // deactivation) so the next request doesn't keep reading a stale cached value for up to the
        // full cache TTL. Async and awaited at every call site rather than fire-and-forget: this runs
        // on a revocation path, so letting the process move on before the invalidation has actually
        // landed would partially defeat the point of calling it at all (the whole reason it's called
        // instead of just waiting out the TTL is to close that window immediately). Also clears any
        // cached ActiveSessionId for the same principal, since both live under one cache entry --
        // see AuthController.IssueAuthResponseAsync, which relies on this to make a newly-superseded
        // device's very next request see the new ActiveSessionId rather than a stale cached one.
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
            var state = await GetStateAsync(principalId, isAdmin);
            return state.Stamp != null && state.Stamp == tokenStamp;
        }

        public async Task<bool> IsSessionValidAsync(Guid userId, string tokenSessionId)
        {
            var state = await GetStateAsync(userId, isAdmin: false);
            return state.ActiveSessionId != null && state.ActiveSessionId == tokenSessionId;
        }

        public async Task InvalidateAsync(Guid principalId, bool isAdmin)
        {
            try { await _cache.RemoveAsync(CacheKey(principalId, isAdmin)); }
            catch (Exception) { /* entry expires within the 60s TTL anyway */ }
        }

        // SecurityStamp and (for students) ActiveSessionId are both small, together-written,
        // together-invalidated pieces of per-principal auth state, so they share one cache entry
        // instead of two -- one cache read per request either way, whether OnTokenValidated ends up
        // needing just the stamp (admin tokens) or both (student tokens).
        private readonly record struct PrincipalSecurityState(string? Stamp, string? ActiveSessionId);

        private async Task<PrincipalSecurityState> GetStateAsync(Guid principalId, bool isAdmin)
        {
            var cacheKey = CacheKey(principalId, isAdmin);

            // The cache is only an optimisation: if Redis is down/slow, fall through to the database
            // instead of turning every authenticated request into a 500.
            string? cached = null;
            try { cached = await _cache.GetStringAsync(cacheKey); }
            catch (Exception) { /* cache unavailable -- read the DB below */ }

            if (cached != null)
            {
                if (cached == NotFoundSentinel) return default;

                // Encoded as "stamp|sid" (sid empty when the user has no ActiveSessionId yet). A
                // value with no "|" is a pre-single-device-login cache entry written by an
                // old-version instance during a rolling deploy -- at most 60s old -- so it's treated
                // as a miss and re-read from the database below rather than guessed at.
                var parts = cached.Split('|', 2);
                if (parts.Length == 2)
                {
                    return new PrincipalSecurityState(
                        parts[0].Length == 0 ? null : parts[0],
                        parts[1].Length == 0 ? null : parts[1]);
                }
            }

            string? stamp = null;
            string? activeSessionId = null;

            if (isAdmin)
            {
                stamp = (await _db.Admins.Where(a => a.Id == principalId).Select(a => (Guid?)a.SecurityStamp).FirstOrDefaultAsync())?.ToString();
            }
            else
            {
                var row = await _db.Users.Where(u => u.Id == principalId)
                    .Select(u => new { u.SecurityStamp, u.ActiveSessionId })
                    .FirstOrDefaultAsync();
                if (row != null)
                {
                    stamp = row.SecurityStamp.ToString();
                    activeSessionId = row.ActiveSessionId?.ToString();
                }
            }

            try { await _cache.SetStringAsync(cacheKey, stamp == null ? NotFoundSentinel : $"{stamp}|{activeSessionId}", CacheOptions); }
            catch (Exception) { /* best-effort */ }

            return new PrincipalSecurityState(stamp, activeSessionId);
        }

        private static string CacheKey(Guid principalId, bool isAdmin) => $"secstamp:{(isAdmin ? "admin" : "user")}:{principalId}";
    }
}
