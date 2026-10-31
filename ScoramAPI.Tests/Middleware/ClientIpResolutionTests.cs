using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using ScoramAPI.Middleware;
using ScoramAPI.Tests.TestSupport;
using Xunit;

namespace ScoramAPI.Tests.Middleware
{
    /// <summary>
    /// SEC-004 -- effective client IP + IP-partitioned rate limiting.
    ///
    /// These run the REAL middleware (ASP.NET Core's ForwardedHeadersMiddleware in trusted mode, this project's
    /// InterimClientIpMiddleware otherwise, and the real RateLimiter) in a pipeline built with ApplicationBuilder.
    /// What they cannot show: Program.cs's registration order and Render's real proxy topology. Peer addresses
    /// below are RFC 1918 / documentation-range placeholders chosen for the tests -- they are NOT claims about Render.
    /// </summary>
    public class ClientIpResolutionTests
    {
        private const string PrivatePeer = "10.20.30.40";        // stands for "some peer inside the hosting network"
        private const string PublicPeer = "203.0.113.50";         // TEST-NET-3: a caller on the public internet
        private const string ClientA = "198.51.100.10";           // TEST-NET-2
        private const string ClientB = "198.51.100.11";

        // ---- pipeline harness ----------------------------------------------------------------------------

        private sealed class Probe
        {
            public IPAddress? SeenByPolicy;        // RemoteIpAddress when the rate-limit partition key was computed
            public IPAddress? SeenByEndpoint;
            public string? SchemeSeenByEndpoint;
        }

        private static (RequestDelegate Pipeline, Probe Probe) Build(ClientIpOptions options, int permitLimit = 2)
        {
            var probe = new Probe();
            var services = new ServiceCollection()
                .AddLogging()
                .AddRateLimiter(o =>
                {
                    // Same partition helper the real login/refresh/register policies use (Program.cs).
                    o.AddPolicy("ip-limited", ctx =>
                    {
                        probe.SeenByPolicy = ctx.Connection.RemoteIpAddress;
                        return RateLimitPartition.GetFixedWindowLimiter(
                            ClientIpResolver.PartitionKey(ctx),
                            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromHours(1), QueueLimit = 0 });
                    });
                    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                })
                .BuildServiceProvider();

            var app = new ApplicationBuilder(services);
            app.UseClientIpResolution(options);   // same call, same position (first) as Program.cs
            app.UseRateLimiter();
            app.Run(ctx =>
            {
                probe.SeenByEndpoint = ctx.Connection.RemoteIpAddress;
                probe.SchemeSeenByEndpoint = ctx.Request.Scheme;
                return Task.CompletedTask;
            });
            return (app.Build(), probe);
        }

        private static DefaultHttpContext Request(string peer, string? xff = null, string? proto = null)
        {
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            ctx.Request.Scheme = "http";
            if (xff != null) ctx.Request.Headers["X-Forwarded-For"] = xff;
            if (proto != null) ctx.Request.Headers["X-Forwarded-Proto"] = proto;
            // [EnableRateLimiting("ip-limited")] as on AuthController.Login
            ctx.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EnableRateLimitingAttribute("ip-limited")), "test"));
            return ctx;
        }

        private static async Task<(int Status, IPAddress? Effective, Probe Probe)> Send(
            (RequestDelegate Pipeline, Probe Probe) built, string peer, string? xff = null, string? proto = null)
        {
            var ctx = Request(peer, xff, proto);
            built.Probe.SeenByEndpoint = null;
            await built.Pipeline(ctx);
            return (ctx.Response.StatusCode, built.Probe.SeenByEndpoint, built.Probe);
        }

        private static ClientIpOptions Interim(int skip = 0) => new() { SkipRightmostEntries = skip };
        private static ClientIpOptions Trusted(string[]? networks = null, string[]? proxies = null, int? limit = null)
            => new() { TrustedNetworks = networks ?? Array.Empty<string>(), TrustedProxies = proxies ?? Array.Empty<string>(), ForwardLimit = limit };

        // ---- 1. no forwarded headers -> connection peer -----------------------------------------------------

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NoForwardedHeaders_UsesTheConnectionPeer(bool trustedMode)
        {
            var built = Build(trustedMode ? Trusted(new[] { "10.0.0.0/8" }) : Interim());
            var r = await Send(built, PrivatePeer);
            Assert.Equal(IPAddress.Parse(PrivatePeer), r.Effective);
        }

        // ---- 2. forged header from an untrusted caller cannot choose the IP --------------------------------

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task UntrustedPublicCaller_CannotChooseItsIpWithAForgedHeader(bool trustedMode)
        {
            var built = Build(trustedMode ? Trusted(new[] { "10.0.0.0/8" }) : Interim());
            var r = await Send(built, PublicPeer, xff: ClientA, proto: "https");
            Assert.Equal(IPAddress.Parse(PublicPeer), r.Effective);
            Assert.Equal("http", r.Probe.SchemeSeenByEndpoint); // scheme can't be declared by an unverified caller either
        }

        [Fact]
        public async Task ForgedHeaderCannotEvadeALimit_RotatingTheValueKeepsTheSameBucket()
        {
            foreach (var trustedMode in new[] { false, true })
            {
                var built = Build(trustedMode ? Trusted(new[] { "10.0.0.0/8" }) : Interim(), permitLimit: 2);
                Assert.Equal(200, (await Send(built, PublicPeer, xff: "198.51.100.1")).Status);
                Assert.Equal(200, (await Send(built, PublicPeer, xff: "198.51.100.2")).Status);
                Assert.Equal(429, (await Send(built, PublicPeer, xff: "198.51.100.3")).Status);
            }
        }

        // ---- 3 + 5. trusted proxy: forwarded info used according to the hop configuration ----------------------

        [Fact]
        public async Task TrustedMode_WalksTrustedHopsAndStopsAtTheFirstUntrustedOne_NotFirstNorLastBlindly()
        {
            // peer(10.20.30.40, trusted) -> header: "spoofed, real client, internal proxy"
            var built = Build(Trusted(new[] { "10.0.0.0/8" }));
            var r = await Send(built, PrivatePeer, xff: $"192.0.2.99, {ClientA}, 10.1.1.1");
            Assert.Equal(IPAddress.Parse(ClientA), r.Effective);   // neither the spoofed leftmost nor the rightmost proxy
        }

        [Fact]
        public async Task TrustedMode_ForwardLimitBoundsHowManyEntriesAreProcessed()
        {
            var built = Build(Trusted(new[] { "10.0.0.0/8" }, limit: 1));
            var r = await Send(built, PrivatePeer, xff: $"192.0.2.99, {ClientA}, 10.1.1.1");
            Assert.Equal(IPAddress.Parse("10.1.1.1"), r.Effective);  // only one entry from the right is consumed
        }

        [Fact]
        public async Task TrustedMode_ExactProxyAddressIsTrusted_AndAnotherAddressIsNot()
        {
            var built = Build(Trusted(proxies: new[] { PrivatePeer }));
            Assert.Equal(IPAddress.Parse(ClientA), (await Send(built, PrivatePeer, xff: ClientA)).Effective);
            Assert.Equal(IPAddress.Parse("10.99.99.99"), (await Send(built, "10.99.99.99", xff: ClientA)).Effective);
        }

        [Fact]
        public async Task TrustedMode_ForwardedProtoHonouredOnlyFromTrustedPeer()
        {
            var built = Build(Trusted(new[] { "10.0.0.0/8" }));
            Assert.Equal("https", (await Send(built, PrivatePeer, xff: ClientA, proto: "https")).Probe.SchemeSeenByEndpoint);
            Assert.Equal("http", (await Send(built, PublicPeer, xff: ClientA, proto: "https")).Probe.SchemeSeenByEndpoint);
        }

        // ---- interim mode (nothing configured) ---------------------------------------------------------------

        [Fact]
        public async Task Interim_PrivatePeer_UsesRightmostEntry_AndIgnoresForgedPrefix()
        {
            var built = Build(Interim());
            var r = await Send(built, PrivatePeer, xff: $"192.0.2.99, 192.0.2.98, {ClientA}");
            Assert.Equal(IPAddress.Parse(ClientA), r.Effective);
        }

        [Fact]
        public async Task Interim_SkipRightmostEntries_SelectsTheConfiguredEntry_AndNeverFallsBackToAnother()
        {
            var built = Build(Interim(skip: 2));
            // client, platform-edge, platform-internal
            Assert.Equal(IPAddress.Parse(ClientA), (await Send(built, PrivatePeer, xff: $"192.0.2.99, {ClientA}, 172.71.0.1, 10.1.1.1")).Effective);
            // chain shorter than the configured depth: the request did not traverse the expected proxies -> not trusted
            Assert.Equal(IPAddress.Parse(PrivatePeer), (await Send(built, PrivatePeer, xff: $"{ClientA}, 10.1.1.1")).Effective);
            Assert.Equal(IPAddress.Parse(PrivatePeer), (await Send(built, PrivatePeer, xff: ClientA)).Effective);
        }

        [Fact]
        public async Task Interim_ForwardedProtoFromPrivatePeer_UsesRightmostHttpOrHttps_OnlyThen()
        {
            var built = Build(Interim());
            Assert.Equal("https", (await Send(built, PrivatePeer, xff: ClientA, proto: "http, https")).Probe.SchemeSeenByEndpoint);
            Assert.Equal("http", (await Send(built, PrivatePeer, xff: ClientA, proto: "https, javascript")).Probe.SchemeSeenByEndpoint);
        }

        // ---- 4. malformed / empty / unexpected values -------------------------------------------------------

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(" , , ")]
        [InlineData("unknown")]
        [InlineData("1")]
        [InlineData("1.2.3")]
        [InlineData("1.2.3.4.5")]
        [InlineData("010.001.001.001")]
        [InlineData("999.1.1.1")]
        [InlineData("0.0.0.0")]
        [InlineData("::")]
        [InlineData("fe80::1%eth0")]
        [InlineData("[::1")]
        [InlineData("1.2.3.4:abc")]
        [InlineData("1.2.3.4:")]
        [InlineData("evil.example.com")]
        [InlineData("198.51.100.10, unknown")]       // rightmost entry malformed -> nothing else is guessed
        public async Task Interim_MalformedRightmostEntry_KeepsThePeerAndNeverThrows(string xff)
        {
            var built = Build(Interim());
            var r = await Send(built, PrivatePeer, xff);
            Assert.Equal(200, r.Status);
            Assert.Equal(IPAddress.Parse(PrivatePeer), r.Effective);
        }

        [Fact]
        public async Task Interim_OversizedHeader_IsTreatedAsMalformed()
        {
            var built = Build(Interim());
            var huge = string.Join(",", Enumerable.Repeat("198.51.100.77", 400)); // > 2048 chars
            var r = await Send(built, PrivatePeer, huge);
            Assert.Equal(IPAddress.Parse(PrivatePeer), r.Effective);
        }

        [Theory]
        [InlineData("198.51.100.10:5678", "198.51.100.10")]
        [InlineData("[2001:db8::1]:443", "2001:db8::1")]
        [InlineData("[2001:db8::1]", "2001:db8::1")]
        [InlineData("2001:db8::1", "2001:db8::1")]
        [InlineData("::ffff:198.51.100.10", "198.51.100.10")]
        [InlineData("  198.51.100.10  ", "198.51.100.10")]
        public async Task Interim_WellFormedVariants_AreNormalised(string xff, string expected)
        {
            var built = Build(Interim());
            var r = await Send(built, PrivatePeer, xff);
            Assert.Equal(IPAddress.Parse(expected), r.Effective);
        }

        [Fact]
        public async Task TrustedMode_MalformedHeaderFromTrustedPeer_DoesNotThrow()
        {
            var built = Build(Trusted(new[] { "10.0.0.0/8" }));
            var r = await Send(built, PrivatePeer, xff: "garbage, ,, not-an-ip");
            Assert.Equal(200, r.Status);
            Assert.Equal(IPAddress.Parse(PrivatePeer), r.Effective);
        }

        // ---- 6 + 7. rate limiting partitions on the effective IP, and users don't collapse -------------------

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RateLimit_PartitionsByEffectiveClientIp_NotByTheSharedProxyPeer(bool trustedMode)
        {
            var built = Build(trustedMode ? Trusted(new[] { "10.0.0.0/8" }) : Interim(), permitLimit: 2);
            // two different users arrive through the SAME proxy peer
            Assert.Equal(200, (await Send(built, PrivatePeer, xff: ClientA)).Status);
            Assert.Equal(200, (await Send(built, PrivatePeer, xff: ClientA)).Status);
            Assert.Equal(429, (await Send(built, PrivatePeer, xff: ClientA)).Status);   // A is limited ...
            Assert.Equal(200, (await Send(built, PrivatePeer, xff: ClientB)).Status);   // ... B is not
            Assert.Equal(200, (await Send(built, PrivatePeer, xff: ClientB)).Status);
        }

        [Fact]
        public async Task RateLimit_DifferentDirectPeers_DoNotShareABucket()
        {
            var built = Build(Interim(), permitLimit: 1);
            Assert.Equal(200, (await Send(built, PublicPeer)).Status);
            Assert.Equal(429, (await Send(built, PublicPeer)).Status);
            Assert.Equal(200, (await Send(built, "203.0.113.51")).Status);
        }

        [Fact]
        public async Task RateLimit_SameIpv4_SpelledAsIpv4OrMappedIpv6_IsOneBucket()
        {
            var built = Build(Interim(), permitLimit: 1);
            Assert.Equal(200, (await Send(built, PublicPeer)).Status);
            Assert.Equal(429, (await Send(built, "::ffff:" + PublicPeer)).Status);
        }

        // ---- 9. order: the effective IP exists before the rate limiter evaluates its partition ----------------

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ClientIpIsEstablishedBeforeTheRateLimiterComputesItsPartition(bool trustedMode)
        {
            var built = Build(trustedMode ? Trusted(new[] { "10.0.0.0/8" }) : Interim());
            var r = await Send(built, PrivatePeer, xff: ClientA);
            Assert.Equal(IPAddress.Parse(ClientA), r.Probe.SeenByPolicy);
        }

        // ---- configuration safety -----------------------------------------------------------------------------

        [Theory]
        [InlineData("not-an-ip")]
        [InlineData("0.0.0.0")]
        [InlineData("")]
        public void TrustedProxies_MalformedEntry_FailsFast(string entry)
            => Assert.Throws<InvalidOperationException>(() => Trusted(proxies: new[] { entry }).ToForwardedHeadersOptions());

        [Theory]
        [InlineData("10.0.0.0")]
        [InlineData("10.0.0.0/33")]
        [InlineData("10.0.0.0/0")]       // would trust every caller
        [InlineData("::/0")]
        [InlineData("10.0.0.0/abc")]
        [InlineData("nope/8")]
        public void TrustedNetworks_MalformedOrTrustAllEntry_FailsFast(string entry)
            => Assert.Throws<InvalidOperationException>(() => Trusted(new[] { entry }).ToForwardedHeadersOptions());

        [Fact]
        public void ForwardLimit_BelowOne_FailsFast()
            => Assert.Throws<InvalidOperationException>(() => Trusted(new[] { "10.0.0.0/8" }, limit: 0).ToForwardedHeadersOptions());

        [Fact]
        public void TrustedOptions_DoNotKeepTheImplicitLoopbackTrust()
        {
            var o = Trusted(new[] { "10.0.0.0/8" }).ToForwardedHeadersOptions();
            Assert.Empty(o.KnownProxies);
            Assert.Single(o.KnownNetworks);
        }

        [Fact]
        public void HasTrustedProxies_ReflectsConfiguration()
        {
            Assert.False(new ClientIpOptions().HasTrustedProxies);
            Assert.True(Trusted(new[] { "10.0.0.0/8" }).HasTrustedProxies);
            Assert.True(Trusted(proxies: new[] { "10.0.0.1" }).HasTrustedProxies);
        }

        // ---- pure helpers -------------------------------------------------------------------------------------

        [Theory]
        [InlineData("10.1.2.3", true)]
        [InlineData("172.16.0.1", true)]
        [InlineData("172.31.255.255", true)]
        [InlineData("172.32.0.1", false)]
        [InlineData("192.168.1.1", true)]
        [InlineData("127.0.0.1", true)]
        [InlineData("169.254.1.1", true)]
        [InlineData("100.64.0.1", true)]
        [InlineData("100.128.0.1", false)]
        [InlineData("8.8.8.8", false)]
        [InlineData("203.0.113.50", false)]
        [InlineData("::1", true)]
        [InlineData("fd00::1", true)]
        [InlineData("fe80::1", true)]
        [InlineData("2001:db8::1", false)]
        [InlineData("::ffff:10.1.2.3", true)]
        [InlineData("::ffff:8.8.8.8", false)]
        public void IsPrivateOrLoopback_ClassifiesAddresses(string ip, bool expected)
            => Assert.Equal(expected, ClientIpResolver.IsPrivateOrLoopback(IPAddress.Parse(ip)));

        [Fact]
        public void PartitionKey_NullAddress_IsTheSharedUnknownKey_NotAThrow()
        {
            var ctx = new DefaultHttpContext();
            Assert.Equal("unknown", ClientIpResolver.PartitionKey(ctx));
        }

        [Fact]
        public async Task Interim_PublicPeerWithForwardedFor_WarnsOnce_WithoutLoggingHeaderContent()
        {
            var logger = new ListLogger<InterimClientIpMiddleware>();
            var middleware = new InterimClientIpMiddleware(_ => Task.CompletedTask, new ClientIpOptions(), logger);
            for (var i = 0; i < 3; i++) await middleware.InvokeAsync(Request(PublicPeer, xff: ClientA));
            await middleware.InvokeAsync(Request(PrivatePeer, xff: ClientA));   // trusted path: no warning

            Assert.Single(logger.Entries);
            Assert.DoesNotContain(ClientA, logger.Entries[0].Message);
        }

        // ---- diagnostics ----------------------------------------------------------------------------------------

        [Fact]
        public async Task Diagnostics_LogsOnlyTheConfiguredNumberOfSamples_AndSanitisesHostileHeaders()
        {
            var logger = new ListLogger<ClientIpDiagnosticsMiddleware>();
            var options = new ClientIpOptions { DiagnosticSampleLimit = 2 };
            var middleware = new ClientIpDiagnosticsMiddleware(_ => Task.CompletedTask, logger, options);

            for (var i = 0; i < 5; i++)
            {
                var ctx = Request(PrivatePeer, xff: "198.51.100.10\r\nFAKE LOG LINE <script>");
                await middleware.InvokeAsync(ctx);
            }

            Assert.Equal(2, logger.Entries.Count);
            Assert.All(logger.Entries, e =>
            {
                Assert.DoesNotContain('\r', e.Message);
                Assert.DoesNotContain('\n', e.Message);
                Assert.DoesNotContain("<script>", e.Message);
            });
        }

        [Fact]
        public async Task Diagnostics_DisabledByDefault_LogsNothing()
        {
            var logger = new ListLogger<ClientIpDiagnosticsMiddleware>();
            var middleware = new ClientIpDiagnosticsMiddleware(_ => Task.CompletedTask, logger, new ClientIpOptions());
            await middleware.InvokeAsync(Request(PrivatePeer, xff: ClientA));
            Assert.Empty(logger.Entries);
        }
    }
}
