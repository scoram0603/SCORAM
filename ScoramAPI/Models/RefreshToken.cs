using System.ComponentModel.DataAnnotations.Schema;

namespace ScoramAPI.Models
{
    // One row per issued refresh token, for both students and admins (distinguished by IsAdmin --
    // PrincipalId is a User.Id or an Admin.Id depending on that flag). The raw token is only ever
    // held by the client; TokenHash is a SHA-256 digest of it, so a leaked database dump alone
    // can't be replayed as a live refresh token, same reasoning as PasswordHash never storing a
    // plaintext password.
    //
    // Rotation: every successful refresh revokes the token used (RevokedAt set, ReplacedByTokenId
    // pointed at the new row) and issues a brand new one -- see IRefreshTokenService. If a token
    // that's already been rotated is presented again, that's a strong signal of a stolen/replayed
    // token (the legitimate client would have moved on to the new one), so the whole chain is
    // revoked rather than just rejecting that one request.
    public class RefreshToken
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid PrincipalId { get; set; }
        public bool IsAdmin { get; set; }

        public string TokenHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public Guid? ReplacedByTokenId { get; set; }

        // Best-effort audit trail, not a security control on its own -- see the forwarded-headers
        // comment in Program.cs for why a client-controlled IP can't be fully trusted.
        public string? CreatedByIp { get; set; }

        [NotMapped]
        public bool IsActive => RevokedAt == null && ExpiresAt > DateTime.UtcNow;
    }
}
