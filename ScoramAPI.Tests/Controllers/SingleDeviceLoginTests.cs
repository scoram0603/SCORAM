using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using ScoramAPI.Controllers;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Controllers
{
    // SINGLE-DEVICE LOGIN -- covers scenarios A, B, C, E, I, J, K and N from the brief's own test
    // list (see AuthController.IssueAuthResponseAsync and Refresh() for the implementation this
    // exercises).
    //
    // NOT covered here, and why:
    //   D, F (a superseding device's own refresh succeeding) -- AuthController.Refresh()'s success
    //     path calls IRefreshTokenService.RotateAsync, which needs a real transaction and
    //     ExecuteUpdateAsync; the EF Core in-memory provider supports neither (see
    //     RefreshTokenServiceTests.RotateAsync_RevokesTheOldTokenAndPersistsTheReplacement, already
    //     skipped for the same reason before this feature existed). "A session superseded by a
    //     login is told SESSION_REVOKED and NOT nuked" (this file's own
    //     SupersededDevice_RefreshAttempt tests) is what actually proves this feature doesn't harm
    //     the new device; a full successful refresh needs a relational test DB (SQL Server via a
    //     container, or SQLite) per the project's existing "Testing" README note.
    //   G, H (Flutter/React client logout UI) -- client-side, not reachable from this project.
    //   L (SignalR) -- this project has no SignalR hub test harness.
    //   M (existing users not invalidated by deployment) -- the OMIT-sid-claim-when-null half is
    //     covered in TokenServiceTests.GenerateToken_OmitsSidClaimEntirely_WhenActiveSessionIdIsNull;
    //     the "Program.cs's OnTokenValidated skips the sid check when the claim is absent" half lives
    //     in Program.cs's top-level JwtBearerEvents lambda, which isn't unit-testable from here --
    //     verify that one by reading OnTokenValidated directly, or via the manual checklist.
    public class SingleDeviceLoginTests : IDisposable
    {
        private readonly ScoramDbContext _db;
        private readonly ITokenService _tokenService;
        private readonly IRefreshTokenService _refreshTokens;
        private readonly ISecurityStampService _securityStamps;
        private readonly AuthController _controller;

        public SingleDeviceLoginTests()
        {
            var options = new DbContextOptionsBuilder<ScoramDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _db = new ScoramDbContext(options);

            var config = new ConfigurationBuilder()
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
            _refreshTokens = new RefreshTokenService(_db, _tokenService);

            // Real IDistributedCache, not a mock -- see SecurityStampServiceTests' own comment on why.
            IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            _securityStamps = new SecurityStampService(_db, cache);

            var captcha = new Mock<ICaptchaService>();
            captcha.Setup(c => c.Verify(It.IsAny<string>(), It.IsAny<int>())).Returns(true);

            _controller = new AuthController(
                _db, _tokenService, _refreshTokens, _securityStamps,
                fileStorage: Mock.Of<IFileStorageService>(),
                gamification: Mock.Of<IGamificationService>(),
                msg91: Mock.Of<IMsg91Service>(),
                captcha: captcha.Object,
                config: config);
        }

        public void Dispose() => _db.Dispose();

        private const string Password = "a-strong-enough-test-password-1";

        private async Task<User> SeedStudentAsync()
        {
            var user = new User
            {
                Username = "singledevicetester",
                Email = "singledevice@example.com",
                FullName = "Single Device Tester",
                PhoneNumber = "9000000000",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
                IsActive = true,
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            return user;
        }

        private static LoginDto LoginRequest() => new()
        {
            Identifier = "singledevicetester",
            Password = Password,
            CaptchaId = "irrelevant-mocked-to-pass",
            CaptchaAnswer = 0,
        };

        private static object? AnonProp(object obj, string name) => obj.GetType().GetProperty(name)?.GetValue(obj);

        // ---- A, B: logging in on a first, then a second, device ---------------------------------

        [Fact]
        public async Task LoginOnDeviceA_Succeeds()
        {
            await SeedStudentAsync();

            var result = await _controller.Login(LoginRequest());

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var body = Assert.IsType<AuthResponseDto>(ok.Value);
            Assert.False(string.IsNullOrEmpty(body.Token));
            Assert.False(string.IsNullOrEmpty(body.RefreshToken));
        }

        [Fact]
        public async Task LoginOnDeviceB_AfterDeviceA_AlsoSucceeds()
        {
            await SeedStudentAsync();

            await _controller.Login(LoginRequest()); // device A
            var resultB = await _controller.Login(LoginRequest()); // device B

            var ok = Assert.IsType<OkObjectResult>(resultB.Result);
            Assert.IsType<AuthResponseDto>(ok.Value);
        }

        // ---- C, E: the superseded device's next refresh attempt ---------------------------------

        [Fact]
        public async Task SupersededDevice_RefreshAttempt_ReturnsSessionRevokedCode_NotGenericMessage()
        {
            await SeedStudentAsync();

            var deviceA = Assert.IsType<AuthResponseDto>(((OkObjectResult)(await _controller.Login(LoginRequest())).Result!).Value);
            await _controller.Login(LoginRequest()); // device B logs in, supersedes device A

            var refreshResult = await _controller.Refresh(new RefreshTokenRequestDto { RefreshToken = deviceA.RefreshToken });

            var unauthorized = Assert.IsType<UnauthorizedObjectResult>(refreshResult.Result);
            Assert.Equal("SESSION_REVOKED", AnonProp(unauthorized.Value!, "code"));
            Assert.Equal("Your account was logged in on another device.", AnonProp(unauthorized.Value!, "message"));
        }

        [Fact]
        public async Task SupersededDevice_RefreshAttempt_DoesNotRevokeTheNewDevicesSession()
        {
            // The collateral-damage case this feature has to avoid -- see Refresh()'s own comment on
            // why RevokedReasonSupersededByLogin has to be checked BEFORE the theft-response branch.
            await SeedStudentAsync();

            var deviceA = Assert.IsType<AuthResponseDto>(((OkObjectResult)(await _controller.Login(LoginRequest())).Result!).Value);
            var deviceB = Assert.IsType<AuthResponseDto>(((OkObjectResult)(await _controller.Login(LoginRequest())).Result!).Value);

            await _controller.Refresh(new RefreshTokenRequestDto { RefreshToken = deviceA.RefreshToken }); // device A's stale attempt

            var deviceBToken = await _refreshTokens.FindByRawTokenAsync(deviceB.RefreshToken, isAdmin: false);
            Assert.NotNull(deviceBToken);
            Assert.True(deviceBToken!.IsActive); // device B's session must survive device A's rejected attempt

            // And device B's access token ("sid" claim) must still validate.
            var sidClaim = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
                .ReadJwtToken(deviceB.Token).Claims.Single(c => c.Type == "sid").Value;
            Assert.True(await _securityStamps.IsSessionValidAsync(deviceB.UserId, sidClaim));
        }

        [Fact]
        public async Task TwoSupersededDevices_BothGetSessionRevoked_NeitherIsTreatedAsTheft()
        {
            await SeedStudentAsync();

            var deviceA = Assert.IsType<AuthResponseDto>(((OkObjectResult)(await _controller.Login(LoginRequest())).Result!).Value);
            var deviceB = Assert.IsType<AuthResponseDto>(((OkObjectResult)(await _controller.Login(LoginRequest())).Result!).Value);
            await _controller.Login(LoginRequest()); // device C -- now supersedes both A and B

            var refreshA = await _controller.Refresh(new RefreshTokenRequestDto { RefreshToken = deviceA.RefreshToken });
            var refreshB = await _controller.Refresh(new RefreshTokenRequestDto { RefreshToken = deviceB.RefreshToken });

            Assert.Equal("SESSION_REVOKED", AnonProp(Assert.IsType<UnauthorizedObjectResult>(refreshA.Result).Value!, "code"));
            Assert.Equal("SESSION_REVOKED", AnonProp(Assert.IsType<UnauthorizedObjectResult>(refreshB.Result).Value!, "code"));
        }

        // ---- I: a normal (self-initiated) logout is unaffected by this feature -------------------

        [Fact]
        public async Task NormalLogout_StillProducesTheOriginalGenericMessage_NotSessionRevoked()
        {
            var user = await SeedStudentAsync();
            var device = Assert.IsType<AuthResponseDto>(((OkObjectResult)(await _controller.Login(LoginRequest())).Result!).Value);

            // Simulate AuthController.Logout()'s own two steps (see that method) without needing a
            // populated ClaimsPrincipal/[Authorize] context in this unit test.
            await _refreshTokens.RevokeAllAsync(user.Id, isAdmin: false);

            var refreshResult = await _controller.Refresh(new RefreshTokenRequestDto { RefreshToken = device.RefreshToken });

            var unauthorized = Assert.IsType<UnauthorizedObjectResult>(refreshResult.Result);
            Assert.Null(AnonProp(unauthorized.Value!, "code")); // no code on the pre-existing generic path
            Assert.Equal("This session is no longer valid. Please log in again.", AnonProp(unauthorized.Value!, "message"));
        }

        // ---- J, K: re-login on the same device, and several logins in a row ----------------------

        [Fact]
        public async Task SameDeviceReLogin_Succeeds_AndSupersedesItsOwnPriorSession()
        {
            // The brief explicitly allows this (scenario J) -- the server has no notion of "device",
            // only of "login event", so a second login from the very same device behaves exactly like
            // a second device: it works, and it supersedes whatever came before it.
            await SeedStudentAsync();

            var first = Assert.IsType<AuthResponseDto>(((OkObjectResult)(await _controller.Login(LoginRequest())).Result!).Value);
            var second = Assert.IsType<AuthResponseDto>(((OkObjectResult)(await _controller.Login(LoginRequest())).Result!).Value);

            var refreshFirst = await _controller.Refresh(new RefreshTokenRequestDto { RefreshToken = first.RefreshToken });
            Assert.Equal("SESSION_REVOKED", AnonProp(Assert.IsType<UnauthorizedObjectResult>(refreshFirst.Result).Value!, "code"));

            var secondToken = await _refreshTokens.FindByRawTokenAsync(second.RefreshToken, isAdmin: false);
            Assert.True(secondToken!.IsActive);
        }

        [Fact]
        public async Task SeveralLoginsInARow_OnlyTheLatestSessionRemainsActive()
        {
            await SeedStudentAsync();

            var logins = new List<AuthResponseDto>();
            for (int i = 0; i < 4; i++)
                logins.Add(Assert.IsType<AuthResponseDto>(((OkObjectResult)(await _controller.Login(LoginRequest())).Result!).Value));

            for (int i = 0; i < logins.Count - 1; i++)
            {
                var token = await _refreshTokens.FindByRawTokenAsync(logins[i].RefreshToken, isAdmin: false);
                Assert.False(token!.IsActive);
            }

            var latest = await _refreshTokens.FindByRawTokenAsync(logins[^1].RefreshToken, isAdmin: false);
            Assert.True(latest!.IsActive);
        }

        // ---- N: multiple tabs sharing one login are not treated as multiple devices --------------

        [Fact]
        public async Task OneLogin_ValidatesRepeatedlyAcrossWhatWouldBeMultipleBrowserTabs()
        {
            // Nothing here is tab-aware -- several "requests" (checks) against the SAME issued
            // session must all keep succeeding without a second login, exactly as several tabs
            // sharing one access token would behave in the browser.
            await SeedStudentAsync();
            var device = Assert.IsType<AuthResponseDto>(((OkObjectResult)(await _controller.Login(LoginRequest())).Result!).Value);
            var sidClaim = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
                .ReadJwtToken(device.Token).Claims.Single(c => c.Type == "sid").Value;

            Assert.True(await _securityStamps.IsSessionValidAsync(device.UserId, sidClaim)); // tab 1
            Assert.True(await _securityStamps.IsSessionValidAsync(device.UserId, sidClaim)); // tab 2
            Assert.True(await _securityStamps.IsSessionValidAsync(device.UserId, sidClaim)); // tab 3
        }

        // SKIPPED, not fixed -- see this class's own header comment (D, F) and
        // RefreshTokenServiceTests.RotateAsync_RevokesTheOldTokenAndPersistsTheReplacement.
        [Fact(Skip = "AuthController.Refresh()'s success path calls RotateAsync, which needs a real transaction and ExecuteUpdateAsync; the EF Core in-memory provider supports neither. Needs a relational test DB.")]
        public async Task SupersedingDevice_CanStillSuccessfullyRefreshItsOwnNewSession()
        {
            await SeedStudentAsync();
            var device = Assert.IsType<AuthResponseDto>(((OkObjectResult)(await _controller.Login(LoginRequest())).Result!).Value);

            var refreshResult = await _controller.Refresh(new RefreshTokenRequestDto { RefreshToken = device.RefreshToken });

            Assert.IsType<OkObjectResult>(refreshResult.Result);
        }
    }
}
