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
        Task RotateAsync(RefreshToken token, RefreshToken replacement);

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

        public async Task RotateAsync(RefreshToken token, RefreshToken replacement)
        {
            token.RevokedAt = DateTime.UtcNow;
            token.ReplacedByTokenId = replacement.Id;
            _db.RefreshTokens.Add(replacement);
            await _db.SaveChangesAsync();
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
