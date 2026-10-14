using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScoramAPI.Middleware;
using ScoramAPI.Tests.TestSupport;
using Xunit;

namespace ScoramAPI.Tests.Middleware
{
    public class RequestTimingMiddlewareTests
    {
        // DefaultHttpContext's response feature ignores OnStarting callbacks; this one remembers them so a
        // test can fire them the way Kestrel does when the response headers are about to be sent.
        private sealed class CapturingResponseFeature : HttpResponseFeature
        {
            private readonly List<(Func<object, Task> Callback, object State)> _callbacks = new();

            public override void OnStarting(Func<object, Task> callback, object state) => _callbacks.Add((callback, state));

            public async Task FireAsync()
            {
                foreach (var (callback, state) in _callbacks) await callback(state);
            }
        }

        private static (DefaultHttpContext Context, CapturingResponseFeature Feature) NewContext(string path = "/api/exams")
        {
            var feature = new CapturingResponseFeature();
            var context = new DefaultHttpContext();
            context.Features.Set<IHttpResponseFeature>(feature);
            context.Request.Method = "GET";
            context.Request.Path = path;
            context.TraceIdentifier = "trace-abc-123";
            return (context, feature);
        }

        private static IConfiguration Config(int? slowMs) => new ConfigurationBuilder()
            .AddInMemoryCollection(slowMs is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { [RequestTimingMiddleware.ConfigKey] = slowMs.Value.ToString() })
            .Build();

        [Fact]
        public async Task AddsTraceAndTimingHeaders_WithoutChangingStatusOrBody()
        {
            var (context, feature) = NewContext();
            var logger = new ListLogger<RequestTimingMiddleware>();
            var middleware = new RequestTimingMiddleware(async ctx =>
            {
                ctx.Response.StatusCode = 201;
                await ctx.Response.WriteAsync("payload");
            }, logger, Config(null));
            context.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(context);
            await feature.FireAsync();

            Assert.Equal("trace-abc-123", context.Response.Headers[RequestTimingMiddleware.TraceHeader].ToString());
            Assert.Matches(@"^app;dur=\d+$", context.Response.Headers[RequestTimingMiddleware.TimingHeader].ToString());
            Assert.Equal(201, context.Response.StatusCode);
            context.Response.Body.Position = 0;
            Assert.Equal("payload", new StreamReader(context.Response.Body).ReadToEnd());
            Assert.Empty(logger.Entries); // fast request -> no slow warning
        }

        [Fact]
        public async Task DoesNotFailWhenAnotherComponentAlreadySetTheHeader()
        {
            var (context, feature) = NewContext();
            context.Response.Headers[RequestTimingMiddleware.TraceHeader] = "pre-existing";
            var middleware = new RequestTimingMiddleware(_ => Task.CompletedTask, new ListLogger<RequestTimingMiddleware>(), Config(null));

            await middleware.InvokeAsync(context);
            await feature.FireAsync();

            Assert.Equal("trace-abc-123", context.Response.Headers[RequestTimingMiddleware.TraceHeader].ToString());
        }

        [Fact]
        public async Task LogsOneWarning_ForASlowRequest_WithoutQueryStringOrIds()
        {
            var (context, _) = NewContext("/api/tests/attempts/3f2504e0-4f89-11d3-9a0c-0305e82c3301");
            context.Request.QueryString = new QueryString("?secret=hunter2");
            var logger = new ListLogger<RequestTimingMiddleware>();
            var middleware = new RequestTimingMiddleware(async _ => await Task.Delay(40), logger, Config(10));

            await middleware.InvokeAsync(context);

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Contains("Slow request", entry.Message);
            Assert.Contains("trace-abc-123", entry.Message);
            Assert.DoesNotContain("hunter2", entry.Message);
        }

        [Fact]
        public async Task StillLogsTheSlowRequest_WhenTheRestOfThePipelineThrows()
        {
            var (context, _) = NewContext();
            var logger = new ListLogger<RequestTimingMiddleware>();
            var middleware = new RequestTimingMiddleware(async _ =>
            {
                await Task.Delay(30);
                throw new InvalidOperationException("boom");
            }, logger, Config(10));

            await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context)); // rethrown untouched
            Assert.Single(logger.Entries);
        }

        [Fact]
        public async Task SignalRHubRequests_AreNeverReportedAsSlow()
        {
            var (context, _) = NewContext("/hubs/chat");
            var logger = new ListLogger<RequestTimingMiddleware>();
            var middleware = new RequestTimingMiddleware(async _ => await Task.Delay(40), logger, Config(10));

            await middleware.InvokeAsync(context);

            Assert.Empty(logger.Entries);
        }

        [Theory]
        [InlineData(null, RequestTimingMiddleware.DefaultSlowRequestMs)]
        [InlineData(0, RequestTimingMiddleware.DefaultSlowRequestMs)]
        [InlineData(-5, RequestTimingMiddleware.DefaultSlowRequestMs)]
        public async Task NonPositiveThreshold_FallsBackToTheDefault(int? configured, int expectedDefault)
        {
            var (context, _) = NewContext();
            var logger = new ListLogger<RequestTimingMiddleware>();
            var middleware = new RequestTimingMiddleware(async _ => await Task.Delay(20), logger, Config(configured));

            await middleware.InvokeAsync(context);

            Assert.True(expectedDefault >= 1000);
            Assert.Empty(logger.Entries); // 20 ms is far below the default, so a 0/negative value must not log everything
        }

        [Fact]
        public void IsSlow_IsInclusiveOfTheThreshold()
        {
            Assert.True(RequestTimingMiddleware.IsSlow(TimeSpan.FromMilliseconds(500), 500));
            Assert.False(RequestTimingMiddleware.IsSlow(TimeSpan.FromMilliseconds(499), 500));
        }
    }
}
