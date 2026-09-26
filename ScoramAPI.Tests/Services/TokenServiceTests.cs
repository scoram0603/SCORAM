using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using ScoramAPI.Enums;
using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    public class TokenServiceTests
    {
        private static ITokenService BuildTokenService(string expiryMinutes = "60", string refreshExpiryDays = "30")
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "unit-test-signing-key-at-least-32-bytes-long!!", // HMAC-SHA256 needs >= 32 bytes
                    ["Jwt:Issuer"] = "ScoramAPI.Tests",
                    ["Jwt:Audience"] = "ScoramAPI.Tests.Client",
                    ["Jwt:ExpiryMinutes"] = expiryMinutes,
                    ["Jwt:RefreshExpiryDays"] = refreshExpiryDays,
                })
                .Build();

            return new TokenService(config);
        }

        private static User BuildUser(Guid? securityStamp = null) => new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser",
            Email = "test@example.com",
            FullName = "Test User",
            PasswordHash = "irrelevant-for-this-test",
            SecurityStamp = securityStamp ?? Guid.NewGuid(),
        };

        private static Admin BuildAdmin(AdminRole role = AdminRole.Admin, bool mustChangePassword = false, Guid? securityStamp = null) => new Admin
        {
            Id = Guid.NewGuid(),
            Email = "admin@example.com",
            FullName = "Test Admin",
            PasswordHash = "irrelevant-for-this-test",
            Role = role,
            MustChangePassword = mustChangePassword,
            SecurityStamp = securityStamp ?? Guid.NewGuid(),
        };

        private static JwtSecurityToken Decode(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

        [Fact]
        public void GenerateToken_IncludesStampClaimMatchingUser()
        {
            var stamp = Guid.NewGuid();
            var user = BuildUser(securityStamp: stamp);
            var service = BuildTokenService();

            var (token, _) = service.GenerateToken(user);
            var jwt = Decode(token);

            Assert.Equal(stamp.ToString(), jwt.Claims.Single(c => c.Type == "stamp").Value);
        }

        [Fact]
        public void GenerateToken_SetsRoleClaimToStudent()
        {
            var service = BuildTokenService();
            var (token, _) = service.GenerateToken(BuildUser());
            var jwt = Decode(token);

            Assert.Equal("Student", jwt.Claims.Single(c => c.Type == ClaimTypesRole).Value);
        }

        [Theory]
        [InlineData(60)]
        [InlineData(15)]
        public void GenerateToken_ExpiresAfterConfiguredMinutes_NotTheOldTwentyFourHourDefault(int minutes)
        {
            // Regression guard for the session-hardening change: this used to default to 1440
            // (24 hours) with no rotation/revocation story at all. Asserting against the *configured*
            // value (not a hardcoded 60) so this test documents intent without becoming a tautology --
            // if a future change silently widens the default back toward a day, this still catches it
            // via the two very different InlineData values above rather than passing coincidentally.
            var service = BuildTokenService(expiryMinutes: minutes.ToString());
            var before = DateTime.UtcNow;

            var (_, expiresAt) = service.GenerateToken(BuildUser());

            var actualMinutes = (expiresAt - before).TotalMinutes;
            Assert.InRange(actualMinutes, minutes - 1, minutes + 1); // small tolerance for test execution time
            Assert.True(expiresAt < before.AddHours(24), "Access tokens must not default back to a 24-hour lifetime.");
        }

        [Fact]
        public void GenerateAdminToken_IncludesMustChangePasswordClaim_WhenTrue()
        {
            var service = BuildTokenService();
            var (token, _) = service.GenerateAdminToken(BuildAdmin(mustChangePassword: true));
            var jwt = Decode(token);

            Assert.Equal("True", jwt.Claims.Single(c => c.Type == "mustChangePassword").Value);
        }

        [Fact]
        public void GenerateAdminToken_IncludesMustChangePasswordClaim_WhenFalse()
        {
            // Explicitly checked as its own case rather than assumed: MustChangePasswordFilter does a
            // literal string comparison against "True" (see that filter's own code), so this claim
            // being present-but-"False" needs to actually read "False", not be absent/null/"0".
            var service = BuildTokenService();
            var (token, _) = service.GenerateAdminToken(BuildAdmin(mustChangePassword: false));
            var jwt = Decode(token);

            Assert.Equal("False", jwt.Claims.Single(c => c.Type == "mustChangePassword").Value);
        }

        [Theory]
        [InlineData(AdminRole.Admin, "Admin")]
        [InlineData(AdminRole.SuperAdmin, "SuperAdmin")]
        public void GenerateAdminToken_SetsRoleClaimToActualAdminRole_NeverStudent(AdminRole role, string expected)
        {
            var service = BuildTokenService();
            var (token, _) = service.GenerateAdminToken(BuildAdmin(role: role));
            var jwt = Decode(token);

            Assert.Equal(expected, jwt.Claims.Single(c => c.Type == ClaimTypesRole).Value);
        }

        [Fact]
        public void GenerateRefreshToken_ProducesUrlSafeStringWithNoPadding()
        {
            // Refresh tokens are sent back and forth as plain query/body strings -- '+', '/', '='
            // (standard Base64) would need extra encoding; this uses a URL-safe alphabet with padding
            // stripped instead (see GenerateRefreshToken's own comment).
            var service = BuildTokenService();

            var (rawToken, _) = service.GenerateRefreshToken();

            Assert.DoesNotContain('+', rawToken);
            Assert.DoesNotContain('/', rawToken);
            Assert.DoesNotContain('=', rawToken);
        }

        [Fact]
        public void GenerateRefreshToken_ProducesADifferentValueEachCall()
        {
            var service = BuildTokenService();

            var (first, _) = service.GenerateRefreshToken();
            var (second, _) = service.GenerateRefreshToken();

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void HashRefreshToken_IsDeterministicAndCaseSensitiveToInput()
        {
            var service = BuildTokenService();

            var hash1 = service.HashRefreshToken("some-raw-token-value");
            var hash2 = service.HashRefreshToken("some-raw-token-value");
            var hash3 = service.HashRefreshToken("Some-Raw-Token-Value");

            Assert.Equal(hash1, hash2); // same input -> same hash, needed to look it up later by hash
            Assert.NotEqual(hash1, hash3);
        }

        [Fact]
        public void HashRefreshToken_NeverReturnsTheRawTokenItself()
        {
            // The entire point (see RefreshToken model's own comment): only a hash is ever stored,
            // so a leaked database dump alone can't be replayed as a live refresh token.
            var service = BuildTokenService();
            var raw = "a-raw-refresh-token-value";

            var hash = service.HashRefreshToken(raw);

            Assert.NotEqual(raw, hash);
        }

        // System.Security.Claims.ClaimTypes.Role is a long URI, not a short string -- this local
        // constant just keeps the assertions above readable rather than repeating that URI everywhere.
        private const string ClaimTypesRole = System.Security.Claims.ClaimTypes.Role;
    }
}
