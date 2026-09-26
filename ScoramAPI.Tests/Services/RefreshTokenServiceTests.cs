using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    public class RefreshTokenServiceTests : IDisposable
    {
        private readonly ScoramDbContext _db;
        private readonly ITokenService _tokenService;
        private readonly RefreshTokenService _service;

        public RefreshTokenServiceTests()
        {
            var options = new DbContextOptionsBuilder<ScoramDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _db = new ScoramDbContext(options);

            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "unit-test-signing-key-at-least-32-bytes-long!!",
                    ["Jwt:Issuer"] = "ScoramAPI.Tests",
                    ["Jwt:Audience"] = "ScoramAPI.Tests.Client",
                    ["Jwt:ExpiryMinutes"] = "60",
                    ["Jwt:RefreshExpiryDays"] = "30",
                })
                .Build();
            _tokenService = new TokenService(config);

            _service = new RefreshTokenService(_db, _tokenService);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task IssueAsync_CreatesAToken_FindableByItsRawValueAfterward()
        {
            var principalId = Guid.NewGuid();

            var (rawToken, expiresAt) = await _service.IssueAsync(principalId, isAdmin: false, createdByIp: "127.0.0.1");
            var found = await _service.FindByRawTokenAsync(rawToken, isAdmin: false);

            Assert.NotNull(found);
            Assert.Equal(principalId, found!.PrincipalId);
            Assert.False(found.IsAdmin);
            Assert.Null(found.RevokedAt); // freshly issued, never used
            Assert.Equal(expiresAt, found.ExpiresAt);
        }

        [Fact]
        public async Task IssueAsync_NeverStoresTheRawTokenValue()
        {
            // See RefreshToken model's own comment: only a hash should ever be persisted, so a
            // leaked DB dump alone can't be replayed. Checked directly against the DB row here,
            // not just inferred from the hashing test in TokenServiceTests.
            var (rawToken, _) = await _service.IssueAsync(Guid.NewGuid(), isAdmin: false, createdByIp: null);

            var storedHashes = await _db.RefreshTokens.Select(t => t.TokenHash).ToListAsync();

            Assert.DoesNotContain(rawToken, storedHashes);
        }

        [Fact]
        public async Task FindByRawTokenAsync_RespectsIsAdminFlag_EvenForTheSamePrincipalId()
        {
            // A student and an admin account could share a Guid only in the astronomically unlikely
            // case discussed in SecurityStampServiceTests -- what actually matters here is more
            // realistic: a raw token issued for a STUDENT session must never be found by a lookup
            // that's searching the ADMIN token space, even by coincidence.
            var principalId = Guid.NewGuid();
            var (studentToken, _) = await _service.IssueAsync(principalId, isAdmin: false, createdByIp: null);
            var (adminToken, _) = await _service.IssueAsync(principalId, isAdmin: true, createdByIp: null);

            Assert.Null(await _service.FindByRawTokenAsync(studentToken, isAdmin: true));
            Assert.Null(await _service.FindByRawTokenAsync(adminToken, isAdmin: false));
            Assert.NotNull(await _service.FindByRawTokenAsync(studentToken, isAdmin: false));
            Assert.NotNull(await _service.FindByRawTokenAsync(adminToken, isAdmin: true));
        }

        [Fact]
        public async Task FindByRawTokenAsync_ReturnsNull_ForATokenThatWasNeverIssued()
        {
            var found = await _service.FindByRawTokenAsync("a-value-nobody-ever-issued", isAdmin: false);

            Assert.Null(found);
        }

        [Fact]
        public async Task RotateAsync_RevokesTheOldTokenAndPersistsTheReplacement()
        {
            var principalId = Guid.NewGuid();
            var (oldRaw, _) = await _service.IssueAsync(principalId, isAdmin: false, createdByIp: null);
            var oldToken = (await _service.FindByRawTokenAsync(oldRaw, isAdmin: false))!;

            var (newRaw, newExpiresAt) = _tokenService.GenerateRefreshToken();
            var replacement = new RefreshToken
            {
                PrincipalId = principalId,
                IsAdmin = false,
                TokenHash = _tokenService.HashRefreshToken(newRaw),
                ExpiresAt = newExpiresAt,
            };

            await _service.RotateAsync(oldToken, replacement);

            Assert.NotNull(oldToken.RevokedAt);
            Assert.Equal(replacement.Id, oldToken.ReplacedByTokenId);
            Assert.NotNull(await _service.FindByRawTokenAsync(newRaw, isAdmin: false));
            // The rotated-away token itself is still findABLE by hash (that's how reuse detection
            // recognizes it -- see AuthController.Refresh's own comment on the replay scenario), but
            // is no longer ACTIVE.
            var reFound = await _service.FindByRawTokenAsync(oldRaw, isAdmin: false);
            Assert.NotNull(reFound);
            Assert.False(reFound!.IsActive);
        }

        [Fact]
        public async Task RevokeAllAsync_RevokesEveryActiveTokenForThatPrincipal_ButNotOthers()
        {
            var principalA = Guid.NewGuid();
            var principalB = Guid.NewGuid();
            var (rawA1, _) = await _service.IssueAsync(principalA, isAdmin: false, createdByIp: null);
            var (rawA2, _) = await _service.IssueAsync(principalA, isAdmin: false, createdByIp: null);
            var (rawB, _) = await _service.IssueAsync(principalB, isAdmin: false, createdByIp: null);

            await _service.RevokeAllAsync(principalA, isAdmin: false);

            Assert.False((await _service.FindByRawTokenAsync(rawA1, isAdmin: false))!.IsActive);
            Assert.False((await _service.FindByRawTokenAsync(rawA2, isAdmin: false))!.IsActive);
            Assert.True((await _service.FindByRawTokenAsync(rawB, isAdmin: false))!.IsActive); // untouched
        }

        [Fact]
        public async Task RevokeAllAsync_DoesNotCrossStudentAdminBoundary()
        {
            var principalId = Guid.NewGuid();
            var (studentRaw, _) = await _service.IssueAsync(principalId, isAdmin: false, createdByIp: null);
            var (adminRaw, _) = await _service.IssueAsync(principalId, isAdmin: true, createdByIp: null);

            await _service.RevokeAllAsync(principalId, isAdmin: false); // revoke only the student session

            Assert.False((await _service.FindByRawTokenAsync(studentRaw, isAdmin: false))!.IsActive);
            Assert.True((await _service.FindByRawTokenAsync(adminRaw, isAdmin: true))!.IsActive); // untouched
        }
    }
}
