using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    public interface IRefreshTokenService
    {
        Task<(string rawToken, DateTime expiresAt)> IssueAsync(Guid principalId, bool isAdmin, string? createdByIp);

        // Looks the token up by hash only -- does NOT check whether it's active. Callers must inspect
        // the returned row's RevokedAt/ExpiresAt themselves, because "already revoked" and "expired"
        // need different handling (see AuthController.Refresh's own comment on token-reuse detection).
        Task<RefreshToken?> FindByRawTokenAsync(string rawToken, bool isAdmin);

        // Rotation: revokes `token`, points it at `replacement`, and persists the new row in the same
        // SaveChanges call so a crash between the two can't leave an old token revoked with no
        // working replacement.
        // Returns false when another request already rotated/revoked `token` first (a race between two
        // parallel refreshes) -- the caller must NOT hand out the replacement in that case.
        Task<bool> RotateAsync(RefreshToken token, RefreshToken replacement);

        // Network-retry grace: if the client's refresh response was lost (flaky mobile data) it retries
        // with the token the server already rotated a moment ago. Inside a short window that is a
        // legitimate retry, not theft -- returns the still-live replacement so the caller can rotate
        // THAT one and hand the client a fresh pair. Null means "outside the window / no live
        // replacement", i.e. real reuse.
        Task<RefreshToken?> TryGetGraceReplacementAsync(RefreshToken revoked);

        Task RevokeAsync(RefreshToken token);

        // Every active token for this principal -- used when a password change, deactivation, or
        // detected token-reuse means every outstanding session should end, not just the one in hand.
        Task RevokeAllAsync(Guid principalId, bool isAdmin);
    }

    public class RefreshTokenService : IRefreshTokenService
    {
        private readonly ScoramDbContext _db;
        private readonly ITokenService _tokenService;

        public RefreshTokenService(ScoramDbContext db, ITokenService tokenService)
        {
            _db = db;
            _tokenService = tokenService;
        }

        public async Task<(string rawToken, DateTime expiresAt)> IssueAsync(Guid principalId, bool isAdmin, string? createdByIp)
        {
            var (rawToken, expiresAt) = _tokenService.GenerateRefreshToken();
            _db.RefreshTokens.Add(new RefreshToken
            {
                PrincipalId = principalId,
                IsAdmin = isAdmin,
                TokenHash = _tokenService.HashRefreshToken(rawToken),
                ExpiresAt = expiresAt,
                CreatedByIp = createdByIp
            });
            await _db.SaveChangesAsync();
            return (rawToken, expiresAt);
        }

        public async Task<RefreshToken?> FindByRawTokenAsync(string rawToken, bool isAdmin)
        {
            var hash = _tokenService.HashRefreshToken(rawToken);
            return await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.IsAdmin == isAdmin);
        }

        private static readonly TimeSpan RotationGrace = TimeSpan.FromSeconds(30);

        public async Task<bool> RotateAsync(RefreshToken token, RefreshToken replacement)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            // Conditional UPDATE: only the first of several concurrent refreshes wins the revoke.
            var now = DateTime.UtcNow;
            var revoked = await _db.RefreshTokens
                .Where(t => t.Id == token.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.ReplacedByTokenId, replacement.Id));
            if (revoked == 0)
            {
                await tx.RollbackAsync();
                return false;
            }

            _db.RefreshTokens.Add(replacement);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            token.RevokedAt = now;
            token.ReplacedByTokenId = replacement.Id;
            return true;
        }

        public async Task<RefreshToken?> TryGetGraceReplacementAsync(RefreshToken revoked)
        {
            if (revoked.RevokedAt == null || revoked.ReplacedByTokenId == null) return null;
            if (DateTime.UtcNow - revoked.RevokedAt.Value > RotationGrace) return null;

            var replacement = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == revoked.ReplacedByTokenId.Value);
            return replacement != null && replacement.IsActive ? replacement : null;
        }

        public async Task RevokeAsync(RefreshToken token)
        {
            token.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        public async Task RevokeAllAsync(Guid principalId, bool isAdmin)
        {
            var tokens = await _db.RefreshTokens
                .Where(t => t.PrincipalId == principalId && t.IsAdmin == isAdmin && t.RevokedAt == null)
                .ToListAsync();

            foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}
