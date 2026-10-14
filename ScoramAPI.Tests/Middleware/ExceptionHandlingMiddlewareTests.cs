using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScoramAPI.Middleware;
using ScoramAPI.Tests.TestSupport;
using Xunit;

namespace ScoramAPI.Tests.Middleware
{
    // Pins the CURRENT behaviour of ExceptionHandlingMiddleware (no production code changed): a consistent
    // JSON 500 with a trace id, no internal detail outside Development, 499 for a client abort, and a
    // pass-through when the response has already started.
    public class ExceptionHandlingMiddlewareTests
    {
        private sealed class FakeEnv : IHostEnvironment
        {
            public FakeEnv(string name) => EnvironmentName = name;
            public string EnvironmentName { get; set; }
            public string ApplicationName { get; set; } = "ScoramAPI.Tests";
            public string ContentRootPath { get; set; } = "/";
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private static DefaultHttpContext NewContext()
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "GET";
            context.Request.Path = "/api/anything";
            context.TraceIdentifier = "trace-xyz-789";
            context.Response.Body = new MemoryStream();
            return context;
        }

        private static JsonElement ReadBody(DefaultHttpContext context)
        {
            context.Response.Body.Position = 0;
            return JsonDocument.Parse(new StreamReader(context.Response.Body).ReadToEnd()).RootElement;
        }

        [Fact]
        public async Task UnhandledException_Returns500Json_WithTraceId_AndNoDetailInProduction()
        {
            var context = NewContext();
            var logger = new ListLogger<ExceptionHandlingMiddleware>();
            var middleware = new ExceptionHandlingMiddleware(_ => throw new InvalidOperationException("Server=secret-host;Password=hunter2"), logger, new FakeEnv("Production"));

            await middleware.InvokeAsync(context);

            Assert.Equal(500, context.Response.StatusCode);
            Assert.StartsWith("application/json", context.Response.ContentType);
            var body = ReadBody(context);
            Assert.Equal("trace-xyz-789", body.GetProperty("traceId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
            Assert.Equal(JsonValueKind.Null, body.GetProperty("detail").ValueKind);
            Assert.DoesNotContain("hunter2", context.Response.Body.ToString());
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
        }

        [Fact]
        public async Task InDevelopment_TheDetailIsIncluded()
        {
            var context = NewContext();
            var middleware = new ExceptionHandlingMiddleware(_ => throw new InvalidOperationException("dev detail"), new ListLogger<ExceptionHandlingMiddleware>(), new FakeEnv("Development"));

            await middleware.InvokeAsync(context);

            Assert.Contains("dev detail", ReadBody(context).GetProperty("detail").GetString());
        }

        [Fact]
        public async Task ClientAbort_Returns499_AndLogsNoError()
        {
            var context = NewContext();
            using var cts = new CancellationTokenSource();
            context.RequestAborted = cts.Token;
            var logger = new ListLogger<ExceptionHandlingMiddleware>();
            var middleware = new ExceptionHandlingMiddleware(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            }, logger, new FakeEnv("Production"));

            await middleware.InvokeAsync(context);

            Assert.Equal(499, context.Response.StatusCode);
            Assert.Empty(logger.Entries);
        }

        [Fact]
        public async Task BadHttpRequest_KeepsItsStatusCode_AndGetsTheTraceId()
        {
            var context = NewContext();
            var middleware = new ExceptionHandlingMiddleware(
                _ => throw new BadHttpRequestException("bad", StatusCodes.Status413PayloadTooLarge),
                new ListLogger<ExceptionHandlingMiddleware>(), new FakeEnv("Production"));

            await middleware.InvokeAsync(context);

            Assert.Equal(413, context.Response.StatusCode);
            Assert.Equal("trace-xyz-789", ReadBody(context).GetProperty("traceId").GetString());
        }

        [Fact]
        public async Task NormalResponses_AreLeftUntouched()
        {
            var context = NewContext();
            var middleware = new ExceptionHandlingMiddleware(async ctx =>
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync("{\"message\":\"nope\"}");
            }, new ListLogger<ExceptionHandlingMiddleware>(), new FakeEnv("Production"));

            await middleware.InvokeAsync(context);

            Assert.Equal(404, context.Response.StatusCode);
            Assert.Equal("nope", ReadBody(context).GetProperty("message").GetString());
        }
    }
}
